# ADR 0005 — Import via a bounded `Channel<T>` and one `BackgroundService`

- Status: Accepted
- Date: 2026-10-16 (decided on 2026-10-03 with the import endpoint, written down afterwards)

## Context

Metering point operators (MSB) deliver interval values as files. One file can hold a day or a whole
year (~35,000 rows) for one market location. An import parses, validates, loads 14 lookback days,
fills gaps, runs ML.NET anomaly detection and saves everything in one transaction. That takes ~11 ms for a
week in the benchmark ([benchmarks.md](../benchmarks.md)), but much longer for a year file on a busy
database. Two requirements shape the design:

- An upload must not hold the HTTP request open for the whole import. Senders time out and resend,
  and the duplicate then competes with the original.
- Two files for the same market location must not be processed at the same time. Both would write
  the same intervals. The unique index `(measurement_series_id, interval_start)` would reject one
  transaction, or worse, the older delivery would commit last and win.

Options considered:

| Option | Why not (for now) |
|--------|-------------------|
| Process inside the request | Long requests, timeouts, and concurrent imports per market location |
| `Task.Run` per upload | Unbounded concurrency and memory, no ordering, exceptions are lost |
| Hangfire / Quartz with a job table | Durable, but a second schema and a polling scheduler for a single service |
| Message broker (RabbitMQ, Azure Service Bus) | Durable and scalable, but another piece of infrastructure to run before it is needed |
| **`Channel<T>` + `BackgroundService`** | In-process, no dependency, back-pressure built in |

## Decision

1. **`ImportChannel` wraps a bounded `Channel<ImportJob>` (capacity 100, single reader).** The
   endpoint checks the request (file present, MaLo-ID with check digit, max 10 MB), reads the file,
   registers the job in `ImportJobTracker` and calls `TryEnqueue`. It answers `202 Accepted` with a
   `Location` to poll, and it never parses on the request thread.
2. **Back-pressure instead of buffering.** If the channel is full, `TryWrite` fails and the endpoint
   answers `503` with `Retry-After: 30`. Memory use stays bounded, and the sender gets a clear signal.
   The endpoint does not wait for space, so a request never blocks on the queue.
3. **One reader, jobs in arrival order.** `ImportBackgroundService` processes one job after the
   other. Imports never race on intervals, and a later delivery always wins (Korrekturlieferung).
4. **A DI scope per job.** The service is a singleton. Each job gets a fresh `DbContext` behind
   `IMeasurementSeriesRepository` and is saved in one transaction, so a job is stored completely or
   not at all.
5. **A failing job doesn't stop the loop.** Exceptions are logged with the job id and the job is
   marked `Failed` with a generic message. The next job runs normally.
6. **Status in memory.** `ImportJobTracker` keeps the state and counts per job id for polling.
   OpenTelemetry metrics (`meterdata.import.*`) report values, job durations and queue depth.

The channel is injected through `ImportChannel`, so tests pass an unbounded, completed channel.
`ImportBackgroundService` then runs to the end of the queue deterministically, without timers.

## Consequences

- **Not durable.** Queued jobs and job statuses are lost on restart or crash. Values already saved
  stay. This is acceptable as long as senders resend on a missing acknowledgement, which is how
  market communication works (APERAK / CONTRL). Polling a job lost with a restart returns `404`.
- **One instance.** The order guarantee holds within one process only. Running several instances
  needs a durable queue with per-market-location ordering: a broker partitioned by MaLo-ID, or a
  job table with row locks. `ImportChannel` is the only place the API and the worker share, so the
  swap stays local.
- **Sequential throughput.** One reader is far from the limit at the measured job times. If it
  becomes one, jobs can be sharded by MaLo-ID across N readers. That keeps the per-location order.
  ML.NET is ~75 % of the import time and is the first candidate for its own queue.
- **The capacity counts jobs, not bytes.** 100 queued files of 10 MB are about 2 GB as .NET strings
  in the worst case. Typical deliveries (a day up to a year) are 4 KB to 1.5 MB. If large files
  become common, the queue should hold a reference to a stored file instead of its content.
