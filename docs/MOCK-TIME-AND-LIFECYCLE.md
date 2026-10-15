# Mock Instrument Lifecycle, Shared Time, and Delay Specification

**Status:** Requirements and design baseline — not yet implemented in the generator or remote instrument hosts.

## 1. Purpose

A mock instrument is a programmable behavioral model, not merely a dictionary of SCPI canned responses. It has a **state initialization phase**, a **runtime measurement/operation phase**, a common **simulation time domain**, configurable physical-world delays, and controllable speed for tests, demos, and engineering studies.

The design must interoperate with the declarative driver descriptions, UnitsNet quantities, async instrument sessions, EasyMock-style expectations, distributed instrument hosts and experiment runner.

## 2. Three distinct notions of time

- **Wall-clock time:** UTC timestamps and real elapsed monotonic duration observed by the execution host. Use a monotonic source for real duration, not DateTime.UtcNow subtraction.
- **Simulation elapsed time:** monotonic logical duration since simulation-epoch start, shared by every simulated device in a bench/scenario.
- **Instrument local time:** optional offset/drift from simulation elapsed time, used when an instrument has its own clock or warmup history.

The simulator publishes both wall and simulation timestamps, plus simulation run ID and clock identity in logged samples and events.

An environment-wide shared clock is **not** a single static global .NET variable. It is an explicit clock service scoped to a simulation run, with a stable clock ID and scheduler. Disparate clients or hosts join the same clock domain only through an authoritative coordinator and ordered commands. For a distributed test across physical hosts, the simulator must not claim deterministic global ordering from unsynchronized wall clocks.

## 3. Execution modes and speed

| Mode | Behavior | Use |
|---|---|---|
| RealTime | Simulated seconds progress at wall-clock rate (scale 1.0) | Operator demonstration |
| Accelerated | Simulated seconds progress at a configured multiple of wall time (e.g. 100×) | Fast integration testing |
| Manual / Virtual | Explicit clock advancement; no real sleeps required | Deterministic unit tests |
| Paused | No simulation-time advancement until resumed | Debugging, single-step demos |

**Convention:** `speedFactor = simulated elapsed seconds / real elapsed seconds`. Therefore 10 simulated seconds at 100× take about 0.1 real second. A factor of 0 must not silently represent an infinite wait; use an explicit paused/manual mode. Hardware-backed operations must never be sped up or replaced by virtual-clock waits. Real timers and lease expirations/security timeouts remain on monotonic wall time even in accelerated simulation.

## 4. Mock specification structure

```json
{
  "schemaVersion": 2,
  "instrumentClass": "DigitalMultimeter",
  "model": "DemoDMM",
  "simulation": {
    "clock": "bench",
    "mode": "Accelerated",
    "speedFactor": 100,
    "seed": 42017,
    "initialize": {
      "measurementMode": "DcVoltage",
      "range": { "value": 10, "unit": "V" },
      "warmup": { "duration": 30, "unit": "s" },
      "initialVoltage": { "value": 5, "unit": "V" }
    },
    "operations": {
      "MeasureDcVoltage": {
        "behavior": {
          "kind": "sineWithNoise",
          "offset": { "value": 5, "unit": "V" },
          "amplitude": { "value": 0.1, "unit": "V" },
          "frequency": { "value": 0.5, "unit": "Hz" },
          "noise": { "distribution": "uniform", "halfRange": { "value": 0.002, "unit": "V" } }
        },
        "latency": {
          "nominal": { "duration": 200, "unit": "ms" },
          "jitter": { "distribution": "uniform", "rangeMs": [-10, 10] },
          "clockDomain": "simulation"
        }
      }
    }
  }
}
```

**This is a proposed schema illustration, not a parser-supported input yet.** The existing `*.instrument.json` schema version 1 handles limited generated SCPI driver properties. Simulation configuration should be a distinct versioned sibling file or a separately validated section with its own schema version; do not silently treat version 2 as backward compatible with the current generator.

### Initialization phase
- Sets instrument identity, default configuration, channel states, errors, internal counters, random seed, calibration approximation, availability and warmup conditions.
- Runs exactly once per fresh simulation instance unless the test explicitly resets/reinitializes.
- `*RST` should follow the model's documented reset semantics; it does not necessarily re-run every initialization action or reset simulated global time.
- May take simulated time and emit logs; tests can control whether warmup begins at scenario start or first power-up.
- Initialization failure, unsupported config and invalid UnitsNet dimensions are explicit errors.

### Runtime measurement phase
- Defines behavior for each operation, including input-dependent responses, signal/noise, state transitions, acquisition triggers, value quantization, overrange and faults.
- Evaluates signal using a specified simulation timestamp (typically acquisition/sample time), not arbitrary execution-thread scheduling.
- Can reference state variables, another instrument's modeled output, shared DUT transfer function and prior samples.
- Supports constant, sequence, sine, ramp, seeded random, scriptable deterministic state machine and composed behaviors. Arbitrary user-supplied C# must not be compiled inside privileged physical hosts.
- A seeded random generator must have specified sampling order/per-channel streams; global shared mutable `Random` across concurrent calls compromises reproducibility.

## 5. Delay model

Explicitly distinguish:
- **Command transport delay:** sending/receiving a SCPI or remote-host request.
- **Instrument processing delay:** settling, integration (NPLC/aperture), switching, warmup, trigger wait and acquisition.
- **Physical effects:** DUT thermal time constants or RC behavior driven by simulation elapsed time.
- **Display delay:** optional client demo pacing, which must not change physics.

Each delay has a duration in UnitsNet or well-defined time units, a time domain (simulation vs wall), optional deterministic jitter and cancellation behavior. A simulated delay uses the shared scheduler (`DelayAsync`), never `Thread.Sleep`. Cancellation of waiting doesn't imply instrument side effects were rolled back; maintain explicit state.

**Example:** a DMM integrates for 10 NPLC at 60 Hz → nominal 166.67 ms of simulated integration before additional delays, where supported and validated against the specific model. Under 100× acceleration, ideal pacing is ~1.67 ms real time. An operator may choose to display at 1× pacing despite accelerated simulation.

## 6. Async and cross-instrument execution

- Every mock accepts a run-scoped `ISimulationClock` service; `Now` returns current simulation elapsed duration and `DelayAsync` schedules an awaitable virtual delay.
- All instruments within one virtual bench observe the **same clock** and schedule events deterministically.
- `Task.WhenAll` is supported across independent simulated instruments; single-instrument command serialization and host reservations still apply.
- Determinism must not depend on thread-race order. Where concurrent events have identical timestamps, use a defined sequence/tie-break rule.
- Partially ordered expected-call specifications must distinguish invoke, acknowledged, complete and acquired-data events.
- Manual advancement should complete timers in timestamp/order sequence, including cascaded callbacks, with limits against infinite zero-delay loops.

## 7. Measurements and units

Use UnitsNet for physical quantities. Record `Measurement<T>` with unit/dimension, basis (DC/RMS/peak/Vpp), acquisition timestamp, quality flags, uncertainty model (if known), source/driver identity and simulation vs physical provenance. Resist treating sensor noise as metrological uncertainty without an explicit model.

For versioned gRPC/JSON messages, send canonical numeric unit values with unit identifiers and schema version; never serialize CLR UnitsNet objects across processes.

## 8. Logging and replay

Log initialization, time-domain changes, seed, scheduled/actual completion timestamps, model state, command receipt, measurement production, fault injection, cancellation and run cleanup. Event IDs and clock IDs must correlate across instrument mocks, experiment runner and client dashboard. Wall timestamps are for diagnostics; simulation timestamps determine replay. Redact sensitive commands by default; have an explicit debug policy for simulation-specific values.

## 9. Safety boundaries

Simulation speed applies **only** to simulated behavior. Physical VISA/IVI hardware timeouts, lease TTLs, network heartbeats, authentication token lifetimes and safety interlocks use real time. Never allow a mock acceleration flag to shorten the real hardware's required settling or warmup. Disallow accidentally routing mock-only state mutations into actual equipment.

## 10. Acceptance criteria

1. Initialization sets defined state; reset has separately specified semantics.
2. DMM sine mock observed at 0, quarter and half cycles returns expected deterministic values.
3. Same seed and fixed logical times produce identical measurements and traces across reruns.
4. 1× and 100× produce the same *simulated time-series*, with different wall duration.
5. Manual mode can advance 1 hour of simulated time without an actual hour-long wait.
6. Instruments sharing a virtual bench see one monotonic clock and stable ordering.
7. Delays/jitter are nonnegative and dimensionally validated; malformed specs fail generation.
8. Timeouts/cancellation preserve correct state and never silently imply safe hardware.
9. Host/client logging displays wall and simulated times without conflating them.
10. Real instrument lease/safety timeouts remain independent of simulation scaling.

## Implementation plan

1. Add portable `ISimulationClock` interface, real-time, accelerated and manual implementations with `TimeProvider` integration where applicable.
2. Add separate `*.mock.json` validated profile and generated initializers/operation behavior binding.
3. Wire `ISignalBehavior` and `SeededRandomBehavior` to clock-controlled acquisition.
4. Implement a virtual-bench scheduler and deterministic per-instrument seeded noise streams.
5. Add timing tests and instrument-session integration.
6. Add optional browser controls for pause/step/speed and synchronized time-axis plots.
7. Integrate simulator clock ID and dual timestamps with remote instrument-host logs.

**Current implementation:** isolated `SineBehavior`, `SeededRandomBehavior`, and `SumBehavior` types exist; shared clock, per-operation mock specs, scaled latency and generation are not yet implemented.
