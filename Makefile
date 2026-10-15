# BenchDouble developer entrypoints. Requires GNU Make on Unix; Windows can
# use scripts/bootstrap.ps1 and dotnet CLI directly without Make.
DOTNET ?= $(if $(wildcard .tools/dotnet/dotnet),$(CURDIR)/.tools/dotnet/dotnet,dotnet)
PYTHON ?= python3

.PHONY: setup setup-windows requirements generate build test test-browser verify demo demo-normal demo-thermal demo-experiment demo-automock demo-automock-json demo-typed-meter demo-generated-mock demo-procedure demo-dashboard check-generated definer-check setup-browser scoreboard requirements-check

setup:
	bash scripts/bootstrap.sh

setup-windows:
	pwsh -File scripts/bootstrap.ps1

requirements-check:
	$(PYTHON) scripts/check_requirements.py
	$(PYTHON) -m unittest discover -s tests/requirements -v

scoreboard:
	$(PYTHON) scripts/requirements_scoreboard.py

setup-browser:
	bash scripts/bootstrap.sh --browser

requirements: requirements-check scoreboard
	.tools/requirements-venv/bin/strictdoc export requirements --output-dir build/requirements

generate:
	$(PYTHON) scripts/check_instrument_catalog.py
	$(DOTNET) run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- instrument-definitions src/BenchDouble/Generated

# Discover projects so new libraries/examples/tools participate automatically.
build: generate
	@set -e; for project in src/*/*.csproj examples/*/*.csproj tools/*/*.csproj; do \
		[ ! -f "$$project" ] || $(DOTNET) build "$$project"; \
	done

test: generate
	@set -e; for project in tests/*/*.csproj; do \
		case "$$project" in *BenchDouble.Browser.Tests*) continue ;; esac; \
		[ ! -f "$$project" ] || $(DOTNET) test "$$project"; \
	done

test-browser:
	$(DOTNET) build tests/BenchDouble.Browser.Tests/BenchDouble.Browser.Tests.csproj
	$(DOTNET) run --project tools/BenchDouble.BrowserSetup -- install chromium
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

demo-generated-mock:
	$(DOTNET) run --project examples/BenchDouble.GeneratedMockDemo/BenchDouble.GeneratedMockDemo.csproj

# Pure-Python source extraction tests; does not require GitHub-hosted runners.
definer-check:
	$(PYTHON) -m unittest discover -s tests/instrument_definer -p 'test_*.py' -v
	$(PYTHON) tools/InstrumentDefiner/definer.py validate instrument-definitions/drafts/keithley2400.definition.json
	$(PYTHON) tools/InstrumentDefiner/definer.py validate instrument-definitions/drafts/example-two-mode-dmm.definition.json

