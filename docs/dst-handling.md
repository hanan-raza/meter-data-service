# DST handling (Sommerzeit / Winterzeit)

German market time is legal time in `Europe/Berlin`: CET (`+01:00`) in winter, CEST (`+02:00`) in
summer. A market day runs from local midnight to local midnight, so twice a year it is not 24 hours
long. This document describes how the service handles that, step by step, and where the tests are.

| Day | Date (2026) | Clock change | Length | 15-min intervals |
|-----|-------------|--------------|--------|------------------|
| Normal day | — | — | 24 h | 96 |
| Spring forward (Umstellung auf Sommerzeit) | last Sunday of March, 29 March | 02:00 CET → 03:00 CEST | 23 h | 92 |
| Fall back (Umstellung auf Winterzeit) | last Sunday of October, 25 October | 03:00 CEST → 02:00 CET | 25 h | 100 |

On the spring-forward day the local hour 02:00–02:59 doesn't exist. On the fall-back day it
happens twice, first at `+02:00`, then at `+01:00`. A wall-clock time like `02:15` therefore
names either no interval or two of them.

## The core rule: instants in UTC, days in German time

1. **Every interval is identified by its UTC start instant.** `MeasurementValue.IntervalStart` is a
   `DateTimeOffset` normalized to UTC and stored as `timestamptz` (ADR 0001). The two passes of the
   repeated hour are different instants (`00:00Z` and `01:00Z` for local 02:00), so they can never
   collide. Nothing in the domain model uses a local time as a key.
2. **Every period is a range of German calendar days.** `GermanCalendar.StartOfDayUtc(day)` gives
   the UTC instant of local midnight. A day is `[StartOfDayUtc(day), StartOfDayUtc(day + 1))`, and
   `GermanCalendar.IntervalsIn(day)` returns 92, 96 or 100. Local midnight is never skipped or
   repeated in Germany, so this is always unambiguous.
3. **Walking a day means stepping 15 minutes in UTC** from one local midnight to the next. Code never
   counts "96 per day" or computes `hour * 4 + quarter`, which would be wrong twice a year.

Everything below follows from these three rules.

## Step by step through the pipeline

### CSV import (`CsvImportParser`)

- **Timestamps with an offset** (`2026-10-25T02:00:00+01:00`) are unambiguous and are converted to UTC.
  Row order doesn't matter.
- **Timestamps without an offset** are read as German local time, because many exports deliver them
  that way:
  - A local time in the **skipped hour** (spring forward, e.g. `2026-03-29 02:15`) becomes a parse
    error on that line ("does not exist"). The rest of the file is still imported. Guessing an
    offset would put the value on an instant that already belongs to another row.
  - A local time in the **repeated hour** (fall back) is resolved by order of appearance: the first
    `02:15` is summer time, the second is winter time. That is how a chronological export lists
    them. A third occurrence maps to winter time again and is caught as a duplicate (see below).

### Validation (`ValidationEngine`)

- **Duplicates** are judged on the UTC instant. The two passes of 02:00 are two intervals, not a
  duplicate. The same instant delivered twice is rejected (`DuplicateInterval`); the first value is kept.
- **Missing intervals** are found by walking the period in UTC, so the skipped spring hour is never
  reported as missing, and both passes of the repeated autumn hour are expected.
- **DST interval count**: on a 23- or 25-hour day the engine compares the number of delivered
  intervals with 92 / 100 and reports a `DstIntervalCount` warning if they differ. With exactly 96
  it adds the hint "the sender probably ignored the clock change", the most common real-world error.
- **Spike check** looks at the neighbours ±30 minutes in UTC, so across the spring jump 01:45 and
  03:00 are neighbours, as they are in reality.

### Gap detection and filling (`GapDetector`, `LinearInterpolationFiller`, `SimilarDayFiller`)

- Gaps are found by stepping through the day in UTC, like validation. Four missing intervals around
  the spring jump (e.g. local 01:30, 01:45, 03:00, 03:15) are **one gap of one real hour**, not of
  two wall-clock hours.
- **Linear interpolation** works on the UTC sequence, so the line between two anchors has as many
  steps as real intervals lie between them.
- **Similar-day filling** copies the same *local* wall-clock time from the source day, because load
  follows the clock (people wake up at 07:00 local, not UTC):
  - If the source day is the spring-forward day and the wall-clock time doesn't exist there, that
    candidate is skipped for this interval.
  - If the source day is the fall-back day, the pass with the same offset as the gap is preferred
    (the winter-time pass for a gap in winter).
  - If the gap is on the fall-back day and the source is a normal day, both passes of 02:00 are
    filled from the source's single 02:00.

### Anomaly detection (`AnomalyDetector`)

The change-point detector compares each value with the **same local time one week earlier**, not
with UTC minus 168 hours. Otherwise, in the week after a clock change, 18:00 would be compared with
17:00 and the evening ramp would look like a change point. A skipped spring hour has no reference
and is left out; for the repeated autumn hour the winter-time pass is the reference.

### Data-quality score and aggregation

- The quality score expects `IntervalsIn(day)` intervals, so a complete 23-hour day scores 100 and
  a 25-hour day isn't complete with 96 values.
- Aggregation (ADR 0002) cuts days and months in `Europe/Berlin` and hours in UTC. The fall-back day
  has 25 hourly buckets, with both 02:00 passes kept apart; a local-time `date_trunc('hour')` would
  merge them.

### API output

Timestamps leave the API with the German offset in effect at that instant
(`GermanCalendar.ToGermanTime`), e.g. the fall-back day runs from `2026-10-25T00:00:00+02:00` to
`2026-10-26T00:00:00+01:00`. A client can tell the two 02:00 passes apart by their offset and doesn't
need a time-zone database to do so.

### Synthetic data

The generator walks German days in UTC like everything else, so a requested range has 92 / 100
values on DST days. Household load follows local time. PV feed-in follows the sun, i.e. UTC, so
production doesn't move by an hour at the clock change.

## What a sender can get wrong, and what happens

| Delivery | Result |
|----------|--------|
| 92 rows on the spring-forward day (correct) | Imported completely, no findings, quality score 100 |
| 96 local-time rows on the spring-forward day, including 02:00–02:45 | 4 parse errors for the nonexistent times, the other 92 rows are imported |
| Four values missing at the spring jump | One 4-interval gap (one real hour), interpolated if both anchors exist |
| 100 local-time rows on the fall-back day, 02:00–02:45 twice (correct) | 100 distinct intervals, no duplicates, no findings |
| 100 rows with explicit offsets, in any order | 100 distinct intervals |
| 96 local-time rows on the fall-back day, each 02:xx once | Read as the summer-time pass; the winter-time pass is 4 missing intervals, a `DstIntervalCount` warning ("probably ignored the clock change") is raised, and the 4 intervals are interpolated |
| An interval delivered twice with the same offset, or a local 02:xx three times | `DuplicateInterval`, the first value is kept |

## Tests

Dedicated DST tests run the real pipeline (parser, validation, gap filling, ML.NET anomaly
detection, quality score) through `ImportBackgroundService` with only persistence faked:

- `tests/MeterDataService.Tests.Unit/Dst/SpringForwardDayTests.cs`: 92 intervals; the generator emits
  nothing in the skipped hour; a complete day has no gaps or findings; four missing intervals before,
  across and after the jump are one gap of exactly one hour; interpolation across the jump; nonexistent
  local times are parse errors; a 23-hour day and a 96-row file both import without crashing.
- `tests/MeterDataService.Tests.Unit/Dst/FallBackDayTests.cs`: 100 intervals; the generator emits the
  repeated hour at both offsets; repeated local times are two intervals, not duplicates; explicit
  offsets in any order; real duplicates (same offset twice, a third local 02:00) are rejected; a gap
  in the second pass is found at its winter-time instants; a 25-hour day imports completely, and a
  96-row file gets the winter-time pass interpolated.

Further DST cases live next to the component they test: `GermanCalendarTests`, `CsvImportParserTests`,
`ValidationEngineTests`, `GapDetectorTests`, `SimilarDayFillerTests`, `AnomalyDetectorTests`,
`DataQualityScorerTests`, and the PostgreSQL integration tests for aggregation and the read API.
All of them use the 2026 transition days (29 March, 25 October).
