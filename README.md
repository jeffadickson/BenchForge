# BenchDouble

**Real tests. Imaginary instruments.**

BenchDouble is an experimental .NET library for testing automated instrument-control applications without physical laboratory hardware.

## Initial scope

- VISA-like query/write session abstraction
- Stateful SCPI simulators for a Keysight 33500-family function generator and 34461A-style digital multimeter
- Strict, fail-fast expected command sequences, including cross-instrument transcripts
- Unit tests using xUnit
- Hardware-independent tests and deliberate protocol-failure injection

This is an early prototype, **not** a certified, hardware-accurate emulation of these models. Commands are a deliberately small supported subset; validate against instrument manuals before using on real equipment.

## Quick start

Install .NET SDK 8 or newer:

```bash
dotnet test
```

## Project layout

- `src/BenchDouble/`: transport abstraction, typed drivers, stateful devices and strict replay
- `tests/BenchDouble.Tests/`: end-to-end protocol tests
- `docs/ROADMAP.md`: design priorities and implementation status
- `.github/workflows/test.yml`: cross-platform CI

## Architecture

```text
Test procedure
  -> typed instrument driver (generator / DMM)
  -> IScpiSession
        -> Stateful simulator
        -> Strict replay / shared interaction timeline
        -> Future: real VISA bridge (NI / Keysight / IVI)
```

We intentionally keep real VISA vendor SDK packages out of the core assembly. The simulated implementations are suitable for portable unit tests, including Windows, macOS and Linux.

## Important limitations

The simulator models selected SCPI strings, not hardware timing, full SCPI command trees, measurement uncertainty, electrical loading or real VISA status semantics. Never use this prototype as a substitute for hardware validation.

## License

License pending selection by repository owner.


## IVI instrument classes

BenchDouble now includes **experimental IVI-inspired** function-generator and DMM contracts and SCPI adapters. See [IVI integration design](docs/IVI-INTEGRATION.md). These are deliberately not advertised as official IVI.NET interfaces or IVI-conformant drivers; native IVI integration is a follow-on milestone.

Example:

```csharp
var model = new GeneratorSimulator();
BenchDouble.Ivi.IIviFunctionGenerator generator =
    new BenchDouble.Ivi.ScpiFunctionGeneratorAdapter(model, simulationEnabled: true);
generator.ConfigureWaveform(1, BenchDouble.Ivi.IviWaveform.Sine, 1000, 1.0);
generator.SetOutputEnabled(1, true);
```


## Detailed design and current work

- [Full software design description](docs/SOFTWARE-DESCRIPTION.md) — engineering requirements, architecture, IVI integration, SCPI/VISA, expected sequences, virtual bench, fault models, verification and release criteria.
- [IVI-specific integration](docs/IVI-INTEGRATION.md)
- [Development roadmap](docs/ROADMAP.md)

The implementation is currently a **partial prototype**. Official IVI.NET conformance, native VISA sessions, electrical DUT models and successful CI verification are not yet established.

## Official IVI.NET integration (experimental)

The optional [BenchDouble.Ivi.Official](src/BenchDouble.Ivi.Official/) project now references **actual IVI Foundation NuGet assemblies** for DMM, Fgen, and driver core. It provides official interface identity checks and a configurable DMM bridge. It **does not yet provide** fully mapped native IVI operations or vendor-driver loading. [Integration details](docs/OFFICIAL-IVI.md).

Build separately with `dotnet build src/BenchDouble.Ivi.Official/BenchDouble.Ivi.Official.csproj`. Its dependencies are intentionally excluded from the portable core. The optional build has not been confirmed passing.

### Official IVI contract tests

The optional project now provides **official DMM and function-generator typed bridges** and isolated tests using official IVI interface assemblies. Run with:

```bash
dotnet test tests/BenchDouble.Ivi.Official.Tests/BenchDouble.Ivi.Official.Tests.csproj
```

Passing those tests would validate type-level adapter behavior, **not** interoperability with a physical vendor instrument or IVI conformance.

## Product direction: instrument control plus integrated test doubles

BenchDouble is being developed as a **production instrument-control framework**, with mocking and stateful simulation as interchangeable testing backends—not just a mock library. The project and package names remain provisional until a separate naming decision. See the [package architecture and requirements](docs/PACKAGE-ARCHITECTURE.md) for the boundary definitions, dependency rules, interoperability acceptance test, migration plan and status. No public API relocation has been performed as part of this design change.

## Generic instrument interfaces and research

The library now includes initial `BenchDouble.Contracts` interfaces for DMMs, function generators (with channels), and oscilloscopes, plus optional NPLC/aperture integration and vendor-extension contracts. The first DMM and FGEN SCPI-backed generic adapters and xUnit tests are committed. See [instrument interface research](docs/INSTRUMENT-INTERFACES-RESEARCH.md) for IVI Foundation, PyMeasure, QCoDeS, pyvisa-sim and Scpi.NET comparisons. The classes are currently part of the original assembly, not yet separate NuGet packages.

## Declarative SCPI parameters (experimental)

The new `BenchDouble.Declarative` namespace provides PyMeasure-inspired **`ScpiParameter.Control`**, **`Measurement`** and **`Setting`** factories, with immutable metadata, validated get/set commands, typed wire codecs and channel binding. See [design patterns](docs/DESIGN-PATTERNS.md) and [regression tests](tests/BenchDouble.Tests/DeclarativeScpiTests.cs).

```csharp
using BenchDouble.Declarative;
var frequency = ScpiParameter.Control(
    new ParameterMetadata("Frequency", "Output frequency", "Hz", 1, 20_000_000),
    "SOUR{channel}:FREQ?", "SOUR{channel}:FREQ {value}", DoubleCodec.Instance);
var channel2 = new ScpiChannelGroup(session).Channel("2");
channel2.Parameter(frequency).Set(1_000);
```

These are **source-level parameter descriptors**, not .NET auto-properties or a complete SCPI parser. Device-specific limits require manufacturer verification. The new C# tests have been committed but not run locally because this environment lacks the .NET SDK.

## Structured logging and telemetry

The new `BenchDouble.Observability.LoggingScpiSession` decorator uses `Microsoft.Extensions.Logging` and may wrap a physical, simulated, or strict replay SCPI session. By default, it **redacts instrument commands and query responses** while logging operation type, instrument name, elapsed duration and failures. Raw payload logging is independent and explicitly opt-in. See [logging architecture](docs/LOGGING.md) and [logging tests](tests/BenchDouble.Tests/LoggingScpiSessionTests.cs).

The library also has a [design-pattern guide](docs/DESIGN-PATTERNS.md) covering Adapter, Strategy, Abstract Factory, Composite, Decorator, Command, State, Builder, Specification and Observer; the [main software description](docs/SOFTWARE-DESCRIPTION.md) now incorporates the decisions and status directly.
