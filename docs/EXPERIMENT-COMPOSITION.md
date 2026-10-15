# Experiment-Scoped Dependency Injection and Instrument Registration

**Status:** simulation-only implementation committed; physical host discovery and lease acquisition remain planned.

## Architecture decision

An **ExperimentDefinition** describes named instrument roles, simulated/physical backend selection, behavior configuration and run parameters. An **ExperimentRun** owns a fresh DI container, run scope, virtual clock and instrument instances. This follows the **Inversion of Control** principle: ordinary instrument and behavior classes receive their dependencies rather than looking them up globally.

The new `BenchDouble.Experiments` project uses `Microsoft.Extensions.DependencyInjection` only at the composition boundary. The core instrument library remains independent of the container.

## How to run

```bash
dotnet run --project examples/BenchDouble.ExperimentDemo/BenchDouble.ExperimentDemo.csproj
```

The example registers `Chamber1` and `Supply1`, composes a first-order thermal sensor, injects a temperature-dependent load, advances the shared virtual clock and prints the instrument roles and readings.

## C# API

```csharp
var builder = new ExperimentDefinitionBuilder();
builder.Model(_ => new ThermalChamberModel(ThermalCycleSettings.Example()));
builder.Model<ITemperatureProgram>(sp =>
    sp.GetRequiredService<ThermalChamberModel>());
builder.Model(sp => new FirstOrderThermalSensor(
    sp.GetRequiredService<ITemperatureProgram>(),
    TimeSpan.FromMinutes(20), Temperature.FromDegreesCelsius(30)));
builder.Model<ITemperatureSignal>(sp =>
    sp.GetRequiredService<FirstOrderThermalSensor>());
builder.Behavior<ILoadBehavior, SensorDrivenLoad>();
builder.Simulated<IThermalChamber, SimulatedChamber>("Chamber1");
builder.Simulated<IPowerSupply, SimulatedSupply>("Supply1");

var definition = builder.Build("Thermal qualification");
await using var run = definition.StartSimulated();
var supply = run.Instrument<IPowerSupply>("Supply1");
```

The example's `SensorDrivenLoad` is a plain constructor-injected C# class; it does not know about the DI container.

.NET 8 keyed services support multiple instruments implementing the same interface. `[FromKeyedServices("Chamber1")]` is available when a behavior needs a specific named role. See the [experiment tests](../tests/BenchDouble.Experiments.Tests/ExperimentCompositionTests.cs).

## What the current code guarantees

- Stable unique instrument role IDs in a definition.
- Keyed simulated instrument registrations.
- Scoped constructor injection for models/behaviors.
- Fresh clock and DI scope per experiment run.
- Typed retrieval at the composition boundary, with invalid roles rejected.
- Asynchronous scope/provider disposal.
- Tests for cross-instrument constructor injection, run isolation, duplicate roles and incorrect lookups.

## What is not implemented

- No actual physical host discovery, remote proxy binding or lease acquisition in `StartSimulated`.
- No serialized/versioned experiment definition or signed mock behavior package manifest.
- No initialization rollback for real equipment, partial failures, or physical safe-state confirmation.
- No automatic cycle detection for arbitrary constructor DI dependencies beyond what the container can detect; the separate virtual wiring graph handles signal graph cycles.
- No per-instrument host activity aggregation in the experiment API yet; the dashboard currently monitors one local demonstration host.

## Why not replace the virtual signal graph?

Constructor injection is the default for ordinary custom mock behaviors. The typed signal graph remains useful for dynamically assembled models, time-consistent snapshots, cycle validation, and future visual wiring. Neither DI nor Channels/Dataflow alone is a physics solver.

## Debugging and testing

The injected behavior is normal C#, so use xUnit breakpoints and deterministic virtual time. Reqnroll verifies acceptance scenarios. Roslyn-compiled custom behavior loading must use isolated simulation workers and immutable package hashes.

See [ADR-001](ADR-001-EXPERIMENT-SCOPED-COMPOSITION.md) and [Version Control Tutorial](VERSION-CONTROL-TUTORIAL.md).

## AutoMock rules at the composition root

The experiment builder now offers `AutoMockLoad(baselineFactory, rules => ...)` and `AutoMockLoadFromJson(baselineFactory, json)`. Both register a normal scoped `ILoadBehavior` decorator; instruments do not parse JSON or call `IServiceProvider`. Rules are frozen with the experiment definition and identified by `AutoMockRuleSha256`. Only one default `ILoadBehavior` AutoMock is supported in this increment; **keyed per-instrument rule registration** remains tracked in issue #40. Run the fluent and JSON examples with `make demo-automock` and `make demo-automock-json`. See [AutoMock rule tutorial](AUTOMOCK-RULES.md).
