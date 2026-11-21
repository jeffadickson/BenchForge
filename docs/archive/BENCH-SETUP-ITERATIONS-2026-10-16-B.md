# Second batch: ten local BenchForge iterations

Date: 2026-10-16 (America/Los_Angeles). Baseline local commit: `e0f665b628456fed4dde37e2f881009760cfe04e`. User requested another ten iterations without pushing. This batch builds on the earlier unpushed ten-iteration checkpoint; no remote branch or hosted CI changes were made.

| Iteration | Implementation | Observed validation |
|---|---|---|
| 1 | Isolate canonical-registry and typed-IVI diagnostic sink exceptions; count failures without changing driver results | 23 focused host tests passed, including failed sinks during successful I/O and recovery |
| 2 | Move descriptor ownership checks into the shared registry, preventing collisions across separate provider-service instances | 24 focused tests passed |
| 3 | Copy and enforce declared provider functions; omission permits DC voltage only; expose metadata without opening a driver | 25 focused tests passed, including mutation and unsupported-function rejection |
| 4 | Require explicit runtime request fields and reject duplicate/unknown JSON rather than defaulting a missing function | 26 focused tests passed |
| 5 | Return atomic reading metadata: function, units, requested settings, revision, sequence and host receipt time | 27 focused tests passed |
| 6 | Expose last failure phase and non-capability correlation; log close/recovery starts and elapsed completion time | 28 focused tests passed |
| 7 | Permit cancellation of queued recovery while allowing dispatched recovery to finish under ownership | 29 focused tests passed |
| 8 | Populate the UI with declared functions and display measurement units/provenance from the response | DOM/HTTP checks passed, including actual HTTP rejection of a missing function |
| 9 | Show private retained lease details and inspect a supplied capability in another tab without driver activation | DOM/HTTP checks passed, including invalid details, explicit confirmation and unchanged activation count |
| 10 | Add activation correlation to readings and exercise concurrent configuration, sequence allocation, recovery and invalid measurements | All 93 host tests passed; concurrent successful recovery runs its callback once |

## Final validation

The focused suite used the following command with `--filter FullyQualifiedName~HostedIviDmmTests`. The final run omitted that filter and passed all 93 host tests:

```sh
/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nr:false --no-restore
python3 scripts/check_requirements.py
python3 -m unittest discover -s tests/requirements -v
make docs-check DOTNET='/tmp/testharbor-dotnet/dotnet /tmp/testharbor-dotnet/sdk/10.0.402/dotnet.dll'
HAPPY_DOM_MODULE=/tmp/benchforge-dom-tests/node_modules/happy-dom/lib/index.js BENCH_SETUP_DOTNET=/tmp/testharbor-dotnet/dotnet node scripts/check_bench_setup_dom.cjs
```

The final DOM/HTTP workflow passed with happy-dom 21.0.3. All 9 requirements tracker tests and 10 documentation tests passed. Documentation audit: 1,383 declarations/modules, 1,129 existing gaps, 0 new gaps and 0 generated gaps. No baseline exceptions were added. JavaScript syntax and whitespace checks passed. StrictDoc export was not run; its executable is unavailable in this environment.

Stable requirements TR-037 (#46), TR-076 (#49), BD-LIFE-005 (#55) and BD-CFG-012 (#69) were extended with acceptance criteria, audit fingerprints and coverage notes. Canonical source/test links remain valid; the host suite supplements original IVI and logging tests. Status counts remain unchanged and no requirement is marked Verified.

## Practical limits

Tests use official IVI interface doubles. Successful browser driver responses are stubbed; the DOM harness exercises actual configuration and rejection endpoints. No physical hardware, installed vendor driver, Windows ConfigServer loader or rendered-browser acceptance was performed.

Provider capability declarations require integrator qualification. Descriptor equality does not discover different physical aliases. Requested range/resolution are not verified instrument settings. Reading time is host receipt time, not hardware sampling time, and sequence is scoped to one activation correlation.

Private lease resumption requires a previously retained token; it cannot reconstruct a lost activation response. Tokens are control capabilities and are never log correlation IDs. Host ownership is still in memory, so restart reconciliation, authenticated remote use and deadline-bounded shutdown remain unfinished. Diagnostic write counters are process-local and do not constitute a durable audit. Procedure/editor expansion remains deferred.
