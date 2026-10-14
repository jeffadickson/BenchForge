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
