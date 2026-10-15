# BenchDouble developer entrypoints. Requires GNU Make on Unix; Windows can
# use scripts/bootstrap.ps1 and dotnet CLI directly without Make.
DOTNET ?= dotnet
PYTHON ?= python3

.PHONY: setup setup-windows requirements generate build test test-browser verify demo demo-normal demo-thermal demo-experiment demo-automock demo-automock-json demo-typed-meter demo-procedure demo-dashboard check-generated

setup:
	bash scripts/bootstrap.sh

setup-windows:
	pwsh -File scripts/bootstrap.ps1

requirements:
	$(PYTHON) scripts/check_requirements.py
	.tools/requirements-venv/bin/strictdoc export requirements --output-dir build/requirements

generate:
	$(PYTHON) scripts/check_instrument_catalog.py
	$(DOTNET) run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- instrument-definitions src/BenchDouble/Generated

build: generate
	$(DOTNET) build src/BenchDouble/BenchDouble.csproj
	$(DOTNET) build src/BenchDouble.Experiments/BenchDouble.Experiments.csproj
	$(DOTNET) build src/BenchDouble.Procedures/BenchDouble.Procedures.csproj
	$(DOTNET) build examples/BenchDouble.ProcedureDemo/BenchDouble.ProcedureDemo.csproj
	$(DOTNET) build examples/BenchDouble.ExperimentDemo/BenchDouble.ExperimentDemo.csproj
	$(DOTNET) build examples/BenchDouble.TypedMockDemo/BenchDouble.TypedMockDemo.csproj
	$(DOTNET) build src/BenchDouble.TimeSeries.Sqlite/BenchDouble.TimeSeries.Sqlite.csproj
	$(DOTNET) build src/BenchDouble.Plotting/BenchDouble.Plotting.csproj
	$(DOTNET) build src/BenchDouble.Plotting.ScottPlot/BenchDouble.Plotting.ScottPlot.csproj
	$(DOTNET) build examples/BenchDouble.DiDemo/BenchDouble.DiDemo.csproj
	$(DOTNET) build examples/BenchDouble.ThermalCycleDemo/BenchDouble.ThermalCycleDemo.csproj
	$(DOTNET) build src/BenchDouble.Host.Server/BenchDouble.Host.Server.csproj
	$(DOTNET) build src/BenchDouble.Ivi.Official/BenchDouble.Ivi.Official.csproj

test: generate
	$(DOTNET) test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
	$(DOTNET) test tests/BenchDouble.Experiments.Tests/BenchDouble.Experiments.Tests.csproj
	$(DOTNET) test tests/BenchDouble.Procedures.Tests/BenchDouble.Procedures.Tests.csproj
	$(DOTNET) test tests/BenchDouble.Acceptance.Tests/BenchDouble.Acceptance.Tests.csproj
	$(DOTNET) test tests/BenchDouble.TimeSeries.Sqlite.Tests/BenchDouble.TimeSeries.Sqlite.Tests.csproj
	$(DOTNET) test tests/UnitsNet.Plotting.Tests/UnitsNet.Plotting.Tests.csproj
	$(DOTNET) test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj
	$(DOTNET) test tests/BenchDouble.Ivi.Official.Tests/BenchDouble.Ivi.Official.Tests.csproj

test-browser:
	$(DOTNET) build tests/BenchDouble.Browser.Tests/BenchDouble.Browser.Tests.csproj
	pwsh tests/BenchDouble.Browser.Tests/bin/Debug/net8.0/playwright.ps1 install chromium
	$(DOTNET) test tests/BenchDouble.Browser.Tests/BenchDouble.Browser.Tests.csproj --no-build

verify: requirements build test

check-generated:
	$(PYTHON) scripts/check_instrument_catalog.py
	$(DOTNET) run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- instrument-definitions src/BenchDouble/Generated --check

demo:
	$(DOTNET) run --project examples/BenchDouble.DiDemo/BenchDouble.DiDemo.csproj

demo-normal:
	$(DOTNET) run --project examples/BenchDouble.DiDemo/BenchDouble.DiDemo.csproj -- --normal

demo-thermal:
	$(DOTNET) run --project examples/BenchDouble.ThermalCycleDemo/BenchDouble.ThermalCycleDemo.csproj

demo-experiment:
	$(DOTNET) run --project examples/BenchDouble.ExperimentDemo/BenchDouble.ExperimentDemo.csproj

demo-dashboard:
	@echo "Open http://127.0.0.1:5278/dashboard/ after the server starts"
	$(DOTNET) run --project src/BenchDouble.Host.Server/BenchDouble.Host.Server.csproj

demo-automock:
	$(DOTNET) run --project examples/BenchDouble.ExperimentDemo/BenchDouble.ExperimentDemo.csproj -- --automock

demo-automock-json:
	$(DOTNET) run --project examples/BenchDouble.ExperimentDemo/BenchDouble.ExperimentDemo.csproj -- --automock-json

demo-typed-meter:
	$(DOTNET) run --project examples/BenchDouble.TypedMockDemo/BenchDouble.TypedMockDemo.csproj

demo-procedure:
	$(DOTNET) run --project examples/BenchDouble.ProcedureDemo/BenchDouble.ProcedureDemo.csproj
