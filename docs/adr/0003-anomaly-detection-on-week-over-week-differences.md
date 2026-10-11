# ADR 0003 — Anomaly detection on week-over-week differences

- Status: Accepted
- Date: 2026-10-11

## Context

Validation (Plausibilisierung) rejects only values that are clearly impossible: negative values and
spikes above 3× the median of the surrounding hour. Many problems are subtler: a value that is high
but not absurd, or a load that suddenly doubles and stays there because a meter was swapped, a
transformer factor is wrong or a new consumer was connected. Someone should look at these values.
Removing them automatically would be wrong.

ML.NET offers two families of time-series detectors: IID (each value against a sliding window of
earlier values) and SSA (a seasonal model trained on a window first). Both report spikes and
change points; change points come from a martingale over the p-values of recent intervals.

A load profile has a strong daily and weekly shape. Experiments on the synthetic H0 and commercial
profiles showed:

- IID spike detection with a one-day window works on raw values. Yesterday's peaks are still in
  the window when today's arrive, so the daily shape is not flagged.
- Change-point detection on raw values fails both ways. With a one-day martingale window, normal
  noise drives the martingale to ~10⁻⁴⁵, and even a tripled load never raises an alert. With a
  window of a few hours, every morning ramp looks like a change. SSA with a 96-interval
  seasonality has the same martingale decay and missed the shifts too.

## Decision

1. **Spikes**: `DetectIidSpike` on the raw values, 99 % confidence, one-day history (96 intervals).
2. **Change points**: `DetectIidChangePoint` on the **difference to the same German local time
   seven days earlier**, 95 % confidence, a 12-interval (3-hour) martingale window. The difference
   removes the daily and weekday shape and leaves noise around zero until the level really shifts.
   On the synthetic profiles this finds a 1.5× shift within two intervals, with about one false
   alarm per hundred clean days.
3. **Compare by local time, not by UTC − 168 h.** Otherwise the week after a clock change compares
   18:00 with 17:00 and reports the evening ramp as a change. The skipped spring hour has no
   reference and is left out. The repeated autumn hour uses its standard-time pass.
4. **Findings are annotations, not status changes.** A `SeriesAnnotation` stores interval, type and
   score (1 − p-value). `MeasurementValue.Status` is untouched, because billing must not treat a
   suspicious metered value as a substitute.
5. **Estimated placeholders are excluded from the input.** A zero fallback is a data gap, and the
   quality score already counts it.
6. **Detection runs in the import**, over the imported days, after gap filling and before the
   single save. Earlier annotations for those days are replaced.
7. **The detector sits behind `IAnomalyDetector` in Application, implemented in Infrastructure**, so
   Application and its tests have no ML.NET dependency.

## Consequences

- Change points need a week of history before the period. The import loads 14 lookback days for
  the similar-day method anyway. A brand-new market location gets spike findings only for its
  first week.
- A public holiday on a weekday is compared with a normal workday and can raise a change point.
  This is the same limitation as similar-day filling, and a holiday calendar would fix both.
- The thresholds are tuned on synthetic data. Real fleets may need per-profile settings. They are
  public constants on `AnomalyDetector`, so a later change shows up in one place.
