# Virtual Bench Wiring Graph — Typed Signals, Dependency Validation and Snapshots

**Status (2026-10-15):** Initial typed wiring graph, tests, and thermal demo integration committed. Builds and runtime verification still pending.

## Decision

Use a **run-scoped, typed directed acyclic graph (DAG)** for physical coupling among simulated instruments. The graph represents causal dependencies, not SCPI communications and not the telemetry/event transport. Use constructor injection for the run's physical models, a graph builder for dynamic wiring, and a shared simulation clock for time.

### Model

```text
  ChamberModel: Temperature ────────┐
                                    ▼
                             ThermalSensor: Temperature
                                    │
                                    ▼
  PowerSupplyVoltage: ElectricPotential ──► DUTLoad: ElectricCurrent
                                              │
                                              ▼
                                    Measurement / Limits / Plot
```

Each port has a stable human-readable ID and a C# type. `SignalPort<Temperature>` cannot connect directly to `SignalPort<ElectricCurrent>` using `Connect`; conversions require a declared typed `Transform`. The graph does not use a string-keyed `object` dictionary as its public API.

## Working API (source committed)

```csharp
var wiring = new VirtualBenchGraphBuilder();
var chamberPort = wiring.Port<Temperature>("chamber.setpoint");
var sensorPort = wiring.Port<Temperature>("dut.sensor");
var voltagePort = wiring.Port<ElectricPotential>("supply.voltage");
var currentPort = wiring.Port<ElectricCurrent>("dut.current");

var chamber = new ThermalChamberModel(ThermalCycleSettings.Example());
var sensor = new FirstOrderThermalSensor(chamber,
    TimeSpan.FromMinutes(20), Temperature.FromDegreesCelsius(30));

wiring.Source(chamberPort, t => chamber.ReadAt(t));
wiring.Transform(chamberPort, sensorPort,
    (_, t) => sensor.ReadAt(t));
wiring.Source(voltagePort, _ => ElectricPotential.FromVolts(12));
wiring.Transform(sensorPort, voltagePort, currentPort,
    (temp, voltage, t) =>
        ElectricCurrent.FromAmperes(voltage.Volts / 24 *
            (1 + 0.1 * Math.Clamp((temp.DegreesCelsius - 30) / 40, 0, 1))));

VirtualBenchGraph graph = wiring.Build();
VirtualBenchSnapshot atHourOne = graph.SampleAt(TimeSpan.FromHours(1));
Temperature sensed = atHourOne.Read(sensorPort);
ElectricCurrent current = atHourOne.Read(currentPort);
```

This code uses the existing thermal sensor as a **stateful transform**. The dependency is explicit, and its output is cached per snapshot. Stateful models still require monotonically increasing sampling time, and they must be constructed per run.

## Guarantees in this increment

1. **Type-safe wiring:** direct connections require identical `SignalPort<T>` types. Two-input transforms explicitly declare the conversion function and its input/output types.
2. **One driver per port:** duplicate source assignments fail immediately.
3. **Complete graph:** missing sources fail at `Build()`.
4. **Cycle detection:** directed cycles fail at `Build()` before the simulation runs.
5. **Run isolation:** ports carry an internal graph identity; cross-graph wiring and reads fail.
6. **Coherent logical time:** every read from a snapshot evaluates at its `Elapsed` timestamp.
7. **Snapshot caching:** a source is evaluated at most once within a snapshot, even if several downstream mocks consume it.
8. **Frozen wiring:** a built graph copies its bindings, so later edits to a builder do not change that graph.
9. **Unit-safe dimensions:** UnitsNet quantity types are checked by C# generic types. Units with the same physical dimension (e.g. volts/millivolts) use the same `ElectricPotential` type; unit conversion occurs in the quantity, not in string identifiers.
10. **No network dependency:** physical coupling is local to a simulation worker. Telemetry may be distributed separately.

## What is not guaranteed yet

- **Atomic wall-clock snapshot:** snapshot caching is not a transactional lock over mutable external state. Sources should be pure time-indexed functions or use a runner-controlled, frozen model state.
- **Thread-safe concurrent reads of one snapshot:** the current snapshot cache is not synchronized. The runner should evaluate it on its simulation scheduler and publish an immutable result. Parallel callers must not mutate/read the same snapshot concurrently until that support is implemented.
- **Random-order independence:** stateful/random sources need run-seeded, port-specific deterministic generators. The current graph caches within a snapshot but does not solve order-dependent randomness across snapshots.
- **Algebraic loops:** a feedback cycle is rejected, not numerically solved. Explicit delayed feedback/stateful update nodes or an ODE solver will be required for coupled circuits/thermal systems.
- **Physical calibration:** the simple thermal sensor and load models are illustrative.
- **Graph persistence/editor:** no JSON graph schema, migration, UI node editor, or dynamic hot-rewire exists yet.
- **Distributed shared state:** multiple host workers must use an authoritative simulation coordinator to share time and state; DI graph objects are not serializable distributed state.

## Design guidance

**Use a graph for physics, Channels/Dataflow for events.** A sensor should read a temperature signal at a specified logical time, not subscribe to an unreliable telemetry queue and guess what the current temperature is. The telemetry layer observes evaluated snapshots and distributes immutable measurements, using bounded queues and durable event IDs.

**Use DI for lifetime and selection.** `Microsoft.Extensions.DependencyInjection` creates the chamber, sensor, load, clock, and graph per experiment. DI is not the simulation scheduler, wiring graph, lease manager, or safety interlock.

**Separate setpoint and actual DUT temperature.** The example's chamber setpoint drives a first-order thermal sensor. The original power-supply mock still uses setpoint-dependent current, while the graph now also computes **sensor-driven DUT current** as a separate series. These differ during ramps and demonstrate why physical coupling matters. In a future increment, inject a graph-backed load behavior into the power supply so its own reading uses the DUT signal.

## Verification

Added `tests/BenchDouble.Tests/VirtualBenchGraphTests.cs` covering:
- typed two-input current calculation;
- shared-source evaluation exactly once per snapshot;
- cycle detection;
- missing source, duplicate driver, duplicate port ID;
- cross-graph isolation;
- stateful filtered thermal sensor as a transform.

The C# thermal demo now builds the graph, samples it every five simulated minutes, and exports `dut-current.plotly.json` in addition to chamber, sensor, and supply plots.

## Next increments

1. Run `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj` and thermal console demo on CI.
2. Add `GraphLoadBehavior` consuming the graph at a run-scoped snapshot and replace the separate setpoint-based current implementation where selected.
3. Add immutable snapshot export, thread-safe scheduler ownership, seeded noise decorators, and graph schema validation.
4. Add typed pressure, resistance, heat-flow, voltage and current ports with capability metadata.
5. Add delay/state nodes for feedback, followed by optional solver interfaces for strongly coupled models.
6. Add a visual wiring editor only after the underlying graph API is validated.

See also [signal sharing](VIRTUAL-BENCH-SIGNAL-SHARING.md), [thermal example](THERMAL-CYCLE-MONITOR.md), and [simulation time](MOCK-TIME-AND-LIFECYCLE.md).
