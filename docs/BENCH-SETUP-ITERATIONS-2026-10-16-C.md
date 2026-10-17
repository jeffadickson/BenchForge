# Third batch: twenty BenchForge iterations

Completed 2026-10-17 UTC. Baseline local commit: `d82f233`. The user requested twenty more iterations and then publication, including the preceding two local ten-iteration batches. Work remains within instrument configuration, canonical ownership, recovery and logging; procedure/editor expansion stays deferred.

| Iteration | Implementation | Observed validation |
|---|---|---|
| 1 | Bounded ownership record and order-independent route digest without raw addresses | 1 focused journal test passed |
| 2 | Atomic flushed snapshot with exact review-ID removal | 2 journal tests passed |
| 3 | Single writer lock held for the journal lifetime | 3 journal tests passed |
| 4 | Fail-closed malformed, duplicate, missing, oversized and future journal state | 4 journal tests passed |
| 5 | Persist canonical managed ownership before factory dispatch | 35 focused hosted-IVI tests passed |
| 6 | Remove durable evidence only after cleanup or recovery succeeds | 36 hosted-IVI tests passed |
| 7 | Restore quarantine and reject old process leases | 37 hosted-IVI tests passed |
| 8 | Expose orphaned records without a registered provider | 38 hosted-IVI tests passed |
| 9 | Separate qualified restart callback with exact record and confirmation checks | 39 hosted-IVI tests passed |
| 10 | Reject changed route mappings before reconciliation | 40 hosted-IVI tests passed |
| 11 | Serialize per-record reconciliation, honor queued cancellation and keep unrelated inspection available | 41 hosted-IVI tests passed |
| 12 | Retain quarantine on failed acknowledgment without replaying completed provider work in-process | 42 hosted-IVI tests passed |
| 13 | Default application journal and strict same-origin recovery endpoints | Host build and DOM/HTTP workflow passed |
| 14 | Operator inspection and explicit restart-reconciliation controls | Host build and DOM/HTTP workflow passed |
| 15 | Clear stale browser capabilities and direct the operator to recovery | DOM/HTTP workflow passed |
| 16 | Invalidate failed inspections and consume each reconciliation confirmation | DOM/HTTP workflow passed |
| 17 | Structured ownership/reconciliation events with payload exclusion | 43 hosted-IVI tests passed |
| 18 | Missing-provider, unsupported-restart and changed-route guidance | 44 hosted-IVI tests passed |
| 19 | Sanitize persistence errors, latch write failure and prevent factory dispatch | 50 combined hosted-IVI/journal tests passed |
| 20 | Forced-process-exit probe and prevention of quarantine bypass through renamed/missing registrations | Probe passed; final full host suite passed all 110 tests |

## Execution evidence

Local Linux .NET SDK 10.0.402 was invoked explicitly because `global.json` restricts the SDK feature band. Final commands:

```sh
/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore
/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll build tools/BenchDouble.RecoveryProbe/BenchDouble.RecoveryProbe.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore
BENCH_SETUP_DOTNET=/tmp/testharbor-dotnet/dotnet python3 scripts/check_ownership_restart.py
HAPPY_DOM_MODULE=/tmp/benchforge-dom-tests/node_modules/happy-dom/lib/index.js BENCH_SETUP_DOTNET=/tmp/testharbor-dotnet/dotnet node scripts/check_bench_setup_dom.cjs
python3 scripts/check_requirements.py
python3 -m unittest discover -s tests/requirements -v
make docs-check DOTNET='/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll'
git diff --check
```

Results: 110 host tests passed with zero failures/skips; the forced-exit probe passed; DOM/HTTP integration passed; requirement consistency and all 9 tracker tests passed; documentation audit and all 10 documentation tests passed. The documentation audit examined 1,421 declarations/modules, with 1,128 legacy gaps, zero new gaps and zero generated gaps. No documentation baseline exceptions were added. StrictDoc export was not run because its executable is unavailable.

The probe waits for a child process to persist a canonical managed reservation, forcibly kills it without cleanup, then starts a separate process against the same journal. It checks quarantine, stale-review rejection, explicit callback reconciliation, fresh reservation and cleanup. It does not open a vendor driver or terminate an actual hardware command. Unit tests separately cover persistence-before-factory ordering, failed cleanup, blocked callback cancellation, old lease rejection, logging failures and retained ownership.

The DOM harness uses happy-dom 21.0.3. Configuration, journal creation, empty recovery inspection and strict/rejected HTTP requests use the real host. Successful instrument and restart-reconciliation UI responses are stubbed. Failed reconciliation retains the record and consumes confirmation; failed inspection blocks reconciliation until refreshed. Rendered-browser acceptance was not performed.

## Requirements and limits

Existing requirements TR-076 (#49), BD-LIFE-001/005/006 (#55) and BD-CFG-012 (#69) carry updated acceptance and audit fingerprints. BD-LIFE-006 moves from Planned to PartialSourceUnverified. Counts: 228 total; 137 source-linked unverified; 67 PartialSourceUnverified; 46 Planned; 69 SourceCommittedUnverified; 39 NoImplementationEvidence; 7 DesignOnly; 0 Verified. The report is scoped local execution evidence, not full acceptance or a Verified evidence artifact.

The journal covers registered physical DMM ownership in the application. It does not coordinate different journal directories or hosts, automatically identify physical aliases, supply vendor factories, or qualify restart safe-state procedures. The default host still has no physical provider. Missing/changed interrupted mappings conservatively block new physical ownership; matching records can be reconciled only through a separately registered callback. Snapshot flush/rename is tested for process termination, not power loss, directory durability, network filesystems or Windows deployment. The journal directory must remain host-owned and stable; deleting or replacing it is not reconciliation.

Dispatched synchronous vendor calls remain unbounded. Authentication for remote operation, real instrument/adapter qualification and Windows IVI loader acceptance remain outstanding. Provider success followed by a failed durable acknowledgment can be retried without callback replay only within that process; another restart requires explicit reconciliation again. Diagnostic counters are process-local and are not a durable audit trail.
