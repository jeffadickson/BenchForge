# Run-scoped simulation clock modes

TR-045 now has a clock-mode foundation in the main simulation library and
experiment composition API. Each adjustable clock starts at zero in manual
mode. `Start` follows monotonic elapsed time; speed 1 is real time, greater than
1 is accelerated, and less than 1 is slowed. `Pause` holds elapsed time.
`UseManual` selects explicit advancement, and `AdvanceBy` advances a stopped clock.

```csharp
var definition = new ExperimentDefinitionBuilder()
    .AdjustableClock(speed: 60)
    .Build("clock example");
await using var run = await definition.StartSimulatedAsync();
var clock = (AdjustableSimulationClock)run.SimulationTime;
clock.Start();
// Injected ISimulationClock consumers see 60 logical seconds per monotonic second.
clock.Pause();
clock.AdvanceBy(TimeSpan.FromMinutes(1));
var snapshot = clock.Status();
```

Speed changes preserve previously accumulated logical time. Decimal fractional
ticks survive pause/resume and rate changes; public elapsed time has TimeSpan's
100 ns resolution. Negative advancement, invalid rates, backwards provider time
and unrepresentable elapsed time fail explicitly. Controls and coherent status
snapshots share one lock. The clock creates no timers or background workers.

`AdjustableClock` creates a fresh clock per run. `UseSimulationClock` supports a
composition-root factory for other clock implementations. Selecting a clock
removes the default manual registration, and ordinary model/behavior registration
cannot replace the reserved clock services. The canonical interface remains
`ISimulationClock`, exposed as `run.SimulationTime`. Eager startup resolves this
clock before instrument factories and awaits normal failed-start cleanup.

The existing `run.Clock` property remains compatible with the default manual
clock. It reports an explicit error for a nonmanual selection. Existing step
runner pacing uses manual advancement; use the default clock for that runner.
Automatic runner pacing and UI controls for the adjustable clock remain further
integration work under TR-045. This increment does not claim complete acceptance.

Clock selection applies to simulation models. Host heartbeat expiry, lease
fencing, physical safety delays and physical I/O deadlines retain their existing
real-time mechanisms. A speed change has no effect on those mechanisms.

Clock tests use injected timestamp providers and require no real sleeps. Runtime
verification passed at checkpoint 250; no requirement status is promoted.
