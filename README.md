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

## Separate instrument host service

The repository now has independent projects for [Host.Contracts](src/BenchDouble.Host.Contracts/), [Host.Server](src/BenchDouble.Host.Server/), and [Host.Client](src/BenchDouble.Host.Client/), plus [host tests](tests/BenchDouble.Host.Tests/). The initial HTTP API demonstrates instrument discovery, simulation-backed exclusive reservation, leased SCPI queries and release. Run `dotnet run --project src/BenchDouble.Host.Server` and use `http://127.0.0.1:5278` locally. **Not safe for network exposure:** authentication, TLS, fenced expiring leases, logging streams and physical VISA are not yet implemented. See [host structure and next steps](docs/HOST-REPO-STRUCTURE.md).

## Versioned host protocol and shared instruments

The target network protocol uses versioned protobuf/gRPC contracts instead of serialized .NET objects. The [v1 schema draft](proto/benchdouble/host/v1/host.proto) and [sharing policy contracts](src/BenchDouble.Host.Contracts/SharingPolicies.cs) define exclusive, read-only, observer and host-sampled subscription modes. Future optional driver uploads are identified by content hash and require trusted signatures and isolated execution; untrusted code must never run inside the privileged host. See [Versioned Drivers and Sharing](docs/VERSIONED-DRIVERS-AND-SHARING.md). These are **contracts/design only**; the current HTTP server still supports exclusive in-memory leases only.

## UnitsNet quantities

BenchDouble now references stable `UnitsNet` and includes [typed instrument facades](src/BenchDouble/Quantities/TypedInstruments.cs) for voltage and sine-generator quantities. Examples: `ElectricPotential.FromMillivolts(500)`, `Frequency.FromKilohertz(10)`. The existing `double` methods are unchanged for compatibility. See [units and measurements](docs/UNITS-AND-MEASUREMENTS.md). Resistance/current typed contracts are present but their SCPI implementations are not yet supported.

## Declarative instrument definitions and generated C# drivers

Drivers will be authored from versioned, validated [instrument description files](instrument-definitions/keysight/33522b.instrument.json), then compiled into strongly typed C# using a planned **Roslyn incremental generator**. The input specifies IVI class, SCPI commands, UnitsNet quantity types, channel templates, validation limits and feature metadata. See [Driver Generation Architecture](docs/DRIVER-GENERATION.md). This phase introduces the description format and generator specification; it does **not** yet implement source generation.

## Standalone driver generator and programmable mocks

The driver-generation approach is now an **explicit build step**, not a Roslyn incremental generator. Run `make generate` or `dotnet run --project tools/BenchDouble.DriverGen -- instrument-definitions src/BenchDouble/Generated` to emit ordinary editor-visible C# source. The generator currently supports a limited subset of SCPI numeric properties and UnitsNet types; it is not a fully validated IVI/PyMeasure converter. The simulation library also includes programmable `SineBehavior`, seeded random voltage-style readings, and composed signal/noise behaviors; JSON-driven mock generation is planned. See [Driver Generation](docs/DRIVER-GENERATION.md). Build verification is outstanding.

## Timed, programmable instrument mocks

The mock design now explicitly separates instrument initialization from operation/measurement behaviors, and introduces a **shared simulation time domain**. Proposed real-time (1×), accelerated, paused and manually advanced modes allow realistic acquisition/warmup delays without slowing tests. Security leases and physical hardware timing always remain on real time. See [Mock Time and Lifecycle Specification](docs/MOCK-TIME-AND-LIFECYCLE.md). The behavior types exist, but clock/scaling and mock-schema generation are not yet implemented.

## Consolidated test-lab runner requirements

The [Test-Lab Runner Requirements baseline](docs/TEST-LAB-RUNNER-REQUIREMENTS.md) consolidates **TR-001 through TR-090** across structured authoring, async execution, distributed hosts, instrument sharing, simulation, durable results, redlines, signatures, unified UI, observability and verification. Requirements are prioritized and distinguish implemented prototypes from planned work. The runner and editor share one structured procedure representation; TestForge may integrate formal qualification/evidence functions without duplicating the execution engine.

## Unified instrument operations: 25 real PyMeasure examples

The [source-verified 25-operation PyMeasure comparison](docs/INSTRUMENT-OPERATIONS-25-PYMEASURE-EXAMPLES.md) demonstrates how one typed UnitsNet instrument API can work with local hardware, local stateful mocks, and a generic remote proxy to a host using either physical or simulated drivers. Examples come from Agilent 34410A, Agilent 33500 and Keithley 2000 source files, with real SCPI command mappings and separate proposed BenchDouble mock behavior. This is an architecture reference; the 25 operations have not all been ported.

## 25 real PyMeasure instruments and Keithley 236

The [25-instrument PyMeasure catalog and Keithley 236 coverage report](docs/PYMEASURE-25-INSTRUMENTS-AND-KEITHLEY-236.md) corrects the earlier 25-property comparison: it inventories **25 distinct PyMeasure instrument driver entries**, with explicit source-check status. PyMeasure does not currently list a Keithley 236 driver, so BenchDouble's [236 typed DC source/measure driver](src/BenchDouble/Instruments/Keithley/Keithley236.cs) is based on Keithley's own legacy IEEE-488 command manual, **not SCPI**. The [236 resistive-DUT mock](src/BenchDouble/Simulation/Keithley236MockBus.cs) and [tests](tests/BenchDouble.Tests/Keithley236Tests.cs) cover a substantial DC core, but sweeps, triggers, status, actual GPIB transport and hardware safety verification remain unfinished.

## Unit-aware plotting (ScottPlot + Plotly.js)

The new [plotting architecture](docs/PLOTTING-AND-UNITS.md) keeps the runner independent of visualization packages. `src/BenchDouble.Plotting` converts UnitsNet quantity series into typed, labeled plot coordinates and emits Plotly.js figure JSON for the future browser runner. Optional `src/BenchDouble.Plotting.ScottPlot` renders headless PNG using ScottPlot 5.1.59. `Plotly.NET` is evaluated but not required for direct Plotly.js rendering; LiveCharts2/OxyPlot remain optional alternatives. Unit conversion and Plotly JSON tests have been added, but compilation and rendering remain unverified.

## Standalone UnitsNet plotting companion

`BenchDouble.Plotting` has been decoupled from the BenchDouble instrument library: it now depends directly on UnitsNet and has [independent tests](tests/UnitsNet.Plotting.Tests/). This makes it a candidate for a future standalone `UnitsNet.Plotting` package and upstream proposal, without forcing ScottPlot or Plotly dependencies into UnitsNet itself. The current names remain provisional; no upstream submission or package publication has occurred. See [plotting architecture](docs/PLOTTING-AND-UNITS.md).

## Dependency injection and programmable mock demo

BenchDouble now has a constructor-injected `ILoadBehavior` extension point for the [mock power supply](src/BenchDouble/Simulation/PowerSupplyBehavior.cs). The runnable [DI demo](examples/BenchDouble.DiDemo/) uses `Microsoft.Extensions.DependencyInjection` at the application composition root to choose a 24-ohm resistor or a timed 2.5 A overcurrent fault at virtual t=5 seconds. The unchanged experiment loop trips at 2 A and disables the simulated output. Run `make demo` or `make demo-normal`, or invoke `dotnet run --project examples/BenchDouble.DiDemo` (add `-- --normal` for no fault). [Full tutorial](docs/DEPENDENCY-INJECTION-AND-MOCK-BEHAVIORS.md). DI container is deliberately not required by the instrument core. Initial unit tests cover behavior swapping and isolated run scopes; build execution has not been verified here. This is a simplified virtual supply, not physical compliance or a safety interlock.

## Unattended Operations Monitor demonstration

The [Unattended Operations Monitor](examples/UnattendedOperationsMonitor/index.html) is a standalone browser demo with live current plotting, threshold alarms, simulated output shutdown and event logs showing **simulated email/SMS notifications**. Open the HTML file directly; it requires no server. The C# [DI demo](examples/BenchDouble.DiDemo/) also has an injectable `IOperatorNotificationSink` and a fake console notifier. **No real messages are sent and no physical equipment is controlled.** [Architecture, demo steps and production requirements](docs/UNATTENDED-OPERATIONS-MONITOR.md).

## Thermal Cycle Monitor demonstration

The [Thermal Cycle Monitor](examples/ThermalCycleMonitor/index.html) is a standalone interactive browser example of a chamber ramping from 30°C to 70°C, dwelling two hours, cooling to −30°C, dwelling two hours, and returning to 30°C. A second plot shows a 12 V supply's modeled current increasing **10% from 0.500 A to 0.550 A** at the hot temperature. Adjustable virtual time acceleration, pause, manual step, phase log and reset are included. The [C# DI example](examples/BenchDouble.ThermalCycleDemo/) injects a temperature-dependent load model and a shared simulation clock, exports UnitsNet-labeled Plotly JSON, and can run with `make demo-thermal`. See [full documentation](docs/THERMAL-CYCLE-MONITOR.md). These are simulated setpoints, not real chamber dynamics or physical safety controls; builds remain unverified.

## Sharing signals between instrument mocks

The virtual bench now uses [typed, DI-injected temperature signals](docs/VIRTUAL-BENCH-SIGNAL-SHARING.md) instead of global string-keyed state. `ThermalChamberModel` publishes a logical-time `ITemperatureProgram`; the new `FirstOrderThermalSensor` consumes it and models a 20-minute thermal time constant. The [thermal console demo](examples/BenchDouble.ThermalCycleDemo/) records sensor readings and exports a third Plotly JSON trace, and the [HTML demo](examples/ThermalCycleMonitor/index.html) overlays the lagging sensor curve on the chamber setpoint. This is a deterministic simulated physics connection, not an event bus or calibrated sensor. Build/visual validation pending.

## Typed virtual-bench wiring graph

The first [virtual wiring graph](docs/VIRTUAL-BENCH-WIRING-GRAPH.md) now supports typed `SignalPort<T>` definitions, source/transform/direct-connect operations, build-time cycle and missing-source detection, run isolation, and cached same-timestamp `VirtualBenchSnapshot` reads. The [thermal console demo](examples/BenchDouble.ThermalCycleDemo/) wires chamber temperature to a first-order sensor and combines sensor temperature with 12 V supply voltage to calculate a **sensor-driven DUT current** trace. Tests cover typed connections, snapshot caching, cycle rejection and stateful thermal coupling. This is a local simulation DAG, not a distributed signal bus, feedback solver, or safety system; build validation remains pending.

## Optional time-series storage

BenchDouble now defines [time-series storage contracts](src/BenchDouble/TimeSeries/TimeSeriesContracts.cs) for append-only measurement batches and bounded streaming queries, plus an in-memory adapter for tests. The [time-series architecture](docs/TIME-SERIES-STORAGE.md) proposes a durable local SQLite spool with optional PostgreSQL/TimescaleDB replication, preserving UnitsNet quantity/dimension metadata, UTC acquisition time, virtual elapsed time and per-stream sequence IDs. No database is required to run experiments. **SQLite, TimescaleDB, InfluxDB and QuestDB production adapters are not yet implemented**; the in-memory adapter is not durable and its quantity registry is pending.

## Durable SQLite time-series recorder

The optional [SQLite time-series package](src/BenchDouble.TimeSeries.Sqlite/) now implements `ITimeSeriesWriter`/`ITimeSeriesReader` using WAL, atomic append-only batches, idempotent replay, per-signal UnitsNet dimension/basis validation and bounded historical queries. The [thermal-cycle C# demo](examples/BenchDouble.ThermalCycleDemo/) records chamber setpoint, filtered sensor, supply current and sensor-driven DUT current to `thermal-demo-output/measurements.sqlite` and reads back the sensor history. Independent [SQLite tests](tests/BenchDouble.TimeSeries.Sqlite.Tests/) cover reopen durability, replay conflicts, dimension checks and query limits. See [time-series requirements](docs/TIME-SERIES-STORAGE.md). The code is committed and added to three-OS CI; **successful build/runtime verification is still pending**. Remote TimescaleDB replication, ingestion buffering and virtual-time query APIs remain future work.

The SQLite recorder also supports `SimulationTimeSeriesQuery` to retrieve readings by virtual elapsed time, independently of accelerated wall-clock pacing. It maintains a separate logical-time index and tests same-UTC/different-simulation-time samples.

## Requirements-driven development and testing (2026-10-15)

BenchDouble now has a **StrictDoc requirements baseline** with **122 stable IDs** (the 90 previously consolidated test-runner requirements plus 32 additional cross-subsystem requirements) in [requirements/benchdouble.sdoc](requirements/benchdouble.sdoc). [Traceability](requirements/traceability.csv) links each to implementation, test evidence status and GitHub work. [Reqnroll.xUnit](tests/BenchDouble.Acceptance.Tests/) provides executable Given–When–Then acceptance for the unattended monitor, and [Playwright Chromium tests](tests/BenchDouble.Browser.Tests/) check the browser SVG, alarm log and controls. [Pure xUnit tests](tests/BenchDouble.Tests/UnattendedCurrentMonitorTests.cs) verify the same C# alarm logic without a browser. **All are source-committed; successful execution has not been verified.**

### First-time setup

- macOS/Linux: `bash scripts/bootstrap.sh` (or `make setup`).
- Windows PowerShell: `pwsh -File scripts/bootstrap.ps1` (no Make required).
- Requirements: `python3 scripts/check_requirements.py`; StrictDoc export is part of bootstrap/CI.
- Tests: `make verify` on Unix; run individual `dotnet test` projects on Windows. `make test-browser` requires PowerShell and installs Playwright Chromium.
- Open [interactive HTML project overview](examples/ProjectOverview/index.html), [user manual](docs/USER-MANUAL.md), [software development plan](docs/SOFTWARE-DEVELOPMENT-PLAN.md), [feature audit](docs/FEATURE-AUDIT.md), and [testing guide](docs/TESTING-AND-TRACEABILITY.md).

The hardware-free .NET 8 target is intended for **Windows, macOS and Linux**; platform-specific VISA/IVI/GPIB drivers may have different support. The HTML demos currently execute JavaScript simulations, not C# WebAssembly; that enhancement is tracked in [issue #30](https://github.com/jeffadickson/BenchDouble/issues/30). Toolchain bootstrap and CI have been expanded, but package lockfiles, complete dependency pinning and green cross-platform evidence remain outstanding.

The [dependency inventory](docs/DEPENDENCIES.md) records pinned project package versions, cross-platform prerequisites, and outstanding lockfile/SBOM hardening work. The [testing and traceability guide](docs/TESTING-AND-TRACEABILITY.md) explains why C# xUnit, Reqnroll acceptance and Playwright browser graphics checks are complementary.

## Experiment-scoped inversion of control and instrument dashboard

The optional [experiment composition project](src/BenchDouble.Experiments/) provides named, typed simulated instrument registrations, constructor-injected mock behaviors and isolated per-run clocks using .NET 8 keyed dependency injection. Try `make demo-experiment` or `dotnet run --project examples/BenchDouble.ExperimentDemo/BenchDouble.ExperimentDemo.csproj`. See the [experiment tutorial](docs/EXPERIMENT-COMPOSITION.md) and [version control tutorial](docs/VERSION-CONTROL-TUTORIAL.md). Physical host discovery and leased resource resolution are **not yet implemented** in this API.

The existing loopback-only instrument host now serves a [read-only HTML instrument dashboard](src/BenchDouble.Host.Server/wwwroot/dashboard/index.html). Run `make demo-dashboard` (or `dotnet run --project src/BenchDouble.Host.Server`) and open **http://127.0.0.1:5278/dashboard/**. It shows registered local simulated instruments, reservation state, and bounded sanitized per-instrument activity logs. The [dashboard guide](docs/INSTRUMENT-DASHBOARD.md) documents current scope and security limitations. This is not yet a network-wide authenticated operations console.

The [requirements baseline](requirements/benchdouble.sdoc) now contains **138** stable IDs. New GitHub issues [#35–#39](https://github.com/jeffadickson/BenchDouble/issues) cover remote composition, dashboard aggregation, CI, versioning and evidence-based PyMeasure comparison. BenchDouble has interesting architectural differentiators but has **not yet demonstrated overall feature parity or superiority to PyMeasure** in validated driver coverage and production operation.

## AutoMock — rule-driven instrument behavior (2026-10-15)

The experiment composition root now supports `AutoMockLoad(...)` and `AutoMockLoadFromJson(...)`, injecting an immutable `RuleBasedLoadBehavior` decorator around an ordinary `ILoadBehavior`. Supported v1 rules match simulated elapsed-time windows and supply voltage, then return fixed current or scale baseline current; higher priority wins. Rule JSON is data-only and rejects unsupported actions/fields. `AutoMockRuleFiles.Sha256` records a deterministic rule-set digest, exposed on the experiment definition/run (not a complete package or run manifest). Run `make demo-automock` or `make demo-automock-json`; both use the unchanged thermal instrument classes. See [AutoMock architecture, JSON and tests](docs/AUTOMOCK-RULES.md). The implementation is **power-supply current only**; command-aware mocks and an optional Microsoft RulesEngine adapter are tracked in [issues #40–#41](https://github.com/jeffadickson/BenchDouble/issues). Full integrated experiment acceptance and cross-platform validation remain open in [issues #42–#43](https://github.com/jeffadickson/BenchDouble/issues). Source is committed; no passing build or CI evidence has yet been verified.

For experiments with multiple supplies, `AutoMockLoadFor(role, ...)` registers independent keyed rule sets; the [experiment tests](tests/BenchDouble.Experiments.Tests/ExperimentCompositionTests.cs) cover two different timed faults in one run. Per-role rule hashes are exposed for provenance. No instrument class changes are required when the composition-root factory injects its typed behavior.

## Generic unit-aware instrument mocks

The core now implements `ITypedMeasurementSource.MeasureAsync<TQuantity>()` and `ITypedConfigurableMeasurementSource.ConfigureAsync<TQuantity>()` with UnitsNet `IQuantity` constraints. A `TypedMockMeasurementsBuilder` registers only the physical quantities a simulated instrument actually supports, and `TypedMockMeter` rejects requests inconsistent with the selected mode. Run `make demo-typed-meter` for voltage (V) and resistance (Ω) examples. The experiment demo also registers `Meter1` with the chamber and supply. [Technical tutorial](docs/GENERIC-UNIT-AWARE-MOCKS.md) explains CLR generics versus Java erasure and why C# does not infer the generic type from the assignment destination. The future instrument dataset will generate the registrations; [issue #44](https://github.com/jeffadickson/BenchDouble/issues/44) tracks that work. Source and xUnit tests are committed, not yet execution-verified.

## Formal audit of the original 90 test-runner requirements

The [2026-10-15 evidence audit](docs/REQUIREMENTS-AUDIT-2026-10-15.md) reviewed all 90 TR requirements against the repository's actual source/tests: **45 partial-source**, **6 design-only**, and **39 with no direct implementation evidence**. The [90-row audit matrix](requirements/legacy-runner-audit.csv) records each requirement's evidence, remaining gap and GitHub issue; the [canonical traceability matrix](requirements/traceability.csv) has been reconciled accordingly. Across all **154** requirements, **none is marked Verified** because passing requirement-level test evidence has not been reviewed. GitHub Actions push runs inspected on 2026-10-15 failed on Windows, Ubuntu and macOS; the cause could not be established from available job logs. [Issues #45–#50](https://github.com/jeffadickson/BenchDouble/issues) track priority procedure, safety, security, evidence, UI and CI gaps. `scripts/check_requirements.py` now enforces audit coverage and consistency.

## Optional Parlot procedure language prototype

[BenchDouble.Procedures](src/BenchDouble.Procedures/) is a separate .NET 8 project that parses a small human-readable test procedure language with **Parlot 1.5.9**. It produces a typed AST, validates declared experiment instrument roles, UnitsNet dimensions, variable references and meter modes, then executes **only against registered simulated typed mocks** using the existing `ExperimentRunner`. Try `make demo-procedure` or `dotnet run --project examples/BenchDouble.ProcedureDemo`. The [procedure tutorial](docs/PROCEDURE-DSL.md) and [xUnit tests](tests/BenchDouble.Procedures.Tests/) cover resistance measurements, virtual-time waits and limit failures. This is not yet the shared production graphical editor, a physical hardware script engine or a round-trip-preserving procedure format. The editor integration and stable step IDs are tracked in [issue #51](https://github.com/jeffadickson/BenchDouble/issues/51). Build/test execution remains unverified while GitHub-hosted runners are unavailable.
