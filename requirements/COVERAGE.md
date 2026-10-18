# Requirements coverage — 2026-10-16 discovery setup revision

**228 requirements; 0 Verified.** Complete row inventory: [full-audit.csv](full-audit.csv). Canonical tracker: [traceability.csv](traceability.csv).

IVI architecture review (2026-10-16): [configuration/driver integration assessment](../docs/IVI-CONFIGURATION-REVIEW.md) maps TR-037 (#46) and BD-CFG-008..012 (#69) to the official configuration model and existing bridges. It records proposed read-only import, driver binding, controlled activation and hardware acceptance work. Documentation only; no runtime implementation, new verification claim or status/count change.

| Status | Count |
|---|---:|
| DesignOnly | 7 |
| NoImplementationEvidence | 39 |
| PartialSourceUnverified | 74 |
| Planned | 39 |
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

Discovery-assisted setup requirements (2026-10-16): BD-CFG-008..012 and issue #69 record optional serials, discovery, human-assisted controller binding, explicit alternate-route associations and guided save/repair. BD-CFG-004 acceptance now explicitly includes cross-interface contention. Five DesignOnly additions bring totals to 228 requirements, 12 DesignOnly and 0 Verified; other status counts are unchanged. No discovery UI, provider implementation or physical acceptance is claimed.

Validation of the setup requirements increment: `python3 scripts/check_requirements.py` passed for all 228 requirements; all 9 requirements tracker tests passed. No runtime behavior changed or hardware tests were performed.

Discovery setup implementation (2026-10-16): BD-CFG-008..012 move from DesignOnly to PartialSourceUnverified. Current totals: 228 requirements, 7 DesignOnly, 64 PartialSourceUnverified, 69 SourceCommittedUnverified, 49 Planned, 39 NoImplementationEvidence and 0 Verified. Implemented guided `/setup/` page, strict host configuration, optional identities, two-bus addressing, explicit alternate routes, stable-ID resolution/operator reconfirmation, atomic revision-checked save, offline drafts and role reservation through the existing registry. Optional installed-VISA enumeration uses an explicit transport/one-host scope; no physical driver activation is claimed.

Validation: local .NET 10 Release host suite and DOM/HTTP integration executed. `scripts/check_bench_setup_dom.cjs` passed against the real host API, covering discover/configure/validate/save/reload and conflict/error paths. The Playwright script is committed but Chromium launch was blocked by the environment's local socket restriction, so screenshots/rendering were not verified. Native VISA translation is tested with injected catalogs; no vendor runtime or attached hardware was available. See `docs/BENCH-SETUP-USER-GUIDE.md` for the boundary and repeatable commands. No requirements were promoted to Verified.

Final setup increment checks: 55 host tests passed under .NET SDK 10.0.402 using `dotnet test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore` (SDK assembly invoked explicitly in this environment). Requirements gate and all 9 tracker tests passed. `make docs-check` passed with 0 new and 0 generated documentation gaps, with all 10 documentation tests passing. The 1,129 legacy gaps remain unchanged. DOM checks used happy-dom 21.0.3; Chromium 141 / Playwright 1.56.0 rendering remains unexecuted because browser startup could not create its required local socket.

IVI catalog increment (2026-10-16), TR-037 / #46: optional official metadata reader plus read-only setup preview implemented in `IviConfigurationCatalog.cs` with `IviConfigurationCatalogTests.cs`. ConfigServer 2.1.0 requires Windows registry support; Linux reports provider unavailability explicitly. Standard VSTest passed 11 official integration tests, with 1 actual Windows loader test explicitly skipped, and all 55 host tests passed. DOM/HTTP checks passed for upload bounds, malformed XML, provider diagnostics, unchanged saved setup and supplied-catalog rendering; the successful catalog UI used a stub response, not the Windows loader. Existing primary bridge traceability remains; catalog tests supplement it. No physical hardware, vendor-driver activation or Windows file-loader acceptance is claimed. Counts/statuses unchanged (228 requirements; 0 Verified).

Reviewed IVI binding/logging increment (2026-10-16): BD-CFG-012 / #69 and TR-076 / #49 now cover schema-two offline driver binding provenance and structured setup events. `IviSetupCatalogService` creates detached reviewed drafts; binding imports never activate hardware. `IviSetupBindingTests` supplements the canonical primary test links. All 59 host tests passed locally, including injected-catalog import, two buses, explicit alternate routes, stale/conflicting mappings, persistence, unchanged active leases and log payload exclusion. DOM/HTTP checks passed; successful catalog/import responses were stubbed in the UI test, while persistence and refusal of activation used the real host. All 9 requirements tests and 10 documentation tests passed; documentation audit reports 0 new and 0 generated gaps (1,129 legacy gaps). Windows catalog loading and physical activation remain unverified. Counts/statuses remain unchanged: 228 requirements, 0 Verified.

Controlled IVI DMM activation increment (2026-10-16), TR-037 / #46 and BD-LIFE-001/005 / #55: host-code registered official DMM bridges now open, configure, measure, close and recover under the existing canonical lease authority. Exact reviewed bindings and current GPIB controller confirmation are required. Initialization, operation and cleanup failures retain ownership for explicit provider recovery; synchronous calls retain ownership until completion despite caller cancellation. Structured events 7450–7456 exclude settings, measurements and vendor exception messages. BD-LIFE-001/005 are PartialSourceUnverified; totals are 228 requirements, 66 PartialSourceUnverified, 47 Planned, 7 DesignOnly, 39 NoImplementationEvidence, 69 SourceCommittedUnverified and 0 Verified (136 source-linked).

Validation: all 66 host tests passed with the explicit .NET SDK 10.0.402 CLI assembly, Release configuration, `-m:1 -p:UseSharedCompilation=false -nr:false`. The DOM/HTTP setup script passed, including stubbed activation/recovery UI responses; service tests exercise real canonical ownership using official-interface test doubles. Requirements consistency and all 9 tracker tests passed. `make docs-check` and all 10 documentation tests passed with no new or generated documentation gaps. No vendor factory is bundled, no physical instrument was tested, and Windows catalog loading remains unverified. In-memory quarantine, browser token retention, synchronous I/O deadlines and provider-specific physical recovery remain limited as documented in the setup guide. No Verified promotion.

Ten local IVI hardening iterations (2026-10-16): TR-037 / #46, TR-076 / #49, BD-LIFE-001/005 / #55 and BD-CFG-012 / #69 retain their canonical trace links and partial status. Added atomic validated registration, explicit measurement configuration, correlated operation diagnostics, preflight reasons, optional expected identity checks, cached status, tab session continuity, serialized UI commands and cancellable queued dispatch. All 80 host tests passed locally using SDK 10.0.402 Release; the DOM/HTTP workflow passed, including stubbed successful lifecycle responses and a real stale-lease status rejection. All 9 tracker tests and 10 documentation tests passed; documentation audit reports 0 new and 0 generated gaps (1,129 legacy gaps). No vendor hardware, Windows loader or rendered-browser acceptance was run. StrictDoc export was not run because its executable is unavailable in this environment. Counts remain 228 requirements, 136 source-linked unverified, 66 PartialSourceUnverified, 47 Planned, 7 DesignOnly, 39 NoImplementationEvidence, 69 SourceCommittedUnverified and 0 Verified. See `docs/BENCH-SETUP-ITERATIONS-2026-10-16.md` for the ten iterations and remaining limits.

Second ten-iteration local batch (2026-10-16): TR-037 / #46, TR-076 / #49, BD-LIFE-005 / #55 and BD-CFG-012 / #69 now include shared descriptor ownership, declared provider functions, strict IVI runtime requests, atomic reading provenance, isolated diagnostic sink failures, cached failure phase, queued-recovery cancellation and explicit private lease resumption. All 93 host tests passed locally under .NET SDK 10.0.402 Release. DOM/HTTP checks passed for function selection, units/provenance, manual resumption without activation, stale capabilities and missing-function HTTP rejection; successful driver responses remain stubbed in the DOM harness. All 9 tracker tests and 10 documentation tests passed. Documentation audit: 0 new and 0 generated gaps, 1,129 legacy gaps. No hardware, Windows loader or rendered-browser qualification; no Verified promotion. Counts remain 228 total, 136 source-linked unverified, 66 PartialSourceUnverified, 47 Planned, 7 DesignOnly, 39 NoImplementationEvidence, 69 SourceCommittedUnverified and 0 Verified. See `docs/BENCH-SETUP-ITERATIONS-2026-10-16-B.md`.

Third twenty-iteration batch (completed 2026-10-17 UTC): BD-LIFE-006 / #55 moves from Planned to PartialSourceUnverified for the local ownership journal and explicit startup quarantine/reconciliation. Existing TR-076 / #49, BD-LIFE-001/005 / #55 and BD-CFG-012 / #69 acceptance and audit fingerprints are updated. Totals: 228 requirements, 137 source-linked unverified, 67 PartialSourceUnverified, 46 Planned, 7 DesignOnly, 39 NoImplementationEvidence, 69 SourceCommittedUnverified and 0 Verified. All 110 host tests, the forced-process-exit probe, DOM/HTTP workflow, 9 tracker tests and 10 documentation tests passed locally. Documentation audit: 1,421 declarations/modules, 1,128 legacy gaps, zero new/generated gaps; no baseline exceptions added. Successful UI driver/reconciliation responses are stubbed; the process probe uses a managed reservation without vendor I/O. No hardware, Windows, power-loss or rendered-browser qualification. See `docs/BENCH-SETUP-ITERATIONS-2026-10-16-C.md` for the twenty iterations, commands and limits. StrictDoc export remains unavailable. No Verified promotion.


Shutdown and heartbeat batches (2026-10-16 local / 2026-10-17 UTC): 25 shutdown iterations followed by 20 heartbeat iterations, published together. BD-LIFE-004 / #55 and BD-HB-001..006 / #56 move from Planned to PartialSourceUnverified; TR-076 / #49, BD-LIFE-005 / #55 and BD-CFG-012 / #69 acceptance and fingerprints are updated. Totals: 228 requirements, 144 source-linked unverified, 74 PartialSourceUnverified, 39 Planned, 69 SourceCommittedUnverified, 39 NoImplementationEvidence, 7 DesignOnly, 0 Verified. Shutdown checkpoint: 133 host tests passed. Combined final: 150 host tests passed; real-time heartbeat, SIGTERM and forced-exit probes passed without vendor I/O; DOM/HTTP workflow passed with successful physical-driver responses stubbed. See iteration reports D/E for exact scope, commands, final documentation/tracker gates and remaining acceptance.




Local iteration 01 (2026-10-17), BD-CFG-006: Enable local .NET tests and release failed ledger locks. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 02 (2026-10-17), BD-HB-002: Validate scope heartbeat routes and periods. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 03 (2026-10-17), BD-HB-002: Make scope heartbeat start and stop race safe. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 04 (2026-10-17), BD-HB-003: Stop scope renewal on terminal host rejections. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 05 (2026-10-17), BD-HB-002: Isolate every scope heartbeat observer. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 06 (2026-10-17), BD-HB-002: Bound each scope heartbeat HTTP renewal. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 07 (2026-10-17), TR-076: Isolate SCPI logging sink failures. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 08 (2026-10-17), TR-076: Fail closed when SCPI payload redactors fail. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 09 (2026-10-17), TR-076: Exclude vendor exception payloads from SCPI logs. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 10 (2026-10-17), BD-CFG-003: Reject control characters in instrument routing IDs. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Iteration 10 regression repair, BD-HB-002: a sub-second renewal period no longer imposes a sub-second HTTP cancellation budget. A delayed-success test covers the one-second minimum, while stalled-transport coverage retains a finite deadline. No status/count change.

Local iteration 10 (2026-10-17), BD-CFG-003: Reject routing control characters and repair short-period heartbeat timeouts. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 11 (2026-10-17), BD-CFG-003: Normalize overflowing routing schema errors. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 12 (2026-10-17), BD-CFG-002: Keep routing independent of diagnostic sinks. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 12 (2026-10-17), BD-CFG-002: Isolate routing logs and stabilize late-cleanup regression. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 13 (2026-10-17), BD-CFG-008: Validate DNS labels before VISA enumeration. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 13 (2026-10-17), BD-CFG-008: Validate discovery hostnames and isolate deadline-sensitive tests. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 14 (2026-10-17), BD-CFG-008: Reject incomplete VISA endpoint descriptors. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 15 (2026-10-17), BD-CFG-008: Bound every VISA discovery catalog. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 16 (2026-10-17), BD-EXP-001: Freeze experiment role metadata defensively. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 17 (2026-10-17), BD-EXP-003: Reject null instrument factories at composition. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 18 (2026-10-17), BD-EXP-003: Reject ambiguous experiment dependency registrations. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 19 (2026-10-17), BD-SIM-001: Validate AutoMock fallback currents and voltage bounds. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Local iteration 19 (2026-10-17), BD-SIM-001: Validate AutoMock fallback currents and voltage bounds. Counts and statuses unchanged; execution details in docs/LOCAL-ITERATIONS-2026-10-17.md.

Batch iteration 01/30 (2026-10-17), TR-076: Isolate stateful mock diagnostic sinks. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 02/30 (2026-10-17), TR-076: Redact stateful mock fault payloads. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 03/30 (2026-10-17), BD-SIM-001: Honor cancellation after asynchronous mock models. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 04/30 (2026-10-17), BD-SIM-001: Track consumed mock faults in state revisions. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 05/30 (2026-10-17), BD-SIM-001: Add explicit queued mock fault clearing. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 06/30 (2026-10-17), BD-SIM-001: Keep absent model removal idempotent. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 07/30 (2026-10-17), BD-SIM-001: Avoid revisions for identical mock overrides. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 08/30 (2026-10-17), BD-SIM-001: Bound pending mock fault queues. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 09/30 (2026-10-17), BD-SIM-001: Prevent overflow in finite virtual ramps. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 10/30 (2026-10-17), BD-SIM-001: Freeze imported capability mode metadata. Canonical statuses unchanged; runtime verification deferred to checkpoint 10 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 11/30 (2026-10-17), BD-SIM-001: Require complete imported identifiers. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 12/30 (2026-10-17), BD-SIM-001: Bound imported definition size and depth. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 13/30 (2026-10-17), BD-SIM-001: Reject duplicate imported JSON fields. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 14/30 (2026-10-17), BD-SIM-001: Reject ambiguous imported mode commands. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 15/30 (2026-10-17), BD-SIM-001: Reject overlapping imported query bindings. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 16/30 (2026-10-17), BD-SIM-001: Reject repeated capability mode references. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 17/30 (2026-10-17), BD-SIM-001: Validate imported mock default types. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 18/30 (2026-10-17), BD-SIM-001: Reject duplicate AutoMock JSON keys. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 19/30 (2026-10-17), BD-SIM-001: Require explicit AutoMock action values. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 20/30 (2026-10-17), BD-SIM-001: Require symbolic AutoMock action names. Canonical statuses unchanged; runtime verification deferred to checkpoint 20 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 21/30 (2026-10-17), BD-SIM-001: Bound programmatic AutoMock rule sets. Canonical statuses unchanged; runtime verification deferred to checkpoint 30 under the requested cadence. See docs/LOCAL-BATCH-30.md.

Batch iteration 22/30 (2026-10-17), BD-SIM-001: Validate null rule entries consistently. Canonical statuses unchanged; runtime verification deferred to checkpoint 30 under the requested cadence. See docs/LOCAL-BATCH-30.md.
