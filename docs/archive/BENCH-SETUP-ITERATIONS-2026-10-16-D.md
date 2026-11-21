# Fourth batch: 25 shutdown iterations

Date: 2026-10-16 America/Los_Angeles (2026-10-17 UTC). Baseline: `008937b74b398c178352745cdf5115ac04178f40`. The user requested 25 iterations and publication, then added 20 heartbeat iterations before the combined push. Procedure/editor expansion remains deferred.

| Iteration | Change or review | Observed validation |
|---|---|---|
| 1 | Finite shutdown policy and report contracts | 1 focused test passed |
| 2 | Irreversible canonical admission stop | 2 passed |
| 3 | Reject existing simulation command dispatch after drain | 3 passed |
| 4 | Block typed dispatch/registration while retaining cached inspection | 4 passed |
| 5 | Recheck admission after waiting for a vendor-call gate | 5 passed |
| 6 | Shared shutdown task, canonical cleanup gates and bounded decision | 6 passed |
| 7 | Preserve unresolved startup quarantine | 7 passed |
| 8 | Refuse automatic physical cleanup without a qualified provider | 8 passed |
| 9 | Bound waiting behind an in-flight driver | 9 passed |
| 10 | Prevent manual close from bypassing shutdown policy after drain | 10 passed |
| 11 | Explicit safe-state preparation before close and durable release | 11 passed |
| 12 | Retain evidence after failed preparation | 12 passed |
| 13 | Independent resource cleanup while one provider blocks | 13 passed |
| 14 | Worker cap and cancellation of cleanup not yet dispatched | 14 passed |
| 15 | Track late workers without rewriting the original deadline report | 15 passed |
| 16 | Generic Host stopping signal and independent cleanup budget | 16 passed |
| 17 | Strict host configuration and startup rejection | 17 passed |
| 18 | Read-only lifecycle endpoint and stopping HTTP behavior | DOM/HTTP workflow passed |
| 19 | Operator shutdown-status panel | DOM/HTTP workflow passed |
| 20 | Failed/stale inspection and safe-rendering checks | DOM/HTTP workflow passed |
| 21 | Payload-free shutdown outcome logging | 18 focused tests passed |
| 22 | Throwing sinks and 32 concurrent shutdown callers | 19 passed |
| 23 | Actual SIGTERM/Generic Host process checks | Clean reservation and retained quarantine checks passed |
| 24 | Preparation/close/prior-device-failure matrix and retained monitor state | 22 focused tests passed |
| 25 | Shutdown versus blocked initialization race | 23 focused tests passed; all 133 host tests passed |

Focused runs used `--filter FullyQualifiedName~HostShutdownTests` with the final host-test command below. Compilation errors in test scaffolding were corrected before counting a pass. Later heartbeat tests extend the same fixture; the 133-test checkpoint precedes that batch.

```sh
/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore
/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll build tools/BenchDouble.RecoveryProbe/BenchDouble.RecoveryProbe.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore
BENCH_SETUP_DOTNET=/tmp/testharbor-dotnet/dotnet python3 scripts/check_ownership_shutdown.py
```

The SIGTERM probe uses a real Generic Host and the canonical journal with a plain managed reservation; it does not open a vendor driver. It verifies release after an orderly signal and preservation of unresolved startup quarantine. Unit tests use official-interface doubles for safe-state callbacks, failures, blocked calls and concurrent cleanup. DOM tests use actual host read/rejection endpoints and stubbed successful physical-driver responses. Browser rendering, physical safe-state behavior, Windows, independent interlocks and power-loss behavior are not qualified.

BD-LIFE-004 advances to PartialSourceUnverified under existing issue #55. Full hardware shutdown acceptance remains incomplete. TR-076, BD-LIFE-005 and BD-CFG-012 gain observable acceptance without Verified promotion. Combined final gates and totals are recorded in the following heartbeat report. Finite coordinator waiting is not forced interruption of synchronous driver, filesystem or logging code; retained evidence and independent hardware protection remain essential boundaries.
