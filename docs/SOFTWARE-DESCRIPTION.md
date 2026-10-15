# BenchDouble — Software Design Description
**Status:** engineering prototype / living architecture baseline  
**Updated:** 2026-10-14  
**Scope:** instrument-class mocks, SCPI/VISA abstraction, protocol verification, virtual test benches, and IVI interoperability.

> This document distinguishes **implemented**, **partially implemented**, and **planned** behavior. The present repository is not IVI-certified, not a hardware-equivalent simulator, and must not be relied upon as a safety interlock or calibrated measurement standard.

## 1. Executive overview

BenchDouble is an open-source-oriented .NET test-double framework for automated laboratory and aerospace/flight-hardware test software. Its objective is to let engineers develop and test measurement automation without requiring every developer and CI job to access GPIB, USB, Ethernet or serial instruments. The design supports four separable levels of verification:

1. **Application tests:** assert typed instrument-class method calls and their arguments.
2. **Protocol tests:** assert precise SCPI write/query transcripts, including ordering across sessions.
3. **Instrument-behavior tests:** run stateful virtual equipment with controlled replies, state changes and injected failures.
4. **System tests:** ultimately connect simulated instruments through a virtual device under test (DUT), timed stimulus, and measurement models.

None of these alone substitutes for calibration, hardware acceptance tests or electrical safety verification.

### 1.1 Distinctive goal
BenchDouble combines EasyMock-inspired deterministic strict transcripts with IVI-aware interchangeable instrument abstractions, explicit capability profiles, and predictable fault injection. It should coexist with xUnit, NSubstitute, vendor IVI drivers, NI/Keysight VISA implementations and other existing test tools rather than competing with them.

### 1.2 Terminology
- **Mock:** scripted responses plus assertions about calls made.
- **Stub:** responds predictably without necessarily validating call history.
- **Fake / simulator:** working, simplified model of device behavior and internal state.
- **Spy / transcript:** records communication for later inspection.
- **VISA:** session/resource API for instrument communication; not an instrument-class model.
- **SCPI:** textual command/query syntax used by many instruments, with vendor variations.
- **IVI:** standardized instrument-driver architecture and instrument classes; conformance is not implied by naming conventions.
- **DUT:** device under test; distinct from the simulated test equipment connected to it.

## 2. Current implementation inventory

| Component | Status | Source |
|---|---|---|
| Synchronous SCPI session (`Write`, `Query`) | Implemented | `src/BenchDouble/Instruments.cs` |
| Typed function-generator facade | Implemented, sine only | `FunctionGenerator` |
| Typed DMM facade | Implemented, DC voltage only | `DigitalMultimeter` |
| Strict exact-order replay | Implemented | `StrictSession` |
| Single common timeline for several sessions | Implemented | `SharedTranscript` |
| Stateful generator | Partial | `GeneratorSimulator` |
| Stateful DMM | Partial | `MultimeterSimulator` |
| One-shot transport timeout | Implemented | `StatefulScpiDevice.FailNextOperation` |
| IVI-inspired, BenchDouble-owned instrument-class contracts | Partial | `Ivi/IviAbstractions.cs` |
| DMM and generator class-to-SCPI adapters | Partial | `Ivi/ScpiIviAdapters.cs` |
| Official IVI.NET assembly compatibility | Not implemented | Planned optional package |
| Native VISA transports | Not implemented | Planned optional package |
| Virtual clock, scheduler and virtual DUT | Not implemented | Planned |
| Dynamic electrical models and uncertainty | Not implemented | Planned |
| Cross-platform CI workflow | Committed, passing status unverified | `.github/workflows/test.yml` |

The instrument profile names use example Keysight model identifiers, **not** claims of complete command, status, triggering, timing, frequency, accuracy, or range fidelity.

## 3. System boundaries and architectural topology

```text
Test application, test procedure, TestForge or standalone xUnit test
   |
   +---- [Typed instrument-class contract]
   |           |
   |           +---- BenchDouble IVI-inspired adapter -- SCPI driver
   |           |                                   |
   |           |                              IScpiSession
   |           |                                   |
   |           |              +--------------------+------------------+
   |           |              |                    |                  |
   |           |       Stateful simulator    Strict replay      Future VISA
   |           |                                                  adapter
   |           |
   |           +---- Future official IVI.NET vendor-driver adapter
   |
   +---- [Future virtual bench orchestration]
                 | virtual connections, clocks, fault rules
                 +---- DUT signal / response model
```

### 3.1 Important seam: typed IVI operations vs wire-level SCPI
`IIviDigitalMultimeter.Measure()` is an **application-level** invocation; `MEAS:VOLT:DC?` is a **wire-level** SCPI query. A driver's legitimate internal status checks, caching, and retries can change the wire transcript while leaving class-level behavior equivalent. BenchDouble must make the chosen assertion boundary explicit. Do not assume a vendor IVI driver emits exactly the same SCPI commands as our reference driver.

### 3.2 Dependency direction
Core contracts should remain independent of NI, Keysight, COM, Windows registry, native GPIB libraries, and vendor DLLs. An optional adapter package may reference official IVI.NET or VISA assemblies. This ensures that developer tests without hardware can run on Windows, macOS and Linux.

## 4. IVI integration strategy

### 4.1 Goals
A single test procedure should configure and read compatible function generators, DMMs, oscilloscopes, DC power supplies and RF generators through stable class-level operations, with either a hardware driver or a BenchDouble substitute injected at composition time.

### 4.2 Compatibility levels
- **Level 0 — IVI-inspired:** own interfaces modeled on IVI classes. **This is what exists now.**
- **Level 1 — adapter compatibility:** a wrapper exposes common operations around genuine IVI.NET class assemblies, and maps standard error, status and capability semantics. Planned.
- **Level 2 — interchangeability demonstration:** the *same compiled test procedure* runs against a supported real driver and a simulator with no procedure edits. Planned.
- **Level 3 — evaluated IVI conformance:** only after assessing official specifications, testing requirements, redistribution rights and any certification process. Not claimed.

### 4.3 IVI generation nuance
Classic IVI class drivers and IVI Generation 2026 lightweight driver recommendations are related but not identical. Implementing a newer core driver alone is insufficient proof of complete legacy IVI class interchangeability. Track specific versions of the IviDmm/IviFgen specification and official API assemblies in an interoperability matrix.

### 4.4 Instrument class API requirements
Contracts should eventually support:
- identity, initialization, reset, close/disposal, options and capability introspection;
- standard measurement/channel configuration and ranges;
- triggering and acquisition models, status/error/reporting;
- explicit optional functionality via feature flags or queried capabilities;
- simulator selection and configurable behavior;
- range checking with explicit units and consistent diagnostics;
- session ownership, thread safety and asynchronous cancellation policy;
- vendor-specific extensions **without** polluting the portable common interface.

Current `IIviFunctionGenerator` supports sine configuration and output state; current `IIviDigitalMultimeter` supports DC voltage with default range and resolution. Other operations must explicitly reject unsupported requests.

## 5. VISA / SCPI transport specification

### 5.1 Current transport
`IScpiSession.Write(string)` sends a command; `Query(string)` returns a response. It is deliberately synchronous and transports only UTF-8-like strings conceptually; no native VISA I/O yet.

### 5.2 Required production transport
`IInstrumentSession` should introduce:
- asynchronous write/read/query with `CancellationToken`;
- binary block data and terminator configuration;
- resource identification (GPIB/USB/TCPIP/serial);
- connection/open/close lifecycle and explicit exclusive/session ownership;
- timeouts, end-of-message handling, line endings, service request/status operations;
- consistent mapping of VISA status/error codes to typed exceptions;
- secure logging with redaction and bounded binary capture;
- injectable clock, retry policy, and transport disturbance profile.

Write/query operations on a single session must be serialized or reject concurrent use unless the transport explicitly supports it. Cancellation semantics must document whether an in-flight operation can leave the device in an unknown state.

### 5.3 SCPI parser requirements
The production parser must handle case-insensitive mnemonics, abbreviated/long forms, common commands (`*IDN?`, `*RST`, `*OPC?`, `*CLS`), command-tree context, delimiters, quoted strings, numeric units and binary blocks as relevant to the particular model. A profile must declare supported subsets. Unknown commands must never be silently accepted.

## 6. Instrument models

### 6.1 Function generator
Current profile: two channels, sine function selection, frequency, voltage amplitude (treated as Vpp by typed facade), enable/disable and identity response. Planned: offset, duty cycle, load, burst, sweep, trigger, sync, arbitrary waveform upload, supported waveforms, limits and error queue.

### 6.2 Digital multimeter
Current profile: DC voltage configuration, injected measurement and identity response. Planned: AC voltage/current, DC current, 2-wire/4-wire resistance, NPLC, aperture, auto/manual range, triggering, sampling, buffering, null/relative, overrange/error reporting, source impedance and uncertainty.

### 6.3 Future instruments
Programmable DC supply (OVP/OCP/current limit, on/off interlock); oscilloscope (channels, coupling, bandwidth, trigger, waveform acquisition); RF source (power/frequency/modulation/ALC); power meter; counter/timer; switching matrix; temperature chamber. Each model requires specification-backed capability limits and test vectors.

## 7. Expectation engine

### 7.1 Present semantics
`StrictSession` compares every write/query against the head of a FIFO. On mismatch, it throws **before consuming** the expectation. `VerifyComplete` detects unconsumed interactions. `SharedTranscript` enforces an ordered cross-instrument timeline.

### 7.2 Planned expectation DSL
- strict whole-transcript, ordered-subsequence, and unordered-set strategies;
- `Exactly(n)`, `AtLeast(n)`, `Between(min,max)`, `Never()`;
- argument and response matchers (numeric tolerances, regex, predicates);
- ordered and unordered nested regions;
- conditional branches, optional operations, bounded retries;
- promises/barriers/partial-order graphs for concurrent instruments;
- prescribed return values, exceptions, delays and timeouts;
- fail-fast mode and collect-and-verify mode;
- diffs with current state, pending operations, source call site and timestamps.

**Invariant:** an unsuccessful unexpected call cannot advance a strict script. End-of-test verification must detect missing calls even when no mismatch was thrown during execution.

### 7.3 Cross-session concurrency
The existing shared FIFO is **not thread safe**. Until synchronization or an event-sourced verifier is implemented, treat sessions as single-threaded and don't claim deterministic parallel test execution.

## 8. Simulation, virtual wiring and deterministic time

The virtual bench will register resources (e.g., `VISA::GEN1`, `VISA::DMM1`), attach instrument models, connect ports through networks and DUT blocks, and schedule events on a virtual clock. A sine generator feeding a gain/low-pass DUT model could yield the DMM's expected RMS value or an oscilloscope trace, while independent faults introduce connection breaks, stale measurements or saturation.

The physics fidelity level must be declared per model:
- **Command-level:** state and query response only.
- **Behavioral:** functional transfer model, ranges, loading and noise.
- **Calibrated/metrological:** requires rigorous uncertainty analysis and validation; outside present scope.

Virtual time should allow deterministic timeout, race, cancellation and recovery tests without sleeping for physical durations.

## 9. Error model and safety

Faults fall into:
1. transport: timeout, truncated reply, lost connection, framing error;
2. SCPI/protocol: unknown command, syntax, status register, error queue;
3. instrument: output fault, range exceedance, overload, interlock, self-test fail;
4. DUT/measurement: abnormal voltage, dropout, noise, drift, missing signal.

A mocked `OUTP ON` merely proves software issued that command; it does not prove physical output reached its target. On cleanup failure, a test must report **unknown hardware state**, not imply the output is safe. Safety-critical tests must separately validate physical inhibit and emergency shutdown logic.

## 10. Developer workflows and examples

### 10.1 Stateful generator
```csharp
var model = new GeneratorSimulator();
var source = new FunctionGenerator(model);
source.SetSine(1000, 1.0);
source.Output(true);
Assert.True(model.Outputs[1]);
```

### 10.2 Strict DMM protocol
```csharp
var script = new StrictSession(new[]
{
    new ExpectedExchange("CONF:VOLT:DC"),
    new ExpectedExchange("MEAS:VOLT:DC?", true, "5.01")
});
var meter = new DigitalMultimeter(script);
meter.ConfigureDcVoltage();
Assert.Equal(5.01, meter.MeasureDcVoltage(), 2);
script.VerifyComplete();
```

### 10.3 IVI-inspired abstraction
```csharp
IIviDigitalMultimeter meter = new ScpiDigitalMultimeterAdapter(
    new MultimeterSimulator { InjectedVoltage = 4.999 },
    simulationEnabled: true);
meter.ConfigureMeasurement(IviMeasurementFunction.DcVoltage);
double volts = meter.Measure();
```
Imports: `using BenchDouble;` and `using BenchDouble.Ivi;`.

### 10.4 Mock type selection
Use NSubstitute for ordinary business collaborators; a strict transcript for exact protocol expectations; a stateful simulator for behavior and faults; Testcontainers for real auxiliary services; and physical bench testing for true electrical compatibility.

## 11. Quality strategy

### 11.1 Testing pyramid
- Core parser, ranges and behavior unit tests;
- strict script and IVI-adapter contract tests;
- multiple-instrument scenario tests with virtual time;
- integration tests with actual VISA backend and optional external simulator;
- opt-in hardware smoke tests with named resource IDs;
- interoperability tests against approved IVI implementations;
- negative tests for unsupported features, resource loss and cleanup failures.

### 11.2 CI and platforms
The repository includes a three-OS GitHub Actions matrix targeting .NET 8. **No verified passing CI status has been established in this document.** All portable simulation and expectation tests must pass without vendor drivers; native integrations require separately labeled, platform-aware jobs.

### 11.3 Release criteria
Before alpha: clean CI, pinned/reproducible dependencies, coherent package boundaries, API examples that compile, deterministic tests, instrument-profile coverage table, exact licensing, and published limitations. Before hardware-ready beta: native backend validation, driver/version matrix, robust cancellation/reconnection, fault coverage, and trace diagnostics.

## 12. Packaging and repository layout

Current:
```text
src/BenchDouble/                  single .NET 8 library
  Instruments.cs                  SCPI session, drivers, models, verifier
  Ivi/                             own IVI-inspired interfaces and adapters
tests/BenchDouble.Tests/          xUnit + NSubstitute
docs/                              roadmap and IVI integration plan
.github/workflows/test.yml         CI matrix
global.json                        .NET SDK selection
```

Proposed when justified by tested dependencies:
`BenchDouble.Core`, `BenchDouble.Scpi`, `BenchDouble.Ivi`, `BenchDouble.Visa`, `BenchDouble.Instruments`, `BenchDouble.VirtualBench`, `BenchDouble.Testing`. Avoid splitting prematurely merely to mirror a diagram.

## 13. Non-functional requirements

- **Portability:** core tests run without hardware or native VISA installation.
- **Determinism:** same configuration/seed produces same trace and result.
- **Performance:** thousands of simulated transactions per test without network/physical delays; benchmark before setting latency SLAs.
- **Observability:** trace resource, operation, duration, response category, expected rule and fault; later OpenTelemetry support.
- **Security:** restrict real resources explicitly, redact sensitive tokens and don't treat untrusted SCPI transcripts as executable code.
- **Maintainability:** documented instrument commands with manuals, SDK version matrix and source-linked test fixtures.
- **Extensibility:** vendor-specific behavior in profiles/adapters, not in generic IVI contracts.

## 14. Known design gaps and risks

1. Native official IVI.NET mapping and legal redistribution conditions are unresolved.
2. Current model supports only a tiny command subset; vendor behavior is illustrative.
3. Session interfaces are synchronous; shared timeline lacks concurrency safety.
4. Simulator does not implement accurate status registers, errors, timing or hardware ranges.
5. Present class interface is IVI-inspired, not official IVI conformance.
6. Simulation mode flag is descriptive; it does not change physical device connection.
7. No virtual DUT or hardware transport is wired into runtime.
8. Automatic CI success, NuGet publication and instrument certification are not established.

## 15. Prioritized implementation plan

**P0 (engineering baseline):** green builds/tests on all three platforms; reset/identity/error semantics; accurate docs; capability validation; non-destructive release packaging.  
**P1 (IVI interoperability):** integrate official API (isolated optional package), adapt DMM and FGEN, test same procedure against mocks and hardware-backed IVI drivers.  
**P2 (expectation DSL):** scoped script, cardinality, nested order policies, thread-safe cross-session diagnostics and programmable failures.  
**P3 (instrument depth):** PSU, oscilloscope, robust DMM/FGEN profiles and official command-source traceability.  
**P4 (virtual bench):** DUT transfer blocks, wiring, virtual time, noise and metrology-aware simulations.  
**P5 (operationalization):** package versioning, documentation site, tutorial playground, hardware validation matrix and adoption guide.

## 16. Success criteria

A developer can clone BenchDouble onto Windows/macOS/Linux, execute hardware-free tests, swap a real instrument adapter and a simulator without rewriting application business logic, verify safety-relevant ordering, produce diagnosable evidence from a failed test, and explicitly see where simulation fidelity ends. These are *target acceptance criteria*, not current achievements.

## 17. Official IVI integration update — 2026-10-14

An optional `src/BenchDouble.Ivi.Official` assembly now references the **real** IVI Foundation DMM/Fgen shared component packages (`IviFoundation.Driver.Dmm` and `IviFoundation.Driver.Fgen` 2.1.0) and the Generation 2026 DriverCore assembly (1.0.1). The code checks actual official class interface types and exposes an application-supplied delegate bridge from official `Ivi.Dmm.IIviDmm` drivers into the BenchDouble DMM abstraction. This is a *typed interoperability boundary*, not a completed mapping or conformant IVI driver. See [OFFICIAL-IVI.md](OFFICIAL-IVI.md).

The package is isolated from `BenchDouble.Core` functionality so portable tests need no native driver. Verification remains pending: current session lacks the .NET SDK, and no full vendor driver has been exercised. Generation 2026 Core does not by itself guarantee IVI 2014 class interface semantics.

## 18. Official DMM/FGEN typed bridges and contract tests

The optional official integration package now includes both `OfficialDmmBridge` and `OfficialFgenBridge`. Each holds a concrete official IVI class-interface typed reference and delegates operations to caller-provided mappings. The function generator additionally validates channel, finite frequency and finite amplitude before calling through. An official-IVI-specific xUnit/NSubstitute test project exercises the actual class-interface types without requiring a vendor instrument.

**Status classification:** *typed adapter plumbing and tests written*, not vendor interoperability proven. CI runs these tests on Windows/macOS/Linux; results require separate verification. The operational mappings are deliberately injected delegates rather than assumed methods, since actual class method signatures and supported extensions need instrument-specific validation.

**Next acceptance test:** write one test procedure using `IIviDigitalMultimeter` and `IIviFunctionGenerator`; run it with simulator-backed SCPI adapters and vendor driver bridges, then compare behavior and capability negotiation rather than demanding identical SCPI transcripts.

## 19. Product scope and package-boundary decision

The principal product purpose is **real instrument control** (VISA, SCPI, IVI, typed instrument drivers and device management), with integrated **mocking**, **stateful simulation**, and eventually **virtual bench** functions. The old name is retained temporarily. [PACKAGE-ARCHITECTURE.md](PACKAGE-ARCHITECTURE.md) is normative for target package ownership and dependency rules, production-vs-test boundaries, interchangeable backend requirements, migration compatibility and release gates. Logical package names are proposed and should not be mistaken for existing assemblies. The currently implemented features and verification gaps elsewhere in this document remain unchanged.

## 20. Generic instrument interface implementation and IVI source review

Initial generic instrument types and corresponding DMM/FGEN adapters are now committed in `InstrumentContracts.cs` and `InstrumentAdapters.cs`, with regression tests in `InstrumentContractTests.cs`. They intentionally separate core instrument-class operations from optional capabilities such as measurement integration, plus model-specific extensions. The oscilloscope is contract-only at this milestone. For source comparison, vendor-specific field rules, PyMeasure/QCoDeS patterns and remaining standards mapping, see [INSTRUMENT-INTERFACES-RESEARCH.md](INSTRUMENT-INTERFACES-RESEARCH.md). This does not imply official IVI conformance; compile/test results for new code remain unverified.

## 21. PyMeasure/IVI contract audit

The current interfaces were reviewed against PyMeasure's instrument adapter, declarative properties, channel objects, Keithley driver and oscilloscope base-driver patterns, together with IVI's class capability model. We will retain transport separation and optional feature interfaces, but replace passive feature flags with typed validated parameter descriptors and introduce metadata-rich measurement/waveform results. The audit also found that immediate measurement queries must not be treated as equivalent to fetch/read after initiation; raw SCPI invalidates cached state; scope channels and physical units need richer descriptions. See [INSTRUMENT-INTERFACES-RESEARCH.md](INSTRUMENT-INTERFACES-RESEARCH.md) and the new tracking issue. No code or binary compatibility claims have changed.

## 22. Declarative instrument parameters and design patterns

The `BenchDouble.Declarative` namespace implements PyMeasure-inspired parameter factories `Control`, `Measurement`, `Setting`. The immutable definition describes SCPI get/set templates, codec, optional validator, and display/capability metadata; binding it to a session/channel produces read/write operations with explicit read-only/write-only rejection. `DoubleCodec` and `BooleanCodec` provide basic invariant conversion. `ScpiChannelGroup` and `ScpiChannel` are initial hierarchical binding helpers. Tests cover multi-channel command generation, readback, boundary rejection and access rules. Integration into mature vendor-specific drivers remains outstanding; code is committed but compile/test status is unverified.

See [DESIGN-PATTERNS.md](DESIGN-PATTERNS.md) for Abstract Factory, Adapter, Strategy, Composite, Decorator, Command, State, Builder, Specification and Observer rationale. These patterns define options for engineering tradeoffs, not mandates to add abstractions before they earn their complexity.

## 23. Design-pattern rationale (normative guidance)

BenchDouble deliberately uses the following patterns, matched to specific problems rather than applied indiscriminately:

| Pattern | Concrete responsibility | Status |
|---|---|---|
| Abstract Factory / dependency injection | Resolve configured instrument class and explicitly chosen physical, simulated or mock backend | Planned |
| Adapter | Bridge typed interfaces to SCPI or official IVI vendor drivers | Implemented in initial DMM/FGEN adapters; full IVI still pending |
| Strategy | Replace session transport, validation and `IScpiCodec<T>` conversion | Sessions and codecs implemented; native VISA pending |
| Composite | Instrument → channel group → channel → parameter with hierarchical addressing | Initial `ScpiChannelGroup` and `ScpiChannel` implemented |
| Decorator | Cross-cutting logging, tracing and eventual scoped retries/recording | `LoggingScpiSession` now implemented |
| Command / Interpreter | Model SCPI exchanges and expected replay behavior | `ExpectedExchange` and `StrictSession` implemented |
| State | Capture configuration, enabling, measurement and future triggering/acquisition lifecycle | Simplified simulator state implemented |
| Builder | Author strict ordered/unordered expectations, counts and injections | Advanced DSL planned |
| Specification | Validate device/model-specific ranges and options | Initial metadata bounds and predicates implemented |
| Observer | Stream state changes and diagnostics to OpenTelemetry or other subscribers | Planned; structured ILogger diagnostics started |

The PyMeasure-inspired parameter factories (`ScpiParameter.Control`, `Measurement`, `Setting`) are declarative descriptors that encapsulate SCPI commands, typed conversion, metadata, validators and hierarchical binding. They are not magical C# properties or a complete instrument driver generator. Instrument features beyond common IVI behavior belong in narrow optional capability interfaces and vendor-specific extensions, not the universal `IInstrument` contract.

Full pattern rationale, tradeoffs and anti-patterns: [DESIGN-PATTERNS.md](DESIGN-PATTERNS.md).

## 24. Structured logging and observability requirements

**Decision:** integrate `Microsoft.Extensions.Logging` for portable, DI-friendly structured events and leave providers/exporters to host applications. Logging must be usable for physical VISA, virtual instrument, and strict mock sessions. Do not implement a separate proprietary logging framework.

**Initial implementation:** `BenchDouble.Observability.LoggingScpiSession` decorates the synchronous `IScpiSession`. It forwards `Write`/`Query`, emits structured start/success/failure records, measures elapsed time with `Stopwatch`, preserves exceptions, and redacts command/response payloads unless independently opted in. Application-supplied redaction is supported. The wrapper does not retry, swallow exceptions, or claim instrument state transitions succeeded.

**Mandatory future structured fields:** instrument logical identifier; backend provenance (real/simulator/mock); resource/session ID; procedure/test run and step correlation; operation type; monotonic duration; status/error category; driver/instrument model; response metadata. A log of a command being issued must never be interpreted as proof of physical effect.

**Security:** no credentials or arbitrary proprietary reply payloads by default; avoid unbounded data dumps, sanitize SCPI payloads and binary blocks; separate audit-grade transcripts from routine application logs.

**OpenTelemetry integration:** optional `ActivitySource` spans, counters and meters, scope correlation and exporters chosen by application. Do not force a vendor-specific exporter into instrument interfaces.

**Testing requirements:** no behavior change with logging enabled, redaction by default, explicit controlled opt-in payloads, correct failure propagation, and no automatic replay/retry of potentially non-idempotent commands.

See [LOGGING.md](LOGGING.md) for code example, event taxonomy, current limitations and future milestones. The source and tests are committed; a successful .NET test execution has not been established.

## 25. PyMeasure source audit and selective port policy

**Architectural decision:** IVI establishes instrument-class interoperability, while PyMeasure's actual `instrument.py`, `common_base.py` and `validators.py` implementations inform developer ergonomics, properties, SCPI mappings and channel hierarchies. A full Python framework translation is explicitly outside the core scope; experiments, GUI, plotters and PyVISA internals belong to their own layers or runtimes. Initial independent C# implementations include the existing immutable SCPI parameter factories plus `ParameterValidators` (range/discrete/composed validation) and `MappedCodec<T>` (bidirectional typed token mapping), with accompanying tests. See [PYMEASURE-PORT-STRATEGY.md](PYMEASURE-PORT-STRATEGY.md) for detailed traceability and license treatment. We inspected source via a connected GitHub service; full local clone was blocked by DNS. The new C# additions have not yet been compiled/tested in this environment.

## 26. Asynchronous instrument-control baseline

The `BenchDouble.Async` namespace introduces `IAsyncScpiSession` with cancellable `ValueTask` write/query operations; `SerializedAsyncScpiSession` serializes operations sharing one SCPI connection; `AsyncDigitalMultimeter` and `AsyncFunctionGenerator` expose cancellable typed operations for the currently supported profiles. `SynchronousScpiAdapter` preserves existing synchronous mocks and simulated devices for compatibility, but it **blocks** while calling the underlying synchronous session and must not be represented as native asynchronous VISA I/O. The serialized adapter owns only its semaphore; disposal requires no in-flight operations. Cancellation requests do **not** establish that actual hardware aborted an acquisition or reached a safe state. End-to-end cleanup, abort confirmation, asynchronous native VISA backend, instrument reservations, and stream acquisition are future work. The committed tests cover legacy compatibility, cancellation before dispatch, and serialization. Compilation and test execution remain unverified.

**Requirements:** one physical session must not receive concurrent query/write sequences without explicit provider support; preserve query atomicity; use `Task<T>` or `ValueTask<T>` for eventual results and `IAsyncEnumerable<T>` for eventual streaming acquisitions; distinguish cancellation of a waiting operation from cancellation of already-issued hardware commands. Concurrent multi-instrument experiments shall use explicit synchronization barriers and partial-order expectations, not only a strict total timeline. Avoid fire-and-forget hardware operations, silent retries or unbounded cleanup after a fault.
