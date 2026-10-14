# Package Boundaries and Architectural Requirements
**Decision:** retain the BenchDouble repository and existing namespaces temporarily. The product scope is **production instrument control with integrated mocking and simulation**. A future branding decision must not dictate present package dependencies.

## 1. Architectural principles
1. Real instrument control is a primary deliverable, not a side effect of mocking.
2. A test procedure uses the same typed instrument contract regardless of whether its backend is real, simulated or expectation-driven.
3. VISA is a transport interface; SCPI is a device protocol; IVI supplies instrument-class contracts. Do not conflate them.
4. Strict mocks and simulation are opt-in backends, not production instrument-driver responsibilities.
5. Physical capabilities, channel counts, range limits, resource ownership and error handling must be explicit.
6. The shared core must be portable, hardware-free, and independent of vendor SDKs.
7. Dependency direction is acyclic; infrastructure packages may depend on contracts, but application code and contracts must not depend on any particular mock or hardware adapter.
8. Safety-critical hardware actions must not silently fall back to simulation. Backend selection requires explicit configuration, with actual mode reflected in diagnostics.

## 2. Target package boundaries

| Logical package (provisional) | Role | May depend on | Must not depend on |
|---|---|---|---|
| `BenchDouble.Abstractions` | instrument/session contracts, units, capability descriptors, errors, lifecycle | BCL | VISA vendor SDKs, mocking frameworks |
| `BenchDouble.Visa` | resource manager, sessions, timeouts, binary blocks, GPIB/USB/TCPIP/serial; pluggable providers | Abstractions | mocks, instrument-specific commands |
| `BenchDouble.Scpi` | SCPI command/query formatting, parsing, status/errors, dialect support | Abstractions | device model implementations |
| `BenchDouble.Ivi` | instrument-class API + mapping rules/capabilities | Abstractions | NI vendor transport, simulation |
| `BenchDouble.Ivi.Official` | bridge to real IVI Foundation assemblies | Abstractions, Ivi, official IVI packages | mocking engine |
| `BenchDouble.Instruments` | typed DMM, FGEN, PSU, scope, RF generator device profiles | Abstractions, Scpi, Ivi | simulation-only packages |
| `BenchDouble.Simulation` | stateful instrument models, deterministic clock, fault injection | Abstractions, optional instrument profiles | native VISA drivers |
| `BenchDouble.Mocking` | EasyMock-like ordered/unordered expectation DSL, capture/replay, assertion reports | Abstractions | VISA or vendor drivers |
| `BenchDouble.VirtualBench` | virtual wiring, DUT models, coordinated sources/meters | Abstractions, Simulation | physical transport implementation |
| `BenchDouble.Testing` | opt-in xUnit/NSubstitute helpers and fixtures | Abstractions, Mocking, Simulation | production runtime packages |
| `BenchDouble.Hosting` | dependency injection, config, resource registry, explicit backend selection | Abstractions and selected adapters | static global state |
| `BenchDouble.Observability` | logs, diagnostics, OpenTelemetry spans and redaction | Abstractions | instrument vendor DLLs |

Names are **logical targets**, not claims of published NuGet packages. Existing `src/BenchDouble` stays in place until compilation, characterization tests, and staged migrations demonstrate equivalence. Do not create empty assemblies solely for the architecture diagram.

## 3. Dependency graph
```text
TestForge / standalone test software / other applications
                    |
                  Hosting (composition root only)
                    |
        public Instrument Abstractions + IVI class API
                    |
         +----------+---------------+
         |                          |
      Instruments                IVI.Official
         |                          |
        Scpi                  vendor IVI driver
         |                          |
         +----- session abstraction-+
                        |
          +-------------+------------+
          |             |            |
        VISA         Simulation     Mocking
      physical       stateful      exact scripts
                        |
                   VirtualBench
```
All test-double facilities implement or wrap the same boundaries as hardware, but do not need to emulate a vendor SCPI transcript when verifying class-level semantics.

## 4. Public API contracts

### 4.1 Application-facing instruments
Interfaces for IDigitalMultimeter, IFunctionGenerator, IPowerSupply, IOscilloscope, IRfSignalGenerator should express physical quantities with declared units; channel indexes/capability checks; sync/async semantics; explicit triggering; status and identity; range errors; and disposal. Instrument functionality must not depend on whether backend is hardware, simulation or mock.

### 4.2 Sessions
An instrument session owns resource lifetime and has a single-writer concurrency policy. Define asynchronous and binary I/O separately from a compatibility adapter for today's synchronous `IScpiSession`. Error types must discriminate VISA transport, SCPI, capability/validation, IVI driver, timeout, and cancellation failures.

### 4.3 Simulation and mocking
Simulation answers based on maintained device state. Mocking checks interaction expectations, including strict order, count, optional calls and cross-object transcripts. A mock response must never imply physically generated power; recorded transcripts should carry a provenance flag (hardware/simulation/mock).

### 4.4 Configuration
`InstrumentRegistry` should resolve a logical name such as `DMM1` to a backend type and resource. Default to an explicit non-hardware-safe development profile; require an affirmative hardware profile to actuate real equipment. Secrets and resource identifiers are never embedded in test scripts.

## 5. Interchangeability acceptance test
One compiled test procedure:
1. obtains a DMM and generator using the typed interfaces;
2. configures a safe waveform and meter function;
3. enables generator, acquires a reading, and disables output in a finally block;
4. executes unchanged against (a) a deterministic simulator, (b) a strict interaction mock, and (c) an authorized physical vendor driver;
5. produces a backend-tagged transcript and asserts behavior, status, and limits.

Physical integration is opt-in; CI must pass hardware-free, and the hardware-backed result is a separately documented gate. Strict SCPI equality is **not** an IVI interchangeability requirement because legal driver command sequences vary.

## 6. Migration and API compatibility
- Stage A: document boundaries and add architecture checks; no breaking relocation.
- Stage B: introduce Abstractions with explicit API versioning; adapters keep legacy `BenchDouble` types compiling.
- Stage C: move SCPI and IVI code under separated source projects, maintain shims and tests.
- Stage D: deliver real VISA + vendor driver support and use it in a hardware-backed smoke suite.
- Stage E: isolate Mocking/Simulation, implement strict typed expectations and optional virtual bench.
- Stage F: review naming and package IDs *after* empirical compatibility and release planning.

Each stage requires a green build, no dependency cycles, documented API compatibility, and an accurate feature matrix. A package split does not itself indicate feature completion.

## 7. Nonfunctional requirements and release gates
- Windows/macOS/Linux portable builds and CI; native driver support by confirmed provider/platform matrix.
- Fixed/specified dependencies, reproducible restore, no implicit hardware on build or test.
- Deterministic simulations, rich diagnostics, no global mutable mock timelines.
- Cancellation, retry, session cleanup and unknown-state safety reporting.
- No unsupported SCPI silently accepted; no fabricated calibration claims.
- Observability: structured command traces, status, correlation IDs, with sensitive-data redaction.
- License/redistribution due diligence for all IVI/VISA SDKs.
- NuGet packaging and semver only after validated contracts.
- Real hardware test matrix is opt-in, instrument-model/version pinned, and explicitly separate from normal CI.

## 8. Implementation status at this decision
**Existing:** single portable core with basic SCPI sessions, DMM/FGEN drivers and simulators, strict replay, shared transcript; IVI-inspired class wrappers; optional official-IVI typed bridges; xUnit source and cross-OS workflow.

**Unverified:** .NET build and CI results in this work session; official IVI adapter binding against a real vendor driver.

**Not implemented:** physical VISA backend, complete IVI interchangeability, async/binary sessions, power-supply/scope production drivers, virtual bench wiring, advanced expectation DSL, individual package projects listed above.

## 9. Rationale for deferring the name
The name BenchDouble communicates mocks, but the planned product controls real instruments. The repository is kept intact while package architecture is established. Future parent brand and naming migration should preserve source compatibility and avoid leaving the mock package conflated with hardware drivers.
