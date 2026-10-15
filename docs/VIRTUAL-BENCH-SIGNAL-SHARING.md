# Sharing Physical Signals Between Instrument Mocks

## Architectural decision

**Use a run-scoped, dependency-injected virtual environment with strongly typed signal interfaces.** Do not use a process-global mutable dictionary, and do not make mocks issue SCPI commands to each other to discover their physical inputs. The runner owns a shared logical clock; each mock receives the relevant signal sources in its constructor.

```text
                         Simulation run scope + shared clock
                                      |
                           ThermalChamberModel
                         ITemperatureProgram (°C)
                            /                 \
            FirstOrderThermalSensor     TemperatureDependentLoadBehavior
            ITemperatureSignal              ILoadBehavior
                  |                              |
         virtual temperature probe        MockPowerSupply
                  \                             /
                   typed measurement/event snapshots
                                  |
                       runner + plots + logs
```

## Mechanisms by purpose

| Need | Preferred mechanism | Why |
|---|---|---|
| Mock A needs physical state from Mock B | DI of `ITemperatureSignal`, `IVoltageSignal`, etc. | Typed coupling, simple tests, no bus protocol duplication |
| Several models need shared simulation time | Run-scoped `ISimulationClock` | Deterministic elapsed time and acceleration |
| Complex coupled physics | Explicit virtual-DUT model and ordered simulation scheduler | Avoid uncontrolled callback loops |
| Telemetry and live UI | `System.Threading.Channels` or TPL Dataflow | Bounded asynchronous publication and backpressure |
| Many remote subscribers | Host-owned per-subscriber streams with durable event IDs | Avoid `BroadcastBlock` delivery-loss assumptions |
| Model registration and lifecycle | `Microsoft.Extensions.DependencyInjection` | Per-run isolation and swappable behaviors |

**Important:** Dependency injection establishes references and ownership; it does not itself provide a thread-safe event bus or a simulation scheduler.

## Implemented thermal sensor

The new `ITemperatureSignal` interface returns a UnitsNet `Temperature` at a requested simulation time. `ITemperatureProgram` additionally supplies piecewise-linear breakpoint times. `ThermalChamberModel` implements the program interface.

`FirstOrderThermalSensor` takes `ITemperatureProgram`, a `TimeSpan` time constant, and an initial UnitsNet temperature. It models:

```text
dT_sensor/dt = (T_chamber - T_sensor) / tau
```

For each linear setpoint segment of duration Δt, the exact update is:

```text
slope = (T_set_end - T_set_start) / Δt
T_sensor_end = T_set_end - slope*tau
             + (T_sensor_start - T_set_start + slope*tau) * exp(-Δt/tau)
```

The algorithm splits advancement at all known chamber-program breakpoints. Consequently, reading once per hour or once per minute gives the same modeled result (within floating-point rounding), and two readers at the same logical time cannot advance the sensor twice. A lock protects state; backward-time reads are rejected. Create a fresh sensor per simulation run.

**Example:** with a 20-minute time constant and a 30→70°C ramp lasting 1 hour, the chamber reaches 70°C at t=1h while the sensor is approximately 57.33°C. During the two-hour hot dwell, it approaches 70°C. This demonstrates why a DUT soak should be qualified from actual sensor temperature rather than chamber setpoint.

## Dependency injection

```csharp
services.AddScoped<ManualSimulationClock>();
services.AddScoped<ISimulationClock>(sp =>
    sp.GetRequiredService<ManualSimulationClock>());
services.AddScoped(_ => ThermalCycleSettings.Example());
services.AddScoped<ThermalChamberModel>();
services.AddScoped<ITemperatureProgram>(sp =>
    sp.GetRequiredService<ThermalChamberModel>());
services.AddScoped<FirstOrderThermalSensor>(sp =>
    new FirstOrderThermalSensor(
        sp.GetRequiredService<ITemperatureProgram>(),
        TimeSpan.FromMinutes(20),
        Temperature.FromDegreesCelsius(30)));
```

A future thermal-current model could depend on the **sensor** rather than chamber setpoint simply by injecting `ITemperatureSignal`. This should be configurable because different physical effects couple to different temperatures (air, fixture, DUT junction).

## Why not a dictionary or a publish/subscribe bus for physical coupling?

A string-keyed global registry (`Get("Chamber1.Temp")`) loses compile-time dimensional checks and creates hidden dependencies. An event bus is useful for telemetry, but physical models need a coherent state at a specified logical time. A subscriber may receive events late, out of order or not at all. The simulator should therefore read the authoritative typed signal/snapshot; events are an observation of that state, not the state itself.

For larger coupled systems, add an explicit graph of signal providers and consumers, reject cycles unless a numerical solver is configured, and evaluate the graph in deterministic timestamp order. No model should advance the global clock on an ordinary read.

## Remaining work

- Sensor noise, calibration offset, quantization, response time, lag variation and fault injection as decorators.
- Separate chamber air, fixture, DUT case and junction temperatures, with thermal coupling and heat capacity.
- A coherent `VirtualBenchSnapshot` for atomic multi-signal sampling at one logical timestamp.
- Shared signal/port registry with typed IDs, graph validation and cycle detection for dynamically composed mocks.
- Stable event stream with bounded queues and host replay; distinct from the physics graph.
- Verified .NET build and browser rendering.

## Files and verification

- `src/BenchDouble/Simulation/ThermalSignals.cs`: typed signal interfaces and first-order sensor.
- `src/BenchDouble/Simulation/ThermalCycle.cs`: chamber implements `ITemperatureProgram`.
- `examples/BenchDouble.ThermalCycleDemo/Program.cs`: DI registration, simulated sensor samples, Plotly JSON export.
- `examples/ThermalCycleMonitor/index.html`: chamber/sensor overlays and live sensor readout.
- `tests/BenchDouble.Tests/ThermalSignalsTests.cs`: lag, deterministic sampling, boundary continuity and time ordering.

**Status:** code committed, build and visual tests not yet verified. Models are idealized; no calibrated instrument or physical safety claim.
