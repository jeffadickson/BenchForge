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
- `requirements/traceability.csv`: 154 requirements, source/test/issue mapping and verification status.
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

## AutoMock: configure a fault without writing another driver

AutoMock is an experiment-level simulation behavior override. Start with the existing thermal example and run:

```bash
dotnet run --project examples/BenchDouble.ExperimentDemo -- --automock
dotnet run --project examples/BenchDouble.ExperimentDemo -- --automock-json
```

The first uses fluent typed C# rules; the second reads `examples/BenchDouble.ExperimentDemo/automock-overcurrent.json` from the repository root. In this demo the power-supply mock normally uses a temperature-dependent load, then reports a fixed 2.5 A current beginning at two simulated hours. The JSON example additionally scales the baseline by 10% at 11–13 V before the fault. The unchanged instrument class is injected with a different `ILoadBehavior`. The console prints the SHA-256 of the configured rule set for traceability. These are **simulated readings**; no hardware current limit or actual electrical safety interlock is exercised. See [AutoMock tutorial](AUTOMOCK-RULES.md) for rule semantics, priority, time windows, validation and testing.

Multiple instruments may use `AutoMockLoadFor("SupplyA", ...)` and `AutoMockLoadFor("SupplyB", ...)` to inject different rules without changing instrument source. Each role has a separate rule fingerprint. See [experiment tests](../tests/BenchDouble.Experiments.Tests/ExperimentCompositionTests.cs) for a two-supply example.

## Generic unit-aware meter AutoMock

Run `make demo-typed-meter` or `dotnet run --project examples/BenchDouble.TypedMockDemo`. The demo registers voltage (250 mV) and resistance (4.7 kΩ, increasing with virtual time), explicitly selects each measurement mode and calls `MeasureAsync<ElectricPotential>()` or `MeasureAsync<ElectricResistance>()`. Requesting voltage while configured for resistance produces an intentional mode-mismatch exception. The experiment demo also includes a named `Meter1`. See [Generic Unit-Aware Mocks](GENERIC-UNIT-AWARE-MOCKS.md). Generated registrations from the complete instrument-definition dataset remain planned.

## Requirement coverage and acceptance status

The [full runner audit](REQUIREMENTS-AUDIT-2026-10-15.md) reviews all 90 original test-lab runner requirements and distinguishes partial source, design-only and missing implementation evidence. The [coverage summary](../requirements/COVERAGE.md) and [per-requirement CSV](../requirements/legacy-runner-audit.csv) are the current reference. **Do not interpret a committed C# test or dashboard demo as a validated procedure execution system.** At the time of this audit no requirement has reviewed passing verification evidence; recent cross-platform CI runs failed. To check the requirement files locally run `python3 scripts/check_requirements.py`. This static command checks traceability consistency, not test execution or hardware safety.

## Experiment runner cleanup and recovery status

`ExperimentRunnerOptions.ShutdownTimeout` sets a finite real-time shutdown wait (default 10 seconds). If the shutdown procedure fails or times out, the result is `RecoveryRequired`, and that runner instance refuses further runs. This is a **warning that physical equipment state may be unknown**, not proof that output was disabled. Operators must follow instrument-specific safe-state and host-lease recovery procedures; those production workflows are still under development. See [experiment runner design](EXPERIMENT-RUNNER.md).

## Text procedures backed by typed instrument mocks

The optional [Parlot procedure tutorial](PROCEDURE-DSL.md) introduces a small human-readable DSL for engineers who prefer editing procedure text. Run `make demo-procedure` or `dotnet run --project examples/BenchDouble.ProcedureDemo` from the repository root. The sample registers a simulated `Meter1`, selects resistance mode, measures 100 Ohm, checks limits, advances the virtual clock five seconds, then measures 105 Ohm. A failed `VERIFY` produces a failed run and a source-line error. The DSL does not connect to physical instruments, execute arbitrary code or replace the graphical procedure editor. Future editor integration and revision control remain in issue #51.

## Generated instrument mocks

Run `make demo-generated-mock` or `dotnet run --project examples/BenchDouble.GeneratedMockDemo`. The example constructs a generated `Keysight33522BMock`, calls `CreateDriver()` to obtain the normal typed C# driver, injects a frequency with `SetMockFrequency(1, Frequency.FromKilohertz(1.5))`, then uses normal `GetFrequencyAsync` and `SetAmplitudeAsync` methods. Channels have separate mock state and unsupported commands are rejected. Use `make generate` after editing instrument definitions, then `make check-generated` to verify checked-in source. The current 33522B command subset is illustrative and not hardware validated. The [instrument catalog](../instrument-definitions/instrument-catalog.json) inventories 25 PyMeasure candidates and a partial Keithley 236; catalog entries are not implemented drivers. See [instrument definition tutorial](INSTRUMENT-DEFINITIONS-AND-MOCK-GENERATION.md).

## Add an instrument with the Instrument Definer

See [Instrument Definer instructions](INSTRUMENT-DEFINER.md). For PyMeasure, use `python3 tools/InstrumentDefiner/definer.py extract-pymeasure path/to/driver.py --manufacturer ... --model ... --revision PINNED_SHA --output instrument-definitions/drafts/model.extracted.json`. For manual extraction, export [properties and methods CSV templates](../tools/InstrumentDefiner/templates/) and use `import-csv`. Run `make definer-check` for example validations. These are **review drafts**, not executable physical drivers. The schema-v2 example DMM can produce a simulation-only UnitsNet mock that refuses voltage reads while in resistance mode. The existing schema-v1 generator remains the only driver generator until v2 commands and modes are reviewed.



## Local-first setup and routing

See [local/remote configuration examples](LOCAL-FIRST-CONFIGURATION.md) for the optional instruments.json file, localhost default, route logging, per-resource reservations and multiple cached observers. See [feature overview](FEATURE-OVERVIEW.md) for current implementation limits. `make setup` restores all project dependencies; `make setup-browser` adds Chromium. `make scoreboard` generates the searchable requirements status page.

## Save scope traces and screenshots

Run `make demo-capture` for a complete synthetic capture example. The optional
local store keeps original bytes and metadata together in SQLite, returns stable
IDs, and links a trace with its screenshot through a shared acquisition ID.
See [Local capture storage](LOCAL-CAPTURE-STORAGE.md) for what is stored, where it
lives, retrieval code, duplicate-save rules and failure handling. Remote upload
is not implemented; local success and a pending outbox do not imply a remote copy.
