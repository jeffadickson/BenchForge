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
