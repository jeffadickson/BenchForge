# BenchDouble Experiment Runner

## Why it belongs in the instrument-control library
external instrument source separates parameterized `Procedure`, lifecycle execution by `Worker`, structured `Results`, and GUI queue management by `Manager`. This is a useful reference for a hardware-independent .NET execution core. BenchDouble experiments must run with real IVI/VISA, simulated or strictly mocked instruments. The instrument-control and runner components are useful standalone; TestForge may additionally handle qualification, requirements, operators and signoff.

References:
- https://external instrument source.readthedocs.io/en/latest/tutorial/procedure.html
- https://external instrument source.readthedocs.io/en/latest/api/experiment/procedure.html
- https://external instrument source.readthedocs.io/en/latest/api/display/manager.html

## Implemented prototype
- `IExperimentProcedure` (`StartupAsync`, `ExecuteAsync`, `ShutdownAsync`)
- `ExperimentProcedure` convenience base class
- `ExperimentRunner` async lifecycle execution serialized per runner via `SemaphoreSlim`
- immutable `ExperimentRun` outcome with status, times, errors
- `ExperimentContext` with run identity, `ILogger` and result sink
- `IExperimentResultSink`, `InMemoryExperimentResultSink`
- measurement rows with numeric values and units
- scoped structured log events
- shutdown in `finally`; cancellation-independent cleanup; explicit unknown-state diagnostic if cleanup throws

## Failure semantics
If startup or execution fails, shutdown still runs. Failure returns an `ExperimentRun` containing the exception, rather than throwing it. If both execution and shutdown fail, errors are aggregated. If cancellation occurs **during** execution, it returns `Cancelled` after shutdown; cancellation before gate acquisition propagates. Shutdown receives `CancellationToken.None` so ordinary user cancellation cannot suppress cleanup; this needs a bounded shutdown policy before physical hardware use.

## Not implemented yet
- Parameter descriptor validation and run snapshots
- Experiment queue, reservation locks across instruments, scheduling
- Durable CSV/SQLite/Parquet result storage and resumable export
- Progress, streaming events and browser dashboard
- Controlled abort/stop/pause/resume with checkpoint semantics
- Async VISA/IVI adapters, injected run context, resource lifecycle
- Bounded safe-state shutdown, emergency stop and hardware validation
- OpenTelemetry ActivitySource and metrics
- Retry policies respecting operation idempotency
- Durable audit trails, operator credentials or TestForge integration

## Required production behaviors
1. Declare and validate input parameters before instrument activation.
2. Reserve instruments for the run and identify backend provenance.
3. Persist run configuration and versions, instrument IDs/calibration and timestamped result metadata.
4. Ensure cleanup even on fault, cancellation or client disconnect; report unknown physical states.
5. Run without physical hardware for CI using simulation and mocks.
6. Avoid concurrency on a single physical session and prevent cross-run instrument conflicts.
7. Capture failures as structured diagnostics linked to run ID.
8. Build an experiment queue and web UI as a separate hosting/presentation component.

**Status:** source/tests committed; successful .NET compilation and CI execution not yet established. This is an experiment-runner kernel, not a production hardware scheduler.

## Bounded shutdown and recovery latch (2026-10-15)

The core `ExperimentRunner` now accepts optional `ExperimentRunnerOptions { ShutdownTimeout = TimeSpan.FromSeconds(10) }`. Cleanup is attempted after success, failure or cancellation using a token independent of the cancelled run. If shutdown faults or exceeds its real wall-clock deadline, the result is `ExperimentStatus.RecoveryRequired`, the error is preserved, and `RequiresRecovery` prevents further runs on that runner. A timed-out shutdown task may continue if the device driver ignores cancellation; **a timeout never proves physical output is disabled**. The runner deliberately has no automatic recovery reset: operator/device-specific verification and host lease fencing are still required before resuming physical control. The latch is per-runner, not a cross-process hardware interlock. xUnit tests cover shutdown fault, timeout, cancellation during execution and refusal to rerun. Full state machine, real device abort and host-level recovery are still open in issue #47.
