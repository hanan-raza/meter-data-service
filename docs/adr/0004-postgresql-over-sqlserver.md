# ADR 0004 — PostgreSQL over SQL Server

- Status: Accepted
- Date: 2026-10-16 (decided on 2026-10-02 with the first migration, written down afterwards)

## Context

The service stores 15-minute energy values: ~35,000 rows per series and year (ADR 0001). The
main queries are ranges per series and totals per German day or month (ADR 0002). Both
PostgreSQL and SQL Server are common in the German energy IT market and both are well supported
by EF Core. Oracle was not considered for this service. It needs a heavier container and gives
no advantage for this workload.

The candidates are equal in what matters most for money and energy:

- exact `numeric(18,5)` / `decimal(18,5)`,
- filtered (partial) unique indexes for "one installed meter per meter location",
- a timestamp type that keeps the instant (`timestamptz` / `datetimeoffset`).

So the decision rests on the differences below.

## Decision

Use **PostgreSQL 17** with `Npgsql.EntityFrameworkCore.PostgreSQL` as the only store of this service.

1. **Time-zone-aware truncation with IANA names.** `date_trunc('day', interval_start, 'Europe/Berlin')`
   cuts a 23- or 25-hour day in one expression. It uses the IANA time zone database, the same zone
   IDs .NET uses on Linux and in `TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")`. SQL Server
   needs `AT TIME ZONE 'W. Europe Standard Time'` (Windows zone names from `sys.time_zone_info`)
   and then a separate truncation (`DATETRUNC`) of the local value. That works, but the zone name
   differs from the one the application uses. Also, the truncated bucket start loses its offset and
   has to be mapped back to an instant before the API can return it.
2. **`timestamptz` stores a UTC instant.** It matches ADR 0001 ("normalize to UTC, convert at the
   edges"). `datetimeoffset` stores the offset that was written. Two equal instants with different
   offsets are equal in comparisons, but the stored data now depends on the writer. With
   `timestamptz` there is only one representation.
3. **UUIDv7 keys stay in index order.** PostgreSQL compares `uuid` byte by byte, so the
   time-ordered UUIDv7 keys of master data (ADR 0001) are appended at the end of the index. SQL
   Server sorts `uniqueidentifier` by the last six bytes first. Sequential UUIDv7 values then
   land at random places in the index, and it fragments.
4. **Light, multi-platform container.** `postgres:17` is small and starts in a few seconds, so
   every integration test class gets its own database via Testcontainers (on GitHub Actions too).
   It runs natively on x64 and ARM. The SQL Server 2022 image is several times larger, needs at
   least 2 GB of RAM and is x64 only.
5. **Room to grow as a time-series store.** Native range partitioning of `measurement_values` by
   time, and extensions such as TimescaleDB, are options later without changing the model.

The portfolio still covers SQL Server, where it fits better: `energy-billing-engine` runs its
golden-master tests against SQL Server and Oracle.

## Consequences

- PostgreSQL-specific SQL is limited to `AggregationRepository` (`date_trunc` with a zone, ADR 0002)
  and the partial index filter `removed_at IS NULL`. Everything else is EF Core LINQ. A switch to
  SQL Server would mean rewriting that one query, regenerating the migrations and running the
  integration tests against the new engine.
- Column names are snake_case (`EFCore.NamingConventions`), the usual style in PostgreSQL. Hand-written
  SQL then needs no quoted identifiers.
- Integration tests need Docker. Without it they are skipped locally and fail in CI (see README → Testing).
