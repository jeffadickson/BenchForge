# Dependency and Platform Inventory

**Baseline:** DEP-001 · 2026-10-15 · **State:** explicit versions in project files; not yet a locked, reproducible dependency graph.

## Runtime/build packages

| Component | Package/tool | Current referenced version | Boundary |
|---|---|---|---|
| .NET SDK | `global.json` | 8.0.100, latestFeature roll-forward | Windows/macOS/Linux build |
| UnitsNet | `UnitsNet` | 5.75.1 | typed quantities and conversion |
| Logging | `Microsoft.Extensions.Logging.Abstractions` | 8.0.3 | core instrumentation |
| SQLite | `Microsoft.Data.Sqlite` | 8.0.11 | optional durable recorder |
| DI | `Microsoft.Extensions.DependencyInjection` | 8.0.1 | demo/application composition roots |
| Scientific plots | `ScottPlot` | 5.1.59 | optional headless renderer |
| Requirements | `strictdoc` Python package | currently unpinned | documentation tool only |
| Acceptance | `Reqnroll.xUnit` | 3.3.4 | test-only |
| Browser testing | `Microsoft.Playwright` | 1.56.0 | test-only; Chromium downloaded separately |
| Component tests | `xunit`, `Microsoft.NET.Test.Sdk` | 2.9.2 / 17.12.0 | test-only |

Other optional adapters and package versions are defined in their respective `*.csproj` files. This inventory is not yet a generated SBOM.

## Provisioning

`scripts/bootstrap.sh` (macOS/Linux) and `scripts/bootstrap.ps1` (Windows) check for .NET 8, install a local SDK if absent, create a Python environment, install StrictDoc, validate the requirement matrix and restore all C# project dependencies. The bootstrap requires Git, Python 3, an HTTPS downloader (curl on Unix), network access, and on Windows PowerShell 7 plus Python launcher `py`.

The GitHub Actions matrix provisions .NET 8 via `actions/setup-dotnet`, Python 3.12 via `actions/setup-python`, installs StrictDoc, restores projects during build/test and installs Playwright Chromium for browser tests.

## Portability

The **hardware-free .NET 8 library, mocks, SQLite recorder, plots, Reqnroll tests and Chromium browser tests** target Windows, macOS and Linux. Cross-platform source compatibility does not prove a passing build. Vendor VISA/IVI/GPIB drivers, DLL loading and USB/GPIB permissions are platform-dependent and remain opt-in.

The standalone HTML demos run in current browsers with no server; they are JavaScript simulations. Running real C# in a browser requires a separate WebAssembly build and is tracked by GitHub Issue #30.

## Security and reproducibility backlog

- Pin StrictDoc and Python transitive dependencies in a lockfile rather than installing latest.
- Centralize NuGet versions and commit `packages.lock.json` for every supported project; use locked restore in CI.
- Add vulnerability, license and SBOM scans; monitor package advisories.
- Pin GitHub Actions to reviewed SHA digests rather than floating major tags.
- Verify official .NET installer downloads and provenance; ensure offline cache strategies are documented.
- Build hardware-free tests in clean Windows/macOS/Linux environments, including native plotting and SQLite dependencies.
- Keep Roslyn-compiled user behaviors out of privileged host processes.

**No package compatibility or security audit is considered complete until a successful dependency restore, build and scan is recorded.**

## Experiment composition and local dashboard increment

`src/BenchDouble.Experiments` depends on `BenchDouble` and **Microsoft.Extensions.DependencyInjection 8.0.1**; the core does not depend on the DI container. The example `BenchDouble.ExperimentDemo` depends on the experiment project. The dashboard is static HTML/CSS/JavaScript served by the existing ASP.NET Core `BenchDouble.Host.Server`; **no charting or UI NuGet package** is added. `tests/BenchDouble.Experiments.Tests` uses xUnit 2.9.2, xunit.runner.visualstudio 2.8.2 and Microsoft.NET.Test.Sdk 17.12.0. Bootstrap scripts already restore recursively discovered C# projects. The Makefile and three-OS CI now explicitly build/run the experiment demo and its tests. Versioned behavior manifests, dependency lockfiles and external host TLS/auth are still outstanding.

## AutoMock dependency decision

The first AutoMock engine uses only .NET 8 `System.Text.Json`, `System.Security.Cryptography` and the existing UnitsNet and Microsoft.Extensions.DependencyInjection dependencies. **No additional runtime NuGet package is required.** Microsoft RulesEngine, NRules and Dynamic Expresso were evaluated as optional advanced rule-engine candidates, not installed. The v1 JSON rules are data-only and never execute arbitrary expressions. Reproducibility still requires NuGet lockfiles, dependency vulnerability checks, compiler/runtime versions and a full signed experiment manifest beyond the current rule-set digest. See [AUTOMOCK-RULES.md](AUTOMOCK-RULES.md).

## Generic quantity mock dependencies

`TypedMockMeasurements` and `TypedMockMeter` reuse the existing **UnitsNet 5.75.1** package and .NET 8 generic runtime support; no new runtime NuGet package is required. The test suite uses existing xUnit packages. The typed meter demo is included in Makefile build targets and the three-OS CI matrix. Driver-definition-based capability generation and compatibility tests remain tracked in issue #44.

## Optional procedure parser package

`src/BenchDouble.Procedures/BenchDouble.Procedures.csproj` references **Parlot 1.5.9** (stable NuGet package, BSD-3-Clause license) and the existing `BenchDouble` and `BenchDouble.Experiments` projects. The core instrument library does **not** depend on Parlot. `tests/BenchDouble.Procedures.Tests` uses the existing xUnit, runner and test SDK packages; the example uses the existing logging abstractions. `make build`, `make test`, `make demo-procedure` and the three-OS CI workflow include the new projects. NuGet lockfiles and successful CI validation remain outstanding.

## Generated driver/mock dependencies

The `BenchDouble.DriverGen` console tool uses only .NET 8 `System.Text.Json`, `Regex` and standard code generation; generated typed drivers and mocks depend on the existing UnitsNet 5.75.1 and `IAsyncScpiSession` contracts. No additional NuGet runtime package is required. A standard-library Python catalog validator is used by `make generate` and CI. Generated source is committed so direct `dotnet build` and editor IntelliSense do not depend on running code generation first. The optional no-write `--check` gate detects stale source. Cross-platform CI cannot be marked green while GitHub runner allocation is unavailable.

## Instrument Definer dependencies

The new `tools/InstrumentDefiner/definer.py` uses Python standard-library `ast`, `csv`, `json`, `argparse` and `unittest`; **no external instrument source installation is required to parse an upstream source file**, and the tool never imports that file. Schema-v2 JSON and CSV templates are repository assets. The core mode-aware mock preview uses existing .NET 8 `System.Text.Json` and UnitsNet 5.75.1; no new NuGet runtime package was added. `make definer-check` provides local checks independent of GitHub-hosted CI.



## Local-first setup update — 2026-10-15

`make setup` and `scripts/bootstrap.ps1` discover and restore every project under
`src`, `tests`, `examples` and `tools`. They use global.json when installing an SDK
and share `tools/requirements.txt` with CI (StrictDoc 0.30.2). The routing client
adds Microsoft.Extensions.Logging.Abstractions 8.0.3, matching core.
`tools/BenchDouble.BrowserSetup` uses Microsoft.Playwright 1.56.0, matching browser
tests, and removes the Unix PowerShell dependency for Chromium provisioning.
See `docs/LOCAL-FIRST-CONFIGURATION.md` for prerequisites and Linux system libraries.
Transitive Python/NuGet lockfiles and clean-platform provisioning remain #28/#60.

## API documentation

- DocFX 2.78.3 is pinned in `.config/dotnet-tools.json`; both bootstrap scripts run
  `dotnet tool restore`. This version includes a .NET 8 tool target.
- Sphinx 8.1.3 and Docutils 0.21.2 are pinned in `tools/docs-requirements.txt`,
  installed into `.tools/docs-venv`. StrictDoc uses its own requirements environment
  with Docutils 0.22.2. They must not be installed into the same Python environment.
- The syntax audit uses Microsoft.CodeAnalysis.CSharp 4.8.0, matching the pinned
  SDK's C#12 generation. Project discovery restores it automatically.
- `make docs` builds a combined site; `make docs-check` audits all owned source.
  Full transitive dependency locking remains tracked in #28.

## Local binary captures

The optional `BenchDouble.Artifacts.Sqlite` package uses Microsoft.Data.Sqlite
8.0.11, matching the existing time-series SQLite adapter. Bootstrap project
discovery includes the capture demo, xUnit tests and process-exit probe.
`make artifact-check` runs the focused tests and recovery probe.
