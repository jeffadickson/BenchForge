# Requirements coverage — 2026-10-15 revision 4

**223 requirements; 0 Verified.** Complete row inventory: [full-audit.csv](full-audit.csv). Canonical tracker: [traceability.csv](traceability.csv).

| Status | Count |
|---|---:|
| DesignOnly | 7 |
| NoImplementationEvidence | 39 |
| PartialSourceUnverified | 59 |
| Planned | 49 |
| SourceCommittedUnverified | 69 |

The original 90 TR findings are retained in [legacy-runner-audit.csv](legacy-runner-audit.csv). New lifecycle, heartbeat, command, resilience, identification, ownership and change-control requirements have explicit acceptance conditions.

Local evidence: 12 Instrument Definer tests and two draft validations passed in the prior increment; this is partial Python evidence, not complete requirement verification. The SDK is now downloaded and host projects compile locally; the standard VSTest runner crashes before test execution. An in-process test-method smoke harness executes 19 host/configuration cases successfully, but is not a standard test-runner result. No hardware validation is claimed. The current audit tests only tracker consistency and failure detection.

Local-first routing increment: BD-CFG-001..007 and issues #59/#60. Host projects and new configuration tests compiled locally using MSBuild; the standard VSTest runner crashed before executing tests. Final client/tests were also compiled with Roslyn and 19 host/configuration test-method cases passed in-process (not VSTest). StrictDoc 0.30.2 installed and exported successfully. No Verified promotion.

API documentation/artifact increment: BD-DOC-003..005 and BD-ART-001..005 (#61/#62). Documentation pipeline and generated comments implemented with an explicit legacy backlog. That increment specified the binary storage design; see the local backend update below. No Verified promotion. See docs/API-DOCUMENTATION-AUDIT-2026-10-15.md for local execution scope.

Local capture backend: BD-ART-001..004 now PartialSourceUnverified (125 source-linked requirements total). Twelve in-process xUnit Fact methods and an independent process-exit recovery probe passed. Standard VSTest remains blocked; remote storage, backup/retention, power-loss and performance acceptance remain open. See docs/ARTIFACT-STORAGE-VALIDATION-2026-10-15.md. No Verified promotion; total remains 220.

Instrument Definer increment BD-DEF-009..011: review gate, conservative inherited/dynamic member coverage, and manifest-driven batch extraction with tests. Source and test files are committed but these **new tests have not been executed**. Existing local Python and .NET evidence from earlier increments remains separate; none of these additions is marked Verified. See [Instrument Definer instructions](../tools/InstrumentDefiner/README.md).

Captured-read validation increment (2026-10-16): BD-SIM-001 now explicitly covers incomplete acquisition records, correlation IDs, waveform/meter start-time agreement and fault preservation. Counts remain 223 requirements, 70 SourceCommittedUnverified and 0 Verified. Regression source is in StatefulMockEngineTests; execution results are recorded separately below.

Local validation for this increment: .NET SDK 10.0.402 compiled the core test project and standard VSTest passed all 175 core tests (the focused stateful/virtual selection passed 26). Invoked the SDK CLI assembly explicitly because global.json permits only the 10.0.1xx feature band: `/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll test tests/BenchDouble.Tests/BenchDouble.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore`. Requirements gate and all 9 tracker tests passed. `make docs-check` with the same explicit SDK command passed: 0 new gaps, 0 generated gaps; 1,134 legacy gaps remain. All 10 documentation tests passed. This is local Linux core-suite evidence, not full-repository, cross-platform or hardware acceptance; no Verified promotion.

Simulation startup cleanup increment (2026-10-16), BD-EXP-005 / issue #35: added eager async role resolution with cleanup on failure/cancellation and preservation of startup plus disposal errors. Status is PartialSourceUnverified because durable provenance and physical recovery are incomplete. Current totals: 223 requirements, 59 PartialSourceUnverified, 69 SourceCommittedUnverified, 0 Verified. Standard VSTest with local .NET SDK 10.0.402 passed all 22 experiment tests, including five new startup regressions. Command: `/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll test tests/BenchDouble.Experiments.Tests/BenchDouble.Experiments.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false`.

Startup increment gates: requirement consistency and all 9 tracker tests passed; `make docs-check` passed with 0 new documentation gaps and 0 generated gaps, and all 10 documentation tests passed. Existing documentation debt remains 1,130 gaps; this increment does not claim repository-wide documentation completion.

Concurrent cleanup increment (2026-10-16), BD-EXP-005 / issue #35: session disposal now publishes a shared completion before invoking resource callbacks. Concurrent and later callers observe the same result or failure, and new clock/instrument access is rejected after disposal starts. Deterministic gated tests exercise 32 callers for both success and failure. All 24 experiment tests passed using `/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll test tests/BenchDouble.Experiments.Tests/BenchDouble.Experiments.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore`. Requirement gate and all 9 tracker tests passed; `make docs-check` and all 10 documentation tests passed (0 new / 0 generated gaps, 1,129 legacy gaps). Counts remain 223 requirements, 59 PartialSourceUnverified, 69 SourceCommittedUnverified and 0 Verified. Startup regression coverage remains in ExperimentStartupTests.cs alongside the new ExperimentDisposalTests.cs. Physical recovery and durable provenance remain incomplete.
