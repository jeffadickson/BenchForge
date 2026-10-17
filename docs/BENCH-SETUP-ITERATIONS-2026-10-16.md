# Ten local BenchForge iterations

Date: 2026-10-16 (America/Los_Angeles). Baseline: `dfba09e46e348401fa7793f1adcb8afe986ceb57`. User requested ten iterations without pushing. All changes and validation below are local; no remote branch update or hosted CI run was requested or performed.

| Iteration | Change | Observed validation |
|---|---|---|
| 1 | Register provider batches atomically, so a late canonical-ID conflict leaves no partial registration | 8 focused IVI host tests passed |
| 2 | Reuse bounded binding validation for both setup and host registration; reject null/malformed metadata before publication | 9 focused tests passed |
| 3 | Require successful measurement configuration before reading a DMM | 10 focused tests passed |
| 4 | Log operation starts, names, completion milliseconds, pinned revision and a session correlation distinct from the lease token | 11 focused tests passed |
| 5 | Record stable preflight rejection reasons for direct service callers, without user role or option payloads | 12 focused tests passed |
| 6 | Add an optional provider identity reader; check only requested fields and retain ownership on mismatch; serial remains optional | 17 focused tests passed |
| 7 | Provide immutable cached session status under the existing lease authority, including during blocked vendor I/O | 18 focused tests passed |
| 8 | Retain resource/lease/revision in tab session storage and inspect it on reload without reopening a driver | DOM/HTTP workflow passed, including a fresh document with retained session data |
| 9 | Serialize instrument button actions, consume confirmations and retain tokens through transient status failures | DOM/HTTP workflow passed, including duplicate clicks and stale capabilities |
| 10 | Allow queued cancellation to finish while an earlier vendor call remains owned; exercise concurrent release and configure failure | All 80 host tests passed; concurrent release closes once and failed configuration is not replayed |

Final review also covered a delayed status response arriving after successful close: the response cannot restore the discarded session. Case-insensitive canonical IDs work during restoration. The real status endpoint rejects a stale lease with HTTP 403. The final DOM/HTTP workflow passed after these corrections.

## Repeatable validation

In this environment, invoke the installed SDK CLI assembly explicitly because `global.json` selects an older SDK feature band:

```sh
/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore
python3 scripts/check_requirements.py
python3 -m unittest discover -s tests/requirements -v
make docs-check DOTNET='/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll'
HAPPY_DOM_MODULE=/tmp/benchforge-dom-tests/node_modules/happy-dom/lib/index.js BENCH_SETUP_DOTNET=/tmp/testharbor-dotnet/dotnet node scripts/check_bench_setup_dom.cjs
```

Iterations 1–7 used the same host-test command with `--filter FullyQualifiedName~HostedIviDmmTests`; iteration 1 restored packages. The DOM harness used happy-dom 21.0.3. All 9 requirements tracker tests and 10 documentation tests passed. The repository documentation audit reports 0 new and 0 generated gaps, with 1,129 existing gaps. No baseline exceptions were added. StrictDoc export was not run because its executable is unavailable here.

## Requirements and scope

The changes extend TR-037 (#46), TR-076 (#49), BD-LIFE-001/005 (#55) and BD-CFG-012 (#69). Existing canonical traceability links remain valid; `HostedIviDmmTests.cs` and the DOM script supplement the original official-interface and logging tests. Baseline acceptance, full-audit fingerprints and coverage notes were updated. No requirement is promoted to Verified.

Tests use official IVI interface doubles. Successful browser activation, measurement and recovery responses are stubbed; configuration, HTTP rejection and service-level canonical ownership use real host code. No vendor driver is bundled, and no physical instrument or Windows ConfigServer loader was qualified. DOM checks do not establish browser rendering acceptance.

Session storage is tab continuity, not durable ownership recovery. Tab closure, blocked storage or a lost activation response can still strand a lease. Host restart is not physical recovery. Synchronous calls and cleanup remain unbounded once dispatched; cancellation only prevents queued/new dispatch. Status is cached metadata rather than a hardware health probe. General lifecycle transitions, authenticated control and restart reconciliation remain incomplete.
