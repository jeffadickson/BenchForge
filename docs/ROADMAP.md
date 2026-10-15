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
