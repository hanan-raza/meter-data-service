# ADR 0002 — Aggregation in SQL with German-time buckets

- Status: Accepted
- Date: 2026-10-09

## Context

Billing and the portal need hourly, daily and monthly energy totals per market location. A year
of one market location is ~35,000 values (ADR 0001). The totals must be exact, and the buckets
must be German calendar units: a day lasts 23, 24 or 25 hours, a month starts at local midnight
on the 1st, which is 22:00 or 23:00 UTC the day before.

## Decision

1. **Sum in PostgreSQL, not in .NET.** `AggregationRepository` runs one grouped query and returns
   only the totals. Loading every value to sum it in memory would move ~35,000 rows per year and
   location for a few hundred results.
2. **Hand-written SQL via `Database.SqlQuery<T>`.** The bucket expression needs PostgreSQL's
   `date_trunc(unit, timestamptz, zone)`, which EF Core's LINQ translation doesn't offer in this
   form. The query is parameterized (interpolated `FormattableString`), and the column names follow
   the snake_case convention of ADR 0001.
3. **Exact arithmetic end to end:** `sum(numeric)` in SQL, `decimal` in C#. There is no cast to
   `double` anywhere, and an integration test checks a sum that floating point gets wrong.
4. **Days and months are truncated in `Europe/Berlin`.** Local midnight is never skipped or
   repeated in Germany, so the result is unambiguous.
5. **Hours are truncated in UTC.** German offsets are whole hours, so UTC hours are the local
   hours. Truncating in local time would merge the two passes of the repeated 02:00 hour on the
   fall-back day: both truncate to the same ambiguous local 02:00. In UTC they stay two buckets.
6. **Bucket start and end are returned with the German offset of that instant**, e.g. the fall-back
   day runs from `2026-10-25T00:00+02:00` to `2026-10-26T00:00+01:00`. The interval count and the
   number of `Measured` values come with each total, so a consumer can see incomplete or
   substitute-heavy buckets.
7. **Empty buckets are omitted.** "No data" is not the same as "zero consumption".

## Consequences

- The aggregation is tied to PostgreSQL. That's acceptable: PostgreSQL is this service's only
  store, and the query is a single file with integration tests against `postgres:17`.
- The query filters on `(market location, OBIS code, interval_start)`, so it benefits from the
  unique index `(measurement_series_id, interval_start)`. A materialized daily table can be added
  later if monthly reports over many locations get slow.
