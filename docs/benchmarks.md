# Import speed benchmarks

How long does it take to import one week of 15-minute values for one market location? The benchmarks in
[`benchmarks/MeterDataService.Benchmarks`](../benchmarks/MeterDataService.Benchmarks) measure it with
[BenchmarkDotNet](https://benchmarkdotnet.org/).

## Running

```bash
dotnet run -c Release --project benchmarks/MeterDataService.Benchmarks -- --filter '*'
```

A full run takes about five minutes. Results go to `BenchmarkDotNet.Artifacts/` (git-ignored). Use
`--filter '*Pipeline*'` for a subset or `--job short` for a quick, less precise run.

## Scenario

`ImportScenario` builds one deterministic H0 household week, Monday 7 to Sunday 13 September 2026: 7 × 96 = 672
quarter hours, no DST switch. The file has the defects of a real delivery, so every step does real work:

| Defect | Where | Expected handling |
|--------|-------|-------------------|
| 2 missing intervals | Monday 07:30 | linear interpolation |
| 4 missing intervals | Tuesday 10:00 | linear interpolation |
| 12 missing intervals | Wednesday 12:00–15:00 | similar-day filling (last Wednesday) |
| 1 spike (6× the real value) | Saturday 19:00 | rejected by validation, then interpolated |

That makes 654 data rows in the CSV (offset timestamps such as `2026-09-07T07:30:00+02:00`). The in-memory
repository hands out a fresh series holding the 14 lookback days (1,344 measured values) for each import, as a
new unit of work would load them from PostgreSQL. Before measuring, `GlobalSetup` imports the scenario once and
checks the counts (654 rows, 1 rejected, 18 missing, 19 substituted). If the pipeline silently did nothing, the run
fails instead of reporting impressive numbers.

| Benchmark | What it measures |
|-----------|------------------|
| 1 CSV parse | `CsvImportParser.Parse` of the 654-row file |
| 2 Validate | `ValidationEngine.Validate` of the parsed readings |
| 3 Record + gap fill | Fresh series with history, record the 653 accepted values, `GapFillingService.Fill` |
| **Pipeline** | CSV parse → validate → repository load → record → gap fill → save (the import as the roadmap defines it) |
| End to end | The real `ImportBackgroundService` with a one-job channel: the pipeline plus ML.NET anomaly detection, the quality score, job tracking and metrics |

Persistence is in memory on purpose: the benchmark measures the service's own code, not PostgreSQL round trips.

## Results

Environment: BenchmarkDotNet v0.15.8, Windows 11 (25H2), Intel Core i5-8365U 1.60 GHz (4 cores, 8 threads, laptop),
.NET SDK 10.0.401, runtime .NET 10.0.12, X64 RyuJIT x86-64-v3, default job.

### Baseline (before the fix)

| Method | Mean | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|--------|-----:|------:|-------:|-----:|-----:|-----:|----------:|
| 1 CSV parse | 991.9 µs | 19.83 µs | 34.73 µs | 300.8 | 3.9 | – | 925.0 KB |
| 2 Validate | 185.1 µs | 3.70 µs | 4.94 µs | 78.9 | 0.5 | – | 242.8 KB |
| 3 Record + gap fill | 2,272.9 µs | 28.99 µs | 50.01 µs | 195.3 | 191.4 | 97.7 | 1,045.6 KB |
| Pipeline | 6,236.3 µs | 123.50 µs | 177.12 µs | 437.5 | 265.6 | 93.8 | 2,213.8 KB |
| End to end (incl. ML.NET) | 15,445.9 µs | 294.27 µs | 705.06 µs | 555.6 | 222.2 | 111.1 | 2,935.0 KB |

### After the fix

| Method | Mean | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|--------|-----:|------:|-------:|-----:|-----:|-----:|----------:|
| 1 CSV parse | 983.1 µs | 19.50 µs | 26.03 µs | 300.8 | 3.9 | – | 925.0 KB |
| 2 Validate | 188.5 µs | 2.93 µs | 4.73 µs | 78.9 | 0.5 | – | 242.8 KB |
| 3 Record + gap fill | **614.3 µs** | 12.08 µs | 22.99 µs | 143.6 | 142.6 | 142.6 | 1,253.3 KB |
| Pipeline | **2,598.6 µs** | 150.09 µs | 435.45 µs | 351.6 | 203.1 | 140.6 | 2,421.4 KB |
| End to end (incl. ML.NET) | **11,197.3 µs** | 222.22 µs | 345.97 µs | 468.8 | 250.0 | 156.3 | 3,140.5 KB |

Gen0/1/2 are collections per 1,000 operations.

## What the benchmark found

**`MeasurementSeries.Record` and `Substitute` were quadratic.** Both looked up the existing value of an interval
with `List<T>.Find`, a linear scan. An import calls `Record` once per accepted reading, and the series already
holds the 14 lookback days. So recording 653 values scanned roughly 653 × 1,700 ≈ 1.1 million entries. Gap
filling then searched the whole list again for each substitute.

The fix is a `Dictionary<DateTimeOffset, MeasurementValue>` index next to the list. It isn't mapped by EF Core
and catches up lazily: EF Core materializes values straight into the backing list, so before each lookup the
index adds whatever was appended since the last one. `TryAdd` keeps the first of duplicate intervals, which is
exactly what `List.Find` returned before. Tests in `MeasurementSeriesTests` cover values added after a lookup,
duplicates, values loaded straight into the backing list, and a shrunken list.

| | Before | After | Speed-up |
|---|---:|---:|---:|
| Record + gap fill | 2.27 ms | 0.61 ms | 3.7× |
| Pipeline | 6.24 ms | 2.60 ms | 2.4× |
| End to end | 15.4 ms | 11.2 ms | 1.4× |

The cost grows with the size of the import, so the gain is larger for a month or a year in one file (a year
would have meant some 35,000 lookups over an average of ~19,000 entries each, over 600 million comparisons). The index allocates about 200 KB more per import. Its arrays
for about 2,000 entries are big enough to land on the large object heap, which explains the extra Gen2
collections. That's a fair price for removing the O(n²).

## What's left

- **ML.NET dominates the end-to-end time.** About 8.6 of the 11.2 ms are spent beyond the pipeline, almost all
  of it in anomaly detection (the quality score is a few counts, job tracking and metrics are dictionary
  updates). Two `MLContext` pipelines are built and fitted per
  import. That's acceptable for one job every few seconds. For high-volume bulk loads, anomaly detection could
  move to its own queue after the import commits.
- **CSV parsing is the largest non-ML step** (~1 ms, ~1.4 KB per row). It comes from CsvHelper's by-name field
  access and `TryParseExact` over several formats per timestamp. Looking up the field indexes once and trying the
  most common format first would help, but at ~1.5 µs per row it isn't worth the complexity yet.
- The Pipeline's standard deviation went up after the fix (435 µs) and its mean is above the sum of steps 1–3.
  It allocates ~2.4 MB per operation and triggers Gen2 collections, so timings depend on when a full collection
  hits. Pre-sizing the index or pooling the parser's buffers would be the next experiment.

Roughly 2.6 ms per week of data means one importer thread handles about 380 market-location weeks per second
before ML.NET, or about 90 per second end to end. The database will be the limit long before the pipeline is.
