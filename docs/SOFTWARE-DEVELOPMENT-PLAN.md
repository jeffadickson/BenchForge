# BenchDouble Software Development Plan (SDP)

**Baseline:** SDP-001 · 2026-10-15 · **Maturity:** active prototype / pre-beta; not production qualified.

## 1. Product and process
BenchDouble is a .NET laboratory instrument-control, mocking and experiment-execution platform with typed IVI/SCPI/legacy GPIB APIs, local/remote instrument hosts, virtual bench physics, procedure editing, monitoring and persistence. Development follows requirements-driven increments: **requirement → GitHub issue → implementation → unit/BDD/browser test → CI evidence → review → release**. Keep evidence and implementation status distinct.

## 2. Configuration and change control
- GitHub repository: `jeffadickson/BenchDouble`. All source, docs and tests are reviewed as commits/PRs.
- Stable IDs in `requirements/benchdouble.sdoc`; `requirements/traceability.csv` is the requirement-to-code/test/issue matrix.
- StrictDoc parses/exports the requirements. Reqnroll executes Gherkin acceptance scenarios tagged with requirement IDs. xUnit is used for component tests.
- Existing `TR-001..TR-090` requirements remain valid; subsystem specifications are linked from the software description.
- Changes to a released requirement are reviewed; record rationale and impact on code/tests/manual.
- STRIDE threat modeling is used for remote-host identity, driver package loading, safety boundaries and secrets, not as a requirements tool.

## 3. Work planning
Priority P0: safety/ownership, data integrity, type correctness, reproducible build, essential verification.
P1: integrated editor, durable notifications, distributed reliability, full browser tests, operational reporting.
P2: rich WebAssembly browser simulation, optional database backends, visual wiring and advanced solvers.

GitHub Issues #1–#26 cover existing known subsystem gaps; #27–#33 cover browser tests, bootstrap, manuals, overview/WebAssembly, requirements CI, notification reliability and audit closure. Every `Planned` requirement needs an issue; no issue is closed without linked test evidence or an explicit justified deferral.

## 4. Architecture and dependency policy
- Instrument core: .NET 8, UnitsNet and logging abstractions. No GUI/database dependency.
- Optional adapters: SQLite, ScottPlot, IVI/VISA, host transport, driver generation, plotting.
- `Microsoft.Extensions.DependencyInjection` only in application composition roots; per-run clocks, simulation models and stateful mocks are scoped.
- `Microsoft.Data.Sqlite` local WAL recorder; TimescaleDB replication remains unimplemented.
- Reqnroll.xUnit and Microsoft.Playwright are **test-only** packages. StrictDoc is a Python tool for requirements, not a runtime dependency.
- Pin SDK with `global.json`; pin NuGet package versions; create/commit NuGet lockfiles and centralize versions in a future dependency-hardening issue.
- No arbitrary uploaded C# may execute in the privileged instrument host. Isolate Roslyn-compiled mock workers.

## 5. Development and verification
1. `bash scripts/bootstrap.sh` on Linux/macOS, `pwsh -File scripts/bootstrap.ps1` on Windows.
2. `python3 scripts/check_requirements.py` checks IDs, statuses, local paths and issue links.
3. StrictDoc export validates the machine-readable baseline and produces navigable HTML.
4. `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj`: component/physics/monitor tests.
5. `dotnet test tests/BenchDouble.Acceptance.Tests/BenchDouble.Acceptance.Tests.csproj`: Reqnroll scenarios.
6. `dotnet test tests/BenchDouble.Browser.Tests/BenchDouble.Browser.Tests.csproj`: real Chromium DOM/SVG smoke tests after Playwright browser install.
7. SQLite tests, other package tests and demo applications run on the Windows/Linux/macOS CI matrix.
8. Physical hardware tests are opt-in, authorized, fixture-specific, and require separate safe-state verification. A green mock CI is not a hardware qualification.

## 6. Quality gates and exit criteria
- All P0 requirements have a verified test or a documented release-blocking exception.
- All public interfaces have XML documentation and examples; package boundaries remain acyclic.
- No unbounded queues, silent telemetry loss, or unverified safe-state claims.
- CI succeeds on all supported OSes; dependency restore reproducibility and vulnerabilities are reviewed.
- At least one complete simulated experiment: configure, acquire, alarm, persist, plot, cleanup, replay.
- Browser tests verify logic-to-UI integration, not only a hardcoded SVG.
- Reproducible builds and signed release artifacts; package SBOM and dependency licenses reviewed.

## 7. Risk register
| Risk | Mitigation | Current status |
|---|---|---|
| False hardware-safe-state claims | Separate simulation and physical recovery contracts | Open |
| Mock UI duplicates C# engine | Move UI to backend event stream; integration tests | Open |
| Disconnected central database | Local SQLite durable spool; replication pending | Partial |
| Untrusted Roslyn plugin execution | Isolated worker, signed behavior packages | Open |
| Package/platform differences | Three-OS CI, optional vendor SDK detection | Partial |
| Requirement status drift | StrictDoc + CSV checker + CI evidence | New |
| Plot regressions | Playwright real-browser smoke tests | New |
| Poor traceability across 154 requirements | Map issues/tests and audit every release | In progress |

## 8. Release strategy
Prototype → hardware-free alpha → controlled bench beta → lab pilot → production release. Do not label a phase complete based on source commits alone. For each release attach a requirement coverage report, test summary, known limitations, package/dependency inventory and user manual revision.

## 9. Dependency baseline
The [dependency inventory](DEPENDENCIES.md) is the maintained source for SDK/tool/package versions, platform assumptions and supply-chain risks. Issue #28 tracks locked NuGet/Python dependencies, clean-host provisioning, SBOM and cross-platform evidence. Until that work passes CI, the bootstrap scripts are a best-effort convenience rather than a fully reproducible build guarantee.

## 10. Experiment composition and dashboard verification increment

New requirements **BD-EXP-001..005**, **BD-DASH-001..006**, **BD-VERSION-001..004** and **BD-PARITY-001** expand the StrictDoc baseline to 138. Source/test commits provide a simulation-only experiment composition root and a read-only local host dashboard. Before declaring this increment complete, run xUnit experiment composition and host dashboard tests on Windows/macOS/Linux; start the host and verify dashboard refresh, reservation state transitions, sanitized logs and offline behavior in a real browser; validate all StrictDoc/traceability links; review lifecycle, host authentication and safe-state boundaries. Issues #35–#39 track remote host resolution, dashboard aggregation, CI evidence, versioned experiment manifests and evidence-based PyMeasure comparison. **Do not claim broad product superiority based on proposed features alone.** See [version control tutorial](VERSION-CONTROL-TUTORIAL.md) and [dashboard guide](INSTRUMENT-DASHBOARD.md).

## 11. AutoMock implementation and verification increment

StrictDoc adds BD-AM-001..012 to the requirement baseline. The initial `ILoadBehavior` rule decorator, data-only JSON parser, experiment composition registration, rule digest, xUnit tests and demonstration have been committed. Test cases must verify the exact five-second fault boundary, baseline fallback, voltage predicates, priority, invalid configuration rejection, disabled-output invariant, run isolation and stable rule hash. Three-OS CI now includes fluent and JSON AutoMock demos; **a green run has not been observed**. Issue #40 tracks command/state and mode-aware mocks; #41 evaluates optional Microsoft RulesEngine/NRules/Dynamic Expresso; #42 integrates a complete experiment, alarms, persistence and dashboard; #43 validates cross-platform dependencies and full manifest provenance. The rule evaluator remains deterministic, side-effect-free and local to an isolated simulation run; no untrusted code executes in a privileged host.

The same increment now includes **BD-AM-012**: per-role keyed AutoMock behavior registration and tests with two independently faulted instruments. Verification status remains SourceCommittedUnverified pending actual CI execution.

## 12. Generic unit-aware mock capability increment

New requirements BD-AM-013..016 cover explicit UnitsNet generic measurement types, instrument mode safety, generated capability metadata and time-indexed default values. Source-committed `TypedMockMeasurements`, `TypedMockMeter`, xUnit tests, standalone demo and experiment registration implement the initial foundation. Build and runtime verification remain pending. Issue #44 tracks generation from instrument definitions after the dataset has adequate mode, quantity, unit and range metadata. Release criteria include rejecting unsupported dimensions and mismatched meter modes, preserving UnitsNet conversions, and verifying Windows/macOS/Linux test execution.

## 13. Requirement audit and release-gate remediation

The [2026-10-15 runner requirements audit](REQUIREMENTS-AUDIT-2026-10-15.md) individually assessed TR-001..TR-090: 45 PartialSourceUnverified, 6 DesignOnly and 39 NoImplementationEvidence. These categories are evidence findings, not acceptance. All 154 requirements remain unverified or planned. Source and test paths, remaining gaps and issue links are recorded in `requirements/legacy-runner-audit.csv`; the canonical traceability matrix has been reconciled. The static checker now fails on missing/duplicate legacy IDs, nonexistent evidence paths and mismatched statuses. Observed GitHub Actions push runs failed across Windows, Ubuntu and macOS; detailed logs could not be retrieved. **Release gate:** no requirement may move to Verified until the CI/build/test failure is resolved and the specific acceptance evidence is reviewed. Prioritize [#50 CI](https://github.com/jeffadickson/BenchDouble/issues/50), [#47 runner safety](https://github.com/jeffadickson/BenchDouble/issues/47), [#46 host security](https://github.com/jeffadickson/BenchDouble/issues/46), then [#45 structured procedures](https://github.com/jeffadickson/BenchDouble/issues/45), [#48 evidence](https://github.com/jeffadickson/BenchDouble/issues/48) and [#49 browser/telemetry](https://github.com/jeffadickson/BenchDouble/issues/49).

## 14. Runner safety increment and CI diagnostic boundary

Issue #47 now has a bounded-shutdown and RecoveryRequired source increment plus xUnit cases for failure, timeout and cancellation after dispatch. The runner refuses reuse after unconfirmed cleanup. This is **not** a validated physical interlock; device abort/disable confirmation, host fencing and full auditable state transitions remain P0 gaps. CI issue #50 is still open: GitHub Actions job records show all three OS jobs terminating in seconds with **zero executed steps and no assigned runner name**. This indicates failure before normal build/test execution, not evidence of a C# compilation error. GitHub check-run records expose diagnostic annotation counts but their details could not be retrieved; root cause remains unknown. The workflow now has concurrency cancellation and manual dispatch to reduce push storms. Actual green CI and requirement-level test evidence are still mandatory.
