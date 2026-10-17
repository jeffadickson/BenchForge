# Fifth batch: 20 owner-heartbeat iterations

Date: 2026-10-16 America/Los_Angeles (2026-10-17 UTC). This batch follows the 25 shutdown iterations in report D and is published with them. The user's steering explicitly prioritized heartbeat. The implemented loss policy is FenceAndRetain; no heartbeat condition infers physical safety or automatically transfers ownership.

| Iteration | Change or review | Observed validation |
|---|---|---|
| 1 | Finite interval/suspect/expiry policy | 24 focused shutdown/heartbeat tests passed |
| 2 | Process epoch, owner generation and passive snapshots | 25 passed |
| 3 | Increasing sequences; duplicate/out-of-order renewal rejection | 26 passed |
| 4 | Exact host, owner and fixed peer incarnation checks | 27 passed |
| 5 | Monotonic suspect/expiry boundaries and irreversible fencing | 28 passed |
| 6 | Refuse expired-owner device dispatch and generic release | 29 passed |
| 7 | Health responsiveness during blocked I/O and pre-dispatch expiry recheck | 30 passed |
| 8 | Explicit expired-owner reconciliation before reassignment | 31 passed |
| 9 | Background observation without hardware action | 32 passed |
| 10 | Strict local heartbeat API and separate owner state in DMM status | 33 passed |
| 11 | Shared snapshot contract and .NET renewal/acknowledgment helper | 34 passed |
| 12 | Client cancellation, repeated disposal and observer isolation | 35 passed |
| 13 | Independent tab renewal plus actual heartbeat HTTP success/rejection | DOM/HTTP workflow passed |
| 14 | Mismatched acknowledgments, terminal loss and explicit UI recovery | DOM/HTTP workflow passed |
| 15 | Restart epoch rejection with durable quarantine retained | 36 focused tests passed |
| 16 | Passive observers and independent experiment owners | 37 passed |
| 17 | Separate device success, reading receipt and monotonic reading age | 38 passed |
| 18 | Structured heartbeat transitions and failure-isolated sinks | 39 passed |
| 19 | Strict host configuration, overflow bounds and shutdown/renewal exclusion | 40 passed |
| 20 | Real-time process expiry and combined regression review | Process probe and all 150 host tests passed |

## Final local execution

SDK 10.0.402 was invoked explicitly because the repository SDK feature-band pin differs. Focused runs used the host-test command with `--filter FullyQualifiedName~HostShutdownTests`; the final run omitted the filter.

```sh
/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore
/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll build tools/BenchDouble.RecoveryProbe/BenchDouble.RecoveryProbe.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore
BENCH_SETUP_DOTNET=/tmp/testharbor-dotnet/dotnet python3 scripts/check_owner_heartbeat.py
BENCH_SETUP_DOTNET=/tmp/testharbor-dotnet/dotnet python3 scripts/check_ownership_shutdown.py
BENCH_SETUP_DOTNET=/tmp/testharbor-dotnet/dotnet python3 scripts/check_ownership_restart.py
HAPPY_DOM_MODULE=/tmp/benchforge-dom-tests/node_modules/happy-dom/lib/index.js BENCH_SETUP_DOTNET=/tmp/testharbor-dotnet/dotnet node scripts/check_bench_setup_dom.cjs
python3 scripts/check_requirements.py
python3 -m unittest discover -s tests/requirements -v
make docs-check DOTNET='/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll'
git diff --check
```

Observed: 150 host tests passed, zero skipped/failed; real-time heartbeat-expiry, SIGTERM and forced-exit probes passed; DOM/HTTP integration passed; requirement consistency and all 9 tracker tests passed; documentation audit and all 10 documentation tests passed. Documentation audit: 1,504 declarations/modules, 1,128 legacy gaps, zero new/generated gaps. No baseline exceptions were added. StrictDoc export remains unexecuted because its executable is unavailable.

The heartbeat process probe uses a real monotonic clock, waits beyond a short configured deadline, rejects renewal and verifies retained ownership after exit. It uses a plain managed reservation without vendor I/O. Unit tests separately exercise real registry gates and official-interface doubles during blocked operations, recovery and faults. The DOM harness runs actual local heartbeat inspection, successful renewal, replay/wrong-epoch/missing-field/origin rejection, while successful physical DMM operations and special owner-loss UI states are stubbed. No browser rendering or background-tab timing qualification is claimed.

## Requirement status and boundaries

Existing issues #55 and #56 retain lifecycle and heartbeat work; #49 and #69 retain logging/setup acceptance. BD-LIFE-004 and BD-HB-001..006 are now PartialSourceUnverified. Totals: 228 requirements, 144 source-linked unverified, 74 PartialSourceUnverified, 39 Planned, 69 SourceCommittedUnverified, 39 NoImplementationEvidence, 7 DesignOnly, 0 Verified. Audit fingerprints, canonical source/test links and coverage notes accompany the change. This report records scoped local execution, not full Verified acceptance.

Local heartbeat uses the existing bearer lease capability and origin checks; it is not complete remote identity/TLS security. The host epoch changes after restart and the durable journal conservatively quarantines interrupted ownership. Read-only observers never renew control. Only FenceAndRetain is implemented; configurable continuation/shutdown policies for independent telemetry/database peers remain future work. Expiry has no automatic provider recovery, retry or reassignment.

Client helpers and the browser validate acknowledgments but cannot prove device readiness or defeat suspended clients, non-cooperative transports or process/power loss. Cached reading age is monotonic; per-experiment stale-data thresholds and integrated dashboards remain incomplete. The independent scope coordinator remains separate. Physical adapters, vendor drivers, Windows deployment, hardware watchdogs/interlocks and power-loss behavior are unqualified. Synchronous provider execution is not forcibly cancelled by a deadline. Procedure/editor expansion remains deferred.
