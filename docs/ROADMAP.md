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
