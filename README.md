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

## PyMeasure-inspired drivers + official IVI interoperability

**Architecture decision:** use official IVI instrument classes and vendor adapters for standardized real control; selectively adapt PyMeasure's tested driver patterns into idiomatic C#, not a wholesale port of Python experiments and GUI tooling. The [port strategy](docs/PYMEASURE-PORT-STRATEGY.md) details evaluated upstream sources, copyright obligations, target drivers and milestones.

Initial C# functionality includes `ScpiParameter` factories, `MappedCodec<T>` for vendor SCPI enumeration tokens and composable `ParameterValidators` for bounded and discrete settings (e.g. NPLC). See [tests](tests/BenchDouble.Tests/DeclarativeValidationTests.cs). No complete PyMeasure checkout or general-purpose Python-to-C# conversion has been made.

## Async instrument APIs

The first asynchronous APIs now live in [`BenchDouble.Async`](src/BenchDouble/Async/AsyncInstruments.cs). They provide `IAsyncScpiSession`, a session-serialization decorator, typed async DMM/generator drivers and a compatibility adapter for existing synchronous simulators. See [tests](tests/BenchDouble.Tests/AsyncInstrumentTests.cs). The compatibility adapter remains synchronous underneath and does not create true nonblocking VISA operations. Cancellation does not guarantee hardware abort or a confirmed safe state. The implementation has been committed but is not yet verified by a successful .NET build.

## Exclusive instrument reservations

`BenchDouble.Async.InstrumentReservationManager` now supports cancellable, exclusive in-process resource leases. Use one shared manager instance and keep reservations for the entire experiment—including cleanup—not merely around one command. [Source](src/BenchDouble/Async/InstrumentReservations.cs) · [Tests](tests/BenchDouble.Tests/InstrumentReservationTests.cs). This is not cross-process locking; currently the instrument factory does not enforce it automatically.

## Distributed instrument hosts (planned)

BenchDouble will support a standalone instrument host on each computer physically connected to USB, GPIB, serial or LAN test equipment, including suitable Raspberry Pi or low-cost PCs. The host—not each client—owns device drivers, authenticated exclusive leases, asynchronous operations and recovery policy. Clients can reserve and operate remote instruments through the same typed API as local simulators. See [Distributed Instrument Host Architecture](docs/DISTRIBUTED-INSTRUMENT-HOST.md). **Not yet implemented:** gRPC server/client and cross-process locking.

## Instrument logs on both hosts and clients (planned)

Each host will maintain local structured instrument logs and stream the same authorized events to connected clients for live experiment displays. Event IDs, trace correlation, cursor replay, host dashboard/CLI access and data redaction are specified in [Distributed Instrument Host Architecture](docs/DISTRIBUTED-INSTRUMENT-HOST.md). Currently only the local `LoggingScpiSession` decorator exists; live remote log streaming is not implemented.

## Platform-neutral instrument hosts

An instrument host can be **any computer with a supported BenchDouble runtime and the necessary instrument drivers and interfaces**—a workstation, server, laptop, industrial PC, mini PC, or embedded system. A Raspberry Pi is only one possible deployment. The service API is platform-neutral; specific VISA/IVI/USB/GPIB driver support must be validated per operating system and architecture. See [distributed host architecture](docs/DISTRIBUTED-INSTRUMENT-HOST.md).
