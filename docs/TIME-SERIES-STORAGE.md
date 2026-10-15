# Time-Series Storage Architecture for BenchDouble

**Decision:** A first-class **optional time-series interface** with durable offline-first ingestion, independent of the instrument API, virtual physics graph, and procedure/audit event store. The initial backend-neutral C# interfaces and in-memory test adapter are implemented; actual SQLite, TimescaleDB and InfluxDB adapters are not.

## 1. Why
- Long unattended tests can generate many timestamped readings: chamber setpoint, DUT temperature, supply current, supply voltage, status and environmental telemetry.
- Need historical overlays, trending, run comparison, limit analysis, drift, settling-time calculations and interactive downsampling.
- Remote hosts may lose central connectivity. Tests must continue and record locally; upload later.
- Simulation produces **virtual elapsed time** in addition to real acquisition UTC. Never conflate these clocks.

## 2. Separation of concerns

```text
Instrument or mock -> typed UnitsNet Measurement<T> -> ingestion service
                                                       | 
                                                local durable spool (SQLite)
                                                       |
                                                optional async replicator
                                                       |
                                               TimescaleDB / InfluxDB / QuestDB
                                                       |
                                        bounded query API -> plotting adapters
```

**Three independent stores:**
1. **Procedure/control/audit store:** immutable step execution, operator actions, redlines, approvals, signatures, alarms, and lease state. Requires transactional integrity; not replaceable with time-series aggregation.
2. **Measurement time-series store:** numeric samples, timestamps, physical dimensions, quality, provenance, acquisition basis. Optimized for append and time-window queries.
3. **Bulk waveform/object store:** large binary traces, scope captures, images and calibration artifacts. The time-series store holds links, hashes and metadata; do not explode gigabyte waveforms into millions of per-sample SQL rows by default.

## 3. Core contracts now committed

`BenchDouble.TimeSeries.ITimeSeriesWriter`: `AppendAsync(IReadOnlyList<TimeSeriesSample>, CancellationToken)`.

`BenchDouble.TimeSeries.ITimeSeriesReader`: `QueryAsync(TimeSeriesQuery, CancellationToken)` returning `IAsyncEnumerable<TimeSeriesSample>`.

`TimeSeriesSample` includes `RunId`, `InstrumentId`, `SignalId`, `AcquiredUtc`, canonical numeric value, quantity kind, unit code, sequence, optional simulation elapsed, quality and measurement basis.

`TimeSeriesQuery` includes run, signal, UTC range and maximum point count.

`InMemoryTimeSeriesStore` supports basic append, replay idempotency, conflicting-duplicate rejection and bounded queries for tests/demos. **It is not durable and not a production ingestion backend.**

### Type safety and unit handling

The wire/storage DTO uses canonical numeric units and stable quantity/unit identifiers rather than serializing UnitsNet CLR objects. **At the API boundary, provide typed adapters** from `Measurement<TQuantity>` / `IQuantity` to canonical storage representation, using a validated registry of quantity kinds and canonical units. The initial canonical DTO is now validated against an allow-listed UnitsNet quantity/unit registry by the SQLite adapter; expand the registry and add authenticated network ingestion validation before accepting remote writes. Reject attempts to insert resistance into an electric-current series, even if the numeric value is valid. Store measurement basis (DC, RMS, peak, Vpp) separately.

Prefer one schema per signal or a signal metadata registry: a signal cannot silently switch from A to V midway through a run. Persist display preferences independently.

## 4. Clock and ordering semantics

- `AcquiredUtc` must be offset-zero UTC, for physical provenance and correlation.
- `SimulationElapsed` is run-scoped logical elapsed time, optional for physical samples; it is the **primary axis for accelerated mock replay**.
- Host clock offset/uncertainty and source instrument timestamps should be stored in metadata when available. UTC does not guarantee perfect cross-host synchronization.
- A monotonic per-stream sequence distinguishes samples with identical timestamps and supports replay/deduplication.
- Sample identity: `(runId, instrumentId, signalId, sequence)`. Duplicate identical writes are idempotent; conflicting duplicates are rejected and surfaced, not silently overwritten.
- Sample acquisition time is not equal to ingestion time. Production records should capture both and explicitly track late/out-of-order arrivals.
- Query ordering: acquired UTC then sequence for physical-time plots; simulated elapsed then sequence for virtual-time plots (requires a separate query mode).
- For large runs, downsample **only at query/presentation**, preserve raw evidence, and use min/max envelope rather than naive averaging when limits/excursions matter.

## 5. Storage choices

| Backend | Purpose | Why |
|---|---|---|
| SQLite | Mandatory initial **local durable spool** | Portable, embedded, transactional, WAL, crash recovery, no network |
| PostgreSQL + TimescaleDB | Preferred first **central optional adapter** | SQL, familiar operations, time-series indexing, aggregate queries |
| InfluxDB | Optional backend | High-rate time-series ingestion, retention, dashboards |
| QuestDB | Optional evaluation | High-throughput telemetry, SQL-oriented analytics |
| Object storage | Bulk binary captures | Efficient waveform/file retention and hashing |

The time-series interface must not leak vendor-specific SQL/Flux/Influx line protocol into experiment code. Check server edition/licensing, schema and supported client APIs at implementation time.

## 6. Batching and fault tolerance

- One bounded ingestion queue per host/run; async batching by count and age, with backpressure.
- **Write locally first** and acknowledge durability only after local commit. A remote TimescaleDB failure cannot cause the runner to lose accepted samples.
- Replicate via checkpointed batches with idempotent sample IDs and durable delivery cursor. Never infer "uploaded" from queued.
- Retention quotas and disk-full policy must be explicit. On exhausted storage, raise a critical recording failure and follow the procedure's configured stop/safe-state policy rather than silently drop evidence.
- Never block safety-critical instrument operations on remote DB latency.
- Auth, encryption, secret management and per-run access control are independent of the DB adapter.

## 7. Plotting and monitoring

The plotting adapter receives typed or validated sample series, converts UnitsNet values to selected display units and passes finite numeric arrays to ScottPlot/Plotly.js. Graphs can show chamber setpoint, filtered DUT temperature, supply current, and event annotations from the **audit/event store**. Multi-axis overlays must label dimensions and RMS/peak/Vpp basis.

Time-series storage is for observations; the virtual wiring graph remains the source of simulated physical state. An event stream (Channels/Dataflow) delivers live readings; the DB is the durable query backend, not a substitute for deterministic simulation scheduling.

## 8. Implementation plan

1. [x] Define backend-neutral write/query contracts, canonical sample DTO and test-only in-memory adapter.
2. [x] Add basic idempotent replay, bounded-query and invalid-number tests (committed, not executed).
3. [ ] Implement a quantity registry, typed UnitsNet conversion and dimension enforcement.
4. [ ] Implement SQLite WAL adapter with migrations, local durable outbox, sequence constraints and restart/recovery tests.
5. [ ] Implement ingestion service with bounded batching, backpressure, cancellation and retention/critical disk-full policy.
6. [ ] Implement TimescaleDB/PostgreSQL adapter and offline-to-central replay with delivery cursors and checksums.
7. [ ] Implement query-time aggregations, virtual-time filtering, min/max envelope and time-aligned joins.
8. [ ] Integrate thermal cycle, unattended monitor, runner results, remote host logs and Plotly/ScottPlot.
9. [ ] Benchmark ingest throughput, query latency, 24h offline recording, database outage and large waveform handling.
10. [ ] Add optional InfluxDB and QuestDB adapters only if demand/performance justifies them.

## 9. Acceptance examples

- Seven-hour thermal simulation records all raw temperature/current samples at 1× and accelerated speed, with identical logical-time results.
- No central DB: local run completes with durable readings, then syncs without duplicate rows.
- Replay identical batches does not duplicate measurements; conflicting sample identities raise errors.
- Reject a voltage quantity sent to a resistance signal; preserve current basis and quality.
- A remote DB outage or slow query does not block instrument control or alarms.
- Query temperature and current by run, align on virtual time, overlay in separate labeled axes and annotate alarm events.
- Crash/restart recovers committed local readings and resumes replication.
- Downsampled plots still display peaks that exceed acceptance limits.

**Status:** architectural design and lightweight interfaces committed. No production database adapter, durable spool, real replication, quantity registry or database-backed plot exists yet.

## 10. Implementation increment: durable SQLite (2026-10-15)

**Implemented in source (not yet build-verified):**
- `src/BenchDouble/TimeSeries/QuantitySeries.cs`: UnitsNet-to-canonical storage conversion and allow-listed quantity kinds and units (K, V, A, Ohm, Hz, s). A voltage cannot be inserted into a resistance/current series by changing the unit string.
- `src/BenchDouble.TimeSeries.Sqlite`: optional `Microsoft.Data.Sqlite` adapter with WAL, synchronous FULL, durable local database file, version-1 schema, indexed acquired-time queries, atomic batch commits, signal dimension/basis locking, and duplicate replay verification.
- `tests/BenchDouble.TimeSeries.Sqlite.Tests`: isolated-file tests for close/reopen durability, idempotent replay, atomic rollback on conflicting duplicates, incompatible quantity rejection, noncanonical unit rejection, and bounded queries.
- `examples/BenchDouble.ThermalCycleDemo`: records four signals at five-minute intervals across the seven-hour virtual thermal cycle and queries stored sensor samples. Uses deterministic simulated timestamps for demo reproducibility; **production physical timestamps must come from actual acquisition**.
- The storage package is optional: the core instrument library has no SQLite dependency.

### Developer example

```csharp
await using var store = await SqliteTimeSeriesStore.OpenAsync("runs/measurements.sqlite");
var point = QuantitySeries.FromQuantity(
    runId: "RUN-42",
    instrumentId: "DMM-1",
    signalId: "supply.current",
    acquiredUtc: DateTimeOffset.UtcNow,
    quantity: ElectricCurrent.FromMilliamperes(550),
    sequence: 0,
    simulationElapsed: TimeSpan.FromHours(1));
await store.AppendAsync(new[] { point });

await foreach (var sample in store.QueryAsync(
    new TimeSeriesQuery("RUN-42", "supply.current",
        DateTimeOffset.UtcNow.AddHours(-1),
        DateTimeOffset.UtcNow.AddHours(1))))
{
    Console.WriteLine($"{sample.CanonicalValue} {sample.UnitCode}");
}
```

### Current limitations / production gates

- This is a **local recorder**, not yet a complete offline-to-central replication system. No TimescaleDB/InfluxDB/QuestDB adapter has been implemented.
- It opens a SQLite connection per operation and serializes writes per store instance; ingestion batching/backpressure, sustained high-rate throughput benchmarks and multi-process stress tests remain pending.
- `TimeSeriesQuery` currently filters by acquisition UTC, not virtual time; add a virtual-time query mode before production use for accelerated simulation.
- No signal metadata migrations beyond schema version 1, retention policies, waveform object store, or replication cursor/outbox.
- Local SQLite durability does not replace the immutable procedure/audit/signature database.
- No performance or physical safety guarantees until CI and failure-injection validation are complete.

### Virtual-time query addition

`ISimulationTimeSeriesReader.QuerySimulationAsync(SimulationTimeSeriesQuery)` now queries stored samples by run-scoped simulated elapsed time, independently of UTC acquisition pacing. SQLite indexes `(run_id, signal_id, simulated_ticks, sequence)`. A test verifies two samples acquired at the same UTC time but different simulated times can be filtered correctly. The query currently requires a bounded maximum-point count and does not yet provide min/max downsampling or time-aligned joins.
