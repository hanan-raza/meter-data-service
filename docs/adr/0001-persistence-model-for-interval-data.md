# ADR 0001 — Persistence model for interval meter data

- Status: Accepted
- Date: 2026-10-02

## Context

The service stores 15-minute energy values for market and meter locations. Every value feeds
billing and balancing downstream, so the storage format must not lose precision, must keep DST
days unambiguous, and must be fast for range queries per series.

## Decision

1. **Energy values are `decimal` / `numeric(18,5)`.** Floating point cannot represent typical kWh
   values exactly and accumulates error when summed into monthly totals. Five decimal places cover
   the resolution of real meters (Wh with fractions); the domain rejects values with more decimals
   instead of letting PostgreSQL round them silently.
2. **Timestamps are `DateTimeOffset` normalized to UTC, stored as `timestamptz`.** Local German
   time is ambiguous for one hour each October (the 02:00–03:00 hour happens twice) and has a hole
   each March. UTC instants are unique, so `(series, interval_start)` can be a unique key. Conversion
   to `Europe/Berlin` happens at the edges (import, aggregation, API).
3. **A value is identified by its interval start.** Values must be aligned to a 15-minute boundary.
   Because German UTC offsets are whole hours, UTC alignment implies local alignment.
4. **Status is stored as text** (`Measured`, `Estimated`, `Replaced`). It is read by humans
   during disputes and by SQL reports; a readable column is worth the few extra bytes.
5. **Surrogate keys:** UUIDv7 (`Guid.CreateVersion7`) for master data, created in the domain so
   aggregates can be wired up before saving; `bigint` identity for measurement values because the
   table grows by ~35,000 rows per series and year.
6. **One active meter per meter location** is enforced in the domain and by a partial unique index
   (`WHERE removed_at IS NULL`) so concurrent imports cannot violate it.
7. **snake_case naming** via `EFCore.NamingConventions`, so hand-written SQL needs no quoting.

## Simplification

In the real market model a meter location can contribute to several market locations and vice
versa (n:m, e.g. for cascades or tenant electricity). This service models 1:n (one market location
has many meter locations), which covers household, commercial and PV feed-in cases. The n:m link
can be introduced later as a join entity without changing the measurement tables.

## Consequences

- Every query that groups by local day or month must convert from UTC explicitly; aggregation
  code and tests must cover 23-hour and 25-hour days.
- Partitioning `measurement_values` (by time range) is possible later without changing the model.
