# BenchDouble User Manual

**Revision:** UM-001 · 2026-10-15 · **Status:** prototype guide; not a physical-hardware operating procedure.

## What BenchDouble does
BenchDouble supports strongly typed .NET instrument control, stateful programmable mocks, a shared virtual simulation clock, distributed instrument-host architecture, UnitsNet measurements, plot adapters, local SQLite recording and test-runner design. Many integrations are still under development; read [requirements](../requirements/README.md) and [roadmap](ROADMAP.md) before relying on a feature.

## Setup

### Windows
Install Git and PowerShell 7 (if not already installed), clone the repository, then run:
```powershell
pwsh -File scripts/bootstrap.ps1
dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
```

### macOS and Linux
Install Git, Python 3 and curl, then:
```bash
bash scripts/bootstrap.sh
dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
```

The scripts attempt to install .NET 8 locally if missing, provision StrictDoc in a Python virtual environment, validate requirements and restore NuGet packages. **Internet access is required for first-time provisioning.** On Unix, if .NET was installed under `.tools/dotnet`, add that folder to PATH in subsequent shells. On Windows use the same PowerShell session or add the local folder to PATH. Optional physical VISA/IVI vendor drivers are not installed automatically; hardware capability is platform/vendor dependent.

## Example 1: dependency-injected power supply
```bash
dotnet run --project examples/BenchDouble.DiDemo
dotnet run --project examples/BenchDouble.DiDemo -- --normal
```
Default mode uses a 12 V, 24 Ω load and injects 2.5 A at virtual t=5 s; the controller monitors a 2 A threshold, records a simulated operator notification and requests output off. Normal mode remains at 0.5 A. No email or SMS is sent.

## Example 2: unattended operations browser monitor
Open `examples/UnattendedOperationsMonitor/index.html` in a browser. Start the run, change the overcurrent threshold, inject a fault, pause/reset, and inspect the simulated notification log. The browser page uses a self-contained JavaScript simulation **not yet wired to the C# backend**.

## Example 3: thermal chamber and virtual sensor
Open `examples/ThermalCycleMonitor/index.html` to explore a seven-hour chamber profile (30→70→−30→30°C), a 20-minute thermal sensor lag, and a 10% hot-temperature supply current change. The page supports accelerated and manually advanced virtual time. Run `dotnet run --project examples/BenchDouble.ThermalCycleDemo` for the C# DI/virtual-wiring version.

The C# demo writes `thermal-demo-output/measurements.sqlite` and Plotly JSON figures. SQLite data persists between runs; the demo uses a fixed run ID and timestamps, so rerunning with identical data is idempotent. Modify the run ID for independent experiments.

## Example 4: requirements and tests
- `requirements/benchdouble.sdoc`: StrictDoc source.
- `requirements/traceability.csv`: 122 requirements, source/test/issue mapping and verification status.
- `python3 scripts/check_requirements.py`: quick coverage validation.
- `dotnet test tests/BenchDouble.Acceptance.Tests/BenchDouble.Acceptance.Tests.csproj`: Given–When–Then acceptance.
- `dotnet test tests/BenchDouble.Browser.Tests/BenchDouble.Browser.Tests.csproj`: Chromium UI checks, after installing the browser via Playwright.

## Interpreting the plots
The chamber line is the **setpoint**; the sensor line is a simulated first-order response and lags ramps. Current depends on the selected mock behavior, not measured device physics. Plot labels preserve UnitsNet dimensions; simulation time differs from wall-clock time.

## Operating constraints and troubleshooting
- **No actual hardware safety is provided by the demos.** Real equipment requires authorized leases, verified output shutdown, interlocks, calibration, and vendor-specific drivers.
- If `dotnet` is missing, rerun the bootstrap script and verify `dotnet --info`.
- If NuGet restore fails, verify network/proxy settings and supported SDK.
- If Playwright tests fail with missing browser, run `pwsh tests/BenchDouble.Browser.Tests/bin/Debug/net8.0/playwright.ps1 install chromium` after building the project, or follow the CI installation step.
- If StrictDoc fails, run the Python requirements checker first, then inspect `requirements/benchdouble.sdoc`.
- If SQLite output is not writable, choose a writable working directory; never silently drop accepted samples.
- macOS/Linux support applies to hardware-free .NET features. Vendor-specific GPIB/VISA/IVI drivers may require Windows or vendor-supported OS/runtime versions.

## Where to go next
[Software description](SOFTWARE-DESCRIPTION.md) · [Development plan](SOFTWARE-DEVELOPMENT-PLAN.md) · [Requirements](../requirements/README.md) · [GitHub Issues](https://github.com/jeffadickson/BenchDouble/issues) · [HTML project overview](../examples/ProjectOverview/index.html)

## Dependency and environment reference
For pinned project dependencies, browser requirements, optional hardware SDKs and outstanding platform caveats, see [Dependency Inventory](DEPENDENCIES.md). A cross-platform .NET build does not establish vendor VISA/IVI hardware compatibility.

## Experiment registration and dependency injection

Run `dotnet run --project examples/BenchDouble.ExperimentDemo/BenchDouble.ExperimentDemo.csproj` (or `make demo-experiment`). The example registers `Chamber1` and `Supply1`, injects a first-order sensor and temperature-dependent current model, advances a shared virtual clock, and prints the registered roles and their readings. The current `StartSimulated()` path does not discover physical hosts or acquire hardware leases. Read the [experiment composition tutorial](EXPERIMENT-COMPOSITION.md) for C# registration and the [Git/version-control tutorial](VERSION-CONTROL-TUTORIAL.md) for managing revisions and mock package compatibility.

## Instrument status dashboard

Start `dotnet run --project src/BenchDouble.Host.Server/BenchDouble.Host.Server.csproj` (or `make demo-dashboard`) and open **http://127.0.0.1:5278/dashboard/**. The page shows the local host's registered simulated DMM and generator, whether each is reserved, and its recent activity log. It refreshes automatically and never sends instrument commands. The host must be running; opening the HTML file alone will not provide live data. **Do not expose this prototype server beyond loopback or attach hazardous equipment.** For details see [Instrument Dashboard](INSTRUMENT-DASHBOARD.md).

## Version control for experiment authors

Read [Version Control for Laboratory Automation](VERSION-CONTROL-TUTORIAL.md). It covers Git clone/branch/commit/pull requests, tags, semantic versioning, host protocol compatibility, package hashes and experiment replay manifests. Test results should identify the exact code and behavior versions, not just the procedure name.
