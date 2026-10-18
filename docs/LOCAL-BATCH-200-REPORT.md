# BenchForge: 200 local iterations, 2026-10-18

This report accompanies iteration 200. Each iteration has its own local commit; the single push follows the final checkpoint and commit. The detailed ledger is [LOCAL-BATCH-200-2026-10-18.md](LOCAL-BATCH-200-2026-10-18.md).

## Changes

- Iterations 1–50: SCPI ambiguity rejection before mutation, bounded metadata, detached immutable reading/history snapshots, cancellation-aware reset and history clear; 40 new boundary/concurrency cases.
- Iterations 51–100: project discovery prunes artifacts and links; optional trees, browser exclusion, literal filter arguments, child timeouts, cancellation and structured execution reports; 40 new workflow cases. The initial recorder-file/report-directory collision was fixed with a compiled C# recording executable; original failure evidence is retained.
- Iterations 101–150: detached mock fault counts/model-kind diagnostics, clock-free fault inspection, bulk clears, one shared synchronous/asynchronous reset transaction, clock-cancellation preservation, and matching imported-mock facade controls; 41 new cases including facade integration.
- Iterations 151–200: strict scalar heartbeat settings, clock frequency validation, fail-closed backwards-time detection, coherent state/age sampling, validated renewal time, and grant validation before mutation; 40 heartbeat cases. Final integration adds three linked-directory/cycle workflow cases.

There are 40 implementation/refactoring iterations and 160 regression iterations, producing 164 named regression cases. These counts are not claims of 200 separate features.

## Validation

Tests ran only within checkpoints 50, 100, 150 and 200, including the recorder correction/retry at checkpoint 100. No intervening test runs.

- Checkpoint 50:
  - `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj -c Release --filter FullyQualifiedName~ScpiBatch200Tests|FullyQualifiedName~ApplicationBatch300Tests|FullyQualifiedName~StatefulMockEngineTests|FullyQualifiedName~StatefulMockSequenceTests|FullyQualifiedName~TypedMockMeasurementsTests --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false`: passed; 110 application cases; 492.994 seconds including startup/builds.
  - `powershell -NoProfile -File build.ps1 -Command checks`: passed; 9 unittest cases; 57.355 seconds including startup/builds.
- Checkpoint 100:
  - `powershell -NoProfile -File build.ps1 -Command identity`: passed; 11.663 seconds including startup/builds.
  - `python tests/check_ports/developer_tasks.py`: passed; 59.032 seconds including startup/builds.
  - `python -m unittest discover -s tests/check_ports -p test_developer_batch200.py -v`: passed; 40 unittest cases; 60.126 seconds including startup/builds.
  - `powershell -NoProfile -File tests/check_ports/architecture.ps1`: passed; 8.258 seconds including startup/builds.
  - `powershell -NoProfile -File tests/check_ports/runtime_requests.ps1`: passed; 4.497 seconds including startup/builds.
  - `powershell -NoProfile -File build.ps1 -Command checks`: passed; 9 unittest cases; 29.168 seconds including startup/builds.
  - `powershell -NoProfile -File build.ps1 -Command plan -Task test -TestFilter FullyQualifiedName~Clock|FullyQualifiedName~Mock`: passed; 13.836 seconds including startup/builds.
- Checkpoint 150:
  - `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj -c Release --filter FullyQualifiedName~StatefulBatch200Tests|FullyQualifiedName~StatefulMockEngineTests|FullyQualifiedName~StatefulMockSequenceTests|FullyQualifiedName~ApplicationDiagnosticsTests|FullyQualifiedName~ScpiBatch200Tests --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false`: passed; 144 application cases; 140.341 seconds including startup/builds.
  - `powershell -NoProfile -File build.ps1 -Command checks`: passed; 9 unittest cases; 37.338 seconds including startup/builds.
- Checkpoint 200:
  - `dotnet test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj -c Release --filter FullyQualifiedName~HeartbeatBatch200Tests|FullyQualifiedName~HostShutdownTests|FullyQualifiedName~ApplicationHeartbeatDiagnosticsTests|FullyQualifiedName~ReadingFreshness|FullyQualifiedName~SavedFreshness --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false`: passed; 173 application cases; 183.467 seconds including startup/builds.
  - `powershell -NoProfile -File build.ps1 -Command checks`: passed; 9 unittest cases; 37.269 seconds including startup/builds.
  - `python -m unittest discover -s tests/check_ports -p test_developer_batch200.py -v`: passed; 43 unittest cases; 35.51 seconds including startup/builds.
  - `python tests/check_ports/workflow_contract.py`: passed; 89.182 seconds including startup/builds.

Every checkpoint passed seven compiled check/report stages and 19 focused checker regressions. Checkpoint 100 passed 19 architecture/containment and 27 requested-runtime contracts plus primary-launcher filter forwarding. At 200, all six reports matched the independent references byte for byte at jobs=1 and jobs=8; stale producers were regenerated and failed producers blocked consumers. GNU Make was checked by source contract because it is unavailable locally.

Versioned evidence records the full base commit of each tested worktree and explicitly identifies pending checkpoint changes. The [manifest](check-evidence/batch200-20261018-manifest.json) fingerprints changed source files. No full requirement is promoted to Verified; all 228 requirements retain their prior status. Existing documentation debt remains visible; the legacy baseline was not refreshed.

## Remaining work

- Deterministic simulated latency/scheduling, step-runner pacing and clock UI integration.
- Fleet/dashboard browser acceptance and comprehensive instrument models/discovery qualification.
- macOS/Linux and foreign-architecture execution, physical-device and power-loss qualification.
- Broader requirement acceptance and legacy API-documentation debt.
- The legacy mutable SCPI transcript remains available for compatibility; external mutations are unsynchronized. Concurrent callers should use the new snapshots and clear API.

Application project builds/tests remain sequential to avoid output races; independent checker stages remain parallel. Console interruption handling is implemented; the automatic process tests exercise timeouts, failure exit codes, ordering and launch failures rather than injecting interactive Ctrl+C. No comparative benchmark speedup is claimed.
