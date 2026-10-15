# BenchDouble roadmap

## Current prototype
- [x] Transport-independent SCPI session interface
- [x] Typed function-generator and DMM frontends
- [x] Stateful simulators (limited command subset)
- [x] Strict ordered replay and cross-instrument transcript assertions
- [x] One-shot timeout injection
- [x] xUnit/NSubstitute tests and three-OS GitHub Actions matrix

## Next priorities
1. Build green on CI and commit package lock files after restore.
2. Split instrument-class API from vendor-specific SCPI profiles; add capability/range checking.
3. Implement real VISA adapter behind optional platform-specific package, with loopback tests.
4. Expand model coverage from vendor programming guides, including status registers and error queues.
5. Add asynchronous sessions and virtual-clock timing, cancellation, retry and cleanup tests.
6. Add expression-based expectation DSL: cardinality, unordered groups and strictness modes.
7. Add bench wiring and a simple signal model coupling a generator and DMM.
8. Add programmable supply and oscilloscope simulators.
9. Add HTML interactive examples and detailed commented tutorials.
10. Publish NuGet packages only after API and cross-platform test coverage mature.

## Design rule
Never silently treat unsupported SCPI commands as successful. Differentiate mock calls, protocol simulation and hardware-level electrical models.


## Detailed requirements baseline

See [SOFTWARE-DESCRIPTION.md](SOFTWARE-DESCRIPTION.md) for subsystem interfaces, simulation fidelity, IVI compatibility levels, fault taxonomy, acceptance criteria and testing requirements.

### IVI-specific completion gate
- [x] Own IVI-inspired DMM and FGEN class interfaces
- [x] SCPI wrappers for these two classes
- [x] Basic adapter unit tests committed
- [ ] Compile and execute all tests on Windows/macOS/Linux
- [ ] Map to official IVI.NET assemblies with approved dependencies
- [ ] Compare real vendor driver and BenchDouble behavior for one common procedure
- [ ] Validate capabilities/ranges/triggering and session status semantics
- [ ] Publish documented supported instrument matrix

### Official IVI module progress
- [x] Isolate vendor-independent core from official IVI optional references
- [x] Reference official DMM, FGEN and driver-core packages
- [x] Add compile-time references to official legacy class interfaces
- [x] Introduce explicit DMM operation delegate bridge
- [ ] Confirm compile and restore of optional module on all supported platforms
- [ ] Implement direct class-method mapping with actual vendor driver test fixtures
- [ ] Verify official function-generator operations and repeated channel capabilities
- [ ] Confirm real driver loading, lifecycle and licensing

## Scope change: production instrument framework

The primary objective now includes **controlling real equipment**, not merely mocking it. See [package boundaries](PACKAGE-ARCHITECTURE.md). Introduce libraries only with working functionality and preserve current APIs during migration.

### New high-priority execution order
1. [ ] Make portable and official-IVI test projects green on all CI operating systems.
2. [ ] Introduce stable instrument/session abstractions with explicit resource and ownership semantics.
3. [ ] Implement real VISA provider plugin and loopback tests (hardware opt-in).
4. [ ] Implement directly mapped official IVI DMM and FGEN operations and optional hardware smoke tests.
5. [ ] Validate interchangeability: same compiled procedure with real, simulated and mocked backends.
6. [ ] Extract mocking and simulation into independently usable packages without breaking legacy APIs.
7. [ ] Expand instrument catalogue and virtual bench after production backend is validated.
8. [ ] Revisit product name and NuGet IDs later.

### Generic instruments and external interface study
- [x] Research IVI Foundation, PyMeasure, QCoDeS, pyvisa-sim and Scpi.NET design patterns
- [x] Add generic DMM, function-generator and oscilloscope class contracts
- [x] Add opt-in integration time (NPLC/aperture) and extension contracts
- [x] Add SCPI-backed generic DMM and FGEN adapters with tests
- [ ] Compile and run generic contract tests in CI
- [ ] Inspect official IVI class member signatures and build compatibility matrix
- [ ] Implement a model-specific Tektronix oscilloscope driver and waveform model
- [ ] Implement NPLC integration on a real supported DMM profile

### Declarative driver framework
- [x] Immutable parameter descriptors and PyMeasure-style factories
- [x] Typed string/double/bool SCPI conversion boundary (currently double and bool codecs)
- [x] Parameter-level min/max and custom validation
- [x] Hierarchical SCPI channel command binding
- [x] Add descriptor and channel tests (committed; not yet executed)
- [ ] Compile and execute across all supported systems
- [ ] Validate metadata against actual manufacturer command sets
- [ ] Provide parameter-level units, enums, numeric scaling and typed measurement results
- [ ] Migrate first concrete production-grade device driver
- [ ] Integrate declarative parameter introspection with IVI capabilities and mock expectation builder

### Structured logging / observability
- [x] Document logging requirements in software description and LOGGING.md
- [x] Add Microsoft.Extensions.Logging abstraction and structured SCPI session decorator
- [x] Redact command/response payloads by default and allow explicit controlled capture
- [x] Add regression tests for forwarding, redaction and exception propagation
- [ ] Validate CI restore/build/tests across supported operating systems
- [ ] Add logical instrument backend, resource and procedure-step correlation to all events
- [ ] Add optional OpenTelemetry ActivitySource and meters
- [ ] Create durable recording/replay artifact format independent of diagnostic logs
- [ ] Validate SCPI binary payload redaction and bounded capture

### PyMeasure-inspired driver porting (selective)
- [x] Inspect PyMeasure base instrument and property/validation source
- [x] Confirm MIT attribution requirements and document selective adaptation policy
- [x] Add typed vendor-token mapping and reusable discrete/range validators with tests
- [ ] Compile and run all tests across Windows, macOS and Linux
- [ ] Implement a first full Keysight 33500 instrument profile and SCPI command reference
- [ ] Validate true IVI DMM/FGEN method compatibility on vendor devices
- [ ] Port selected scope/DMM model capabilities with original attribution if source is reused
- [ ] Add full upstream source download as a versioned, reproducible development setup step if needed

### Async-first instrument APIs
- [x] Add cancellable ValueTask SCPI session contract
- [x] Add per-session serialization of query/write operations
- [x] Implement asynchronous DMM and function-generator subset drivers
- [x] Preserve existing synchronous simulator support through explicit compatibility adapter
- [x] Add async serialization and cancellation-before-dispatch tests
- [ ] Confirm compilation and CI execution
- [ ] Implement genuine native asynchronous VISA I/O and timeouts
- [ ] Implement physical abort and safe-state confirmation semantics
- [ ] Add IAsyncEnumerable measurement streaming with backpressure
- [ ] Add parallel-instrument experiment barriers and partial-order mock expectations

### Instrument arbitration
- [x] Implement in-process exclusive instrument reservations and cancellable leases
- [x] Verify intended resource contention and idempotent release in source tests
- [ ] Compile/run the reservation tests in CI
- [ ] Integrate compulsory leases into instrument factory and experiment context
- [ ] Canonicalize resource aliases; prevent bypass via duplicate sessions
- [ ] Add atomic multi-instrument reservations with deadlock-free acquisition
- [ ] Add cross-process/instrument-host locking and optional VISA resource locks
- [ ] Add lease telemetry, timeouts and physical safe-state cleanup policy

### Distributed instrument-host system
- [x] Define host-owned instrument reservations and remote API architecture
- [x] Document Pi/Linux ARM64, low-cost PC and network deployment considerations
- [ ] Implement standalone ASP.NET Core gRPC host and remote proxy for simulated DMM/FGEN
- [ ] Enforce authenticated exclusive server-side leases and fencing tokens
- [ ] Add bounded heartbeat, expiry and RecoveryRequired state handling
- [ ] Support safe reconnect, durable operation identity and unknown-outcome reporting
- [ ] Provide Linux ARM64/systemd deployment package
- [ ] Add optional native VISA provider with real USB/GPIB/TCPIP resource enumeration
- [ ] Add cross-host coordinator without claiming premature global atomicity
- [ ] Verify tests for contention, lost network, host restart and safe cleanup

### Live logs at host and experiment client
- [x] Document unified server-side logging schema and host/client visibility
- [ ] Implement bounded nonblocking log fan-out plus retained local spool
- [ ] Add authenticated gRPC SubscribeEvents with cursor resume and filters
- [ ] Build host dashboard and headless CLI tail utility
- [ ] Display live logs alongside client experiment steps and notebook cells
- [ ] Propagate OpenTelemetry trace and instrument/run correlation IDs
- [ ] Validate reconnect, slow subscribers, gap signals and redaction permissions

### Host package scaffolding milestone
- [x] Create separate host contracts, server and client projects
- [x] Implement simulator-backed local discovery and exclusive reservation demo
- [x] Implement leased SCPI query demo endpoint and async HTTP client
- [x] Add host lease-contention and authorization-by-lease unit-test source
- [ ] Confirm host project builds and tests pass on CI
- [ ] Replace demo leases with authenticated, fenced expiring leases and safe recovery
- [ ] Add non-loopback TLS/gRPC and instrument permissions only after security is tested
- [ ] Implement structured host/client log streaming and UI
- [ ] Integrate physical VISA adapter under controlled host ownership

### Protocol evolution, portable drivers and shared access
- [x] Specify versioned protobuf contract and avoid serialized .NET object graphs
- [x] Add v1 host proto schema and host access-policy DTOs
- [x] Define signed content-addressed driver package approval/isolation design
- [x] Define exclusive controller and shared observer/host-sampled policies
- [ ] Generate gRPC client/server from v1 proto and implement feature negotiation
- [ ] Implement server-enforced policy checks, observer streaming and sampled sensor fan-out
- [ ] Add host-owned driver package cache, trusted signatures and process isolation
- [ ] Add old/new client and host compatibility CI matrix, schema-breaking checks
- [ ] Validate crash recovery, unknown physical state and resource fencing across versions

### UnitsNet integration
- [x] Adopt stable UnitsNet in library and tests
- [x] Implement typed voltage results and function-generator frequency/amplitude conversion
- [x] Preserve existing double-valued APIs
- [x] Add conversion tests
- [ ] Compile/run new tests in cross-platform CI
- [ ] Implement supported current/resistance measurement profiles
- [ ] Add unit-aware parameter definitions, range checking and result storage
- [ ] Define protobuf quantity wire DTOs and reject dimensional mismatch
- [ ] Model RMS/Vpp/peak and uncertainty independently of UnitsNet quantities

### Generated instrument drivers
- [x] Define IVI-aligned, PyMeasure-inspired descriptor-to-code architecture
- [x] Add sample versioned Keysight instrument JSON profile
- [ ] Implement JSON schema and semantic validator with diagnostics
- [ ] Implement Roslyn incremental generator with compile-time typed UnitsNet APIs
- [ ] Generate test fixtures and XML docs; integrate with simulator expectations
- [ ] Validate Keysight and Tektronix profile SCPI against vendor manuals
- [ ] Convert selected PyMeasure device profiles with attribution and parity tests

### Explicit source generation and programmable mocks
- [x] Replace compiler-time generator direction with standalone .NET generator and Makefile entry point
- [x] Emit editor-visible ordinary .g.cs files from versioned JSON numeric SCPI definitions
- [x] Implement sine, seeded uniform random and composable simulation signal behavior types
- [x] Add simulation signal tests
- [ ] Verify generator compiles and generated drivers pass unit tests
- [ ] Commit generated outputs and enforce drift check in CI
- [ ] Add schema-driven mock expressions and scripted response behavior
- [ ] Add stateful/mock timeout/error functions and controlled deterministic clock
- [ ] Import PyMeasure driver profiles with attribution and validate manual command parity

### Mock lifecycle and simulation clocks
- [x] Specify initialization and per-measurement mock behavior definitions
- [x] Specify run-scoped shared simulation time and speed scaling
- [x] Specify wall-time versus simulation-time delay and safety separation
- [ ] Implement I/SimulationClock abstraction with real, accelerated, paused and manual time
- [ ] Add validated .mock.json format to standalone generator and produce editable .g.cs files
- [ ] Bind seeded sine/noise mock behavior to simulation clocks and stateful DMM samples
- [ ] Add per-operation latency, warmup, acquisition and jitter using UnitsNet durations
- [ ] Verify deterministic multi-instrument concurrency, cancellation, replay and acceleration

## Integrated test-lab runner acceptance baseline

Use [TEST-LAB-RUNNER-REQUIREMENTS.md](TEST-LAB-RUNNER-REQUIREMENTS.md) as the indexed TR-001–TR-090 source for implementation issues, tests and milestone acceptance. Prioritize (1) green build and durable async runner, (2) structured procedure editing, (3) controlled redlines and signoff, (4) secure host streaming and leases, (5) multi-host demonstration. Maintain requirement-to-test traceability; do not mark an item complete solely because an interface or document was committed.

### Source-grounded unified instrument operation architecture
- [x] Document local physical, local simulated, remote physical and remote simulated paths
- [x] Audit 25 actual PyMeasure operation definitions with file/line provenance
- [ ] Implement typed operation registry and generic dispatch for selected DMM/FGEN operations
- [ ] Integrate mock state transitions with operation definitions (mode-aware measurement)
- [ ] Implement typed versioned remote proxy without client-side vendor SCPI duplication
- [ ] Verify source-to-C# operation parity and real-device behavior where authorized

### 25 instrument port inventory and Keithley 236
- [x] Correct 25 PyMeasure properties to 25 distinct catalog instrument driver entries
- [x] Document which instrument source files were actually inspected
- [x] Implement first Keithley 236 legacy IEEE-488 DC source/measure subset and resistive DUT mock
- [x] Add typed UnitsNet protocol and mock tests (source committed, not executed)
- [ ] Build and execute 236 tests in CI
- [ ] Complete 236 sweep, trigger, status, range, sense, error and output-data format commands
- [ ] Add actual GPIB TALK/EOI transport and hardware acceptance tests
- [ ] Validate physical safety/interlocks and fault recovery before production enable
- [ ] Inspect and port remaining 25 catalog instrument drivers individually with attribution

### TPL Dataflow evaluation
- [x] Review ActionBlock, bounded capacity, transforms, fan-out semantics and instrument actor architecture
- [x] Document boundaries vs leases, procedure DAG, durable storage and safe abort
- [ ] Prototype internal bounded instrument actor for Keithley 236 and DMM
- [ ] Test concurrent clients, cancellation, faulted block, fencing and command completion
- [ ] Benchmark ActionBlock against Channel<T> and existing SemaphoreSlim design
- [ ] Adopt only if it simplifies the implementation and passes safety/concurrency tests

### Scientific plotting and UnitsNet visualization
- [x] Separate unit-aware plot conversion from instrument and experiment core
- [x] Add ScottPlot optional PNG renderer and Plotly.js-compatible JSON exporter
- [x] Add conversion, axis labeling and Plotly JSON tests
- [ ] Verify optional ScottPlot package restore/build and render PNG in CI
- [ ] Integrate timestamp, quality, uncertainty and measurement basis in plot traces
- [ ] Add dimension-aware multi-trace validation and mixed-axis support
- [ ] Build browser live Plotly.js charts with bounded streaming and cursor/limit overlays
- [ ] Add waveform/downsampling policies preserving limit excursions

### Standalone UnitsNet plotting companion
- [x] Remove BenchDouble core dependency from typed plotting adapter
- [x] Add independent test project referencing only plotting library
- [ ] Verify standalone project and tests build on supported operating systems
- [ ] Split Plotly JSON renderer into optional adapter if API stability warrants
- [ ] Complete unit/dimension compatibility, affine temperature, basis and uncertainty tests
- [ ] Agree on public naming/license and submit upstream proposal after validation

### Dependency injection and programmable mock behavior
- [x] Introduce container-independent ILoadBehavior constructor injection and idealized MockPowerSupply
- [x] Implement ResistiveLoadBehavior and timed-overcurrent decorator with virtual clock
- [x] Add runnable .NET DI demo, normal/fault scenarios, and make demo targets
- [x] Add xUnit tests for DI swapping, run-scope isolation, time-triggered faults and safe simulated output-off
- [ ] Verify builds and tests on Windows, macOS and Linux
- [ ] Implement configurable IV curves, realistic compliance/voltage droop and per-model output limits
- [ ] Add explicit InitializeAsync/ResetAsync behavior lifecycle for stateful plugins
- [ ] Add Roslyn DLL/PDB build, test and approved isolated-worker deployment pipeline
- [ ] Integrate DI-backed mocks with remote host factory, run logging and versioned behavior hashes

### Unattended Operations Monitor
- [x] Add self-contained HTML demo with live current plot, fault injection, overcurrent alarm and event log
- [x] Add simulated email/SMS notification sink to C# DI example
- [x] Document alert/outbox, delivery and physical safety boundaries
- [ ] Integrate browser UI with live UnitsNet measurement stream and host event IDs
- [ ] Add persistent alarm rules, acknowledgment, deduplication and escalation
- [ ] Add opt-in real notification providers with secure credentials, retries and delivery receipts
- [ ] Validate hardware safe-state independent of UI and notifications

### Thermal cycle virtual bench demonstration
- [x] Implement typed 30→70→−30→30°C ramp/dwell model with seven-hour default program
- [x] Inject temperature-dependent supply load with +10% hot current response
- [x] Add standalone interactive two-plot HTML monitor with accelerated/manual time
- [x] Add DI C# demo exporting unit-labeled Plotly JSON and xUnit phase-boundary tests
- [ ] Verify browser visual behavior and run .NET demo/tests in cross-platform CI
- [ ] Add chamber thermal lag, actual sensor stabilization/dwell qualification and independent DUT temperature
- [ ] Connect browser monitor to actual runner/host streaming, alerts and durable measurements
