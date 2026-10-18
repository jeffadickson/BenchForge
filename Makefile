# BenchForge developer entrypoints. Requires GNU Make on Unix; Windows can
# use scripts/bootstrap.ps1 and dotnet CLI directly without Make.
.DEFAULT_GOAL := setup
DOTNET ?= $(if $(wildcard .tools/dotnet/dotnet),$(CURDIR)/.tools/dotnet/dotnet,dotnet)
PYTHON ?= python3

# Windows local measurements: docs/CHECK-PORT-BENCHMARKS.md. Build native tools
# once per source change; the normal checks invoke cached executables directly.
CHECKS_NATIVE_PATH := .tools/checks/native/BenchForge.Checks$(if $(filter Windows_NT,$(OS)),.exe,)
CHECKS_NATIVE := "$(CURDIR)/$(CHECKS_NATIVE_PATH)" --python "$(PYTHON)"
CHECKS_SOURCES := $(wildcard tools/BenchForge.Checks/*.cs) tools/BenchForge.Checks/BenchForge.Checks.csproj tools/BenchForge.Checks/scoreboard.html scripts/python_ast_inventory.py
ROSLYN_DLL := tools/BenchDouble.DocumentationAudit/bin/Release/net10.0/BenchDouble.DocumentationAudit.dll
ROSLYN_JOBS ?= 8

$(CHECKS_NATIVE_PATH): $(CHECKS_SOURCES)
	$(DOTNET) publish tools/BenchForge.Checks -c Release -p:PublishAot=true -p:IlcOptimizationPreference=Speed -p:ILLinkTreatWarningsAsErrors=true -o .tools/checks/native

$(ROSLYN_DLL): tools/BenchDouble.DocumentationAudit/Program.cs tools/BenchDouble.DocumentationAudit/BenchDouble.DocumentationAudit.csproj
	$(DOTNET) build tools/BenchDouble.DocumentationAudit -c Release

.PHONY: checks-native check-ports
checks-native: $(CHECKS_NATIVE_PATH)

check-ports: $(CHECKS_NATIVE_PATH) $(ROSLYN_DLL)
	$(DOTNET) build tools/BenchForge.Checks -c Release
	$(PYTHON) tests/check_ports/benchmark.py --prepare-baseline 68113cc72de785e5329a3df2bcf3087d0059c294 --prepare-only
	$(PYTHON) tests/check_ports/verify.py


.PHONY: alpha-check demo-alpha import-check setup setup-windows requirements generate build test test-browser verify demo demo-normal demo-thermal demo-experiment demo-automock demo-automock-json demo-typed-meter demo-generated-mock demo-procedure demo-dashboard check-generated definer-check setup-browser scoreboard requirements-check

setup:
	bash scripts/bootstrap.sh

setup-windows:
	pwsh -File scripts/bootstrap.ps1

requirements-check: $(CHECKS_NATIVE_PATH)
	$(CHECKS_NATIVE) requirements --jobs 8
	$(PYTHON) -m unittest discover -s tests/requirements -v

scoreboard: $(CHECKS_NATIVE_PATH)
	$(CHECKS_NATIVE) scoreboard --jobs 1

setup-browser:
	bash scripts/bootstrap.sh --browser

requirements: requirements-check scoreboard
	.tools/requirements-venv/bin/strictdoc export requirements --output-dir build/requirements

generate:
	$(PYTHON) scripts/check_instrument_catalog.py
	$(DOTNET) run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- instrument-definitions src/BenchDouble/Generated

# Discover projects so new libraries/examples/tools participate automatically.
build: generate build-generated
	@set -e; for project in src/*/*.csproj examples/*/*.csproj tools/*/*.csproj; do \
		[ ! -f "$$project" ] || $(DOTNET) build "$$project"; \
	done

test: generate generate-v2-preview
	@set -e; for project in tests/*/*.csproj; do \
		case "$$project" in *BenchDouble.Browser.Tests*) continue ;; esac; \
		[ ! -f "$$project" ] || $(DOTNET) test "$$project"; \
	done

test-browser:
	$(DOTNET) build tests/BenchDouble.Browser.Tests/BenchDouble.Browser.Tests.csproj
	$(DOTNET) run --project tools/BenchDouble.BrowserSetup -- install chromium
	$(DOTNET) test tests/BenchDouble.Browser.Tests/BenchDouble.Browser.Tests.csproj --no-build

verify: requirements build test docs artifact-recovery

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


# The Python reference is copied into the same DocFX site.
.PHONY: docs docs-check
docs-check: $(ROSLYN_DLL)
	$(DOTNET) $(ROSLYN_DLL) . build/api-audit.json --jobs $(ROSLYN_JOBS)
	$(PYTHON) scripts/check_api_docs.py
	$(PYTHON) -m unittest discover -s tests/documentation -v

docs: check-generated docs-check
	.tools/docs-venv/bin/python -m sphinx -W --keep-going -b html docs/python build/python
	$(DOTNET) tool run docfx docfx.json

.PHONY: demo-capture artifact-check artifact-recovery
demo-capture:
	$(DOTNET) run --project examples/BenchDouble.CaptureDemo

artifact-check: artifact-recovery
	$(DOTNET) test tests/BenchDouble.Artifacts.Tests

artifact-recovery:
	$(DOTNET) build tools/BenchDouble.ArtifactCrashProbe
	$(PYTHON) scripts/check_artifact_recovery.py --dotnet "$(DOTNET)"

# Focused .NET 10 alpha verification, without expanding distributed-host scope.
import-check:
	$(DOTNET) test tests/BenchDouble.Tests/BenchDouble.Tests.csproj --configuration Release --filter "FullyQualifiedName~ImportedInstrumentDefinitionTests|FullyQualifiedName~InstrumentImportPipelineTests"

demo-alpha:
	$(DOTNET) run --project examples/BenchForge.AlphaDemo/BenchForge.AlphaDemo.csproj --configuration Release

alpha-check: check-generated import-check demo-alpha
	$(DOTNET) test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj --configuration Release

# Full external source inventory from a pinned local checkout.
.PHONY: instrument-source-inventory
INSTRUMENT_SOURCE_ROOT ?= external/instrument-source
instrument-source-inventory:
	$(PYTHON) tools/InstrumentDefiner/discover_catalog.py --source-root $(INSTRUMENT_SOURCE_ROOT) --manifest build/instrument-all-classes.csv --inventory build/instrument-source-inventory.json

# Normalize imported descriptor descriptions without silently accepting inferred units.
.PHONY: instrument-normalize
INSTRUMENT_DESCRIPTORS ?= build/instrument-descriptors.csv
instrument-normalize:
	$(PYTHON) tools/InstrumentDefiner/normalize_descriptions.py $(INSTRUMENT_DESCRIPTORS) build/instrument-descriptors-normalized.csv

# Schema-v2 simulation-only emitter. Never enables physical I/O from unreviewed data.
.PHONY: generate-v2-preview check-v2-preview
V2_PREVIEW_DIR ?= generated/BenchForge.Instruments/Code
generate-v2-preview:
	$(DOTNET) run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- --preview-v2 instrument-definitions/drafts $(V2_PREVIEW_DIR)

check-v2-preview:
	$(DOTNET) run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- --preview-v2 instrument-definitions/drafts $(V2_PREVIEW_DIR) --check

# Dedicated disposable schema-v2 generated C# project.
.PHONY: build-generated clean-generated
build-generated: generate-v2-preview
	$(DOTNET) build generated/BenchForge.Instruments/BenchForge.Instruments.csproj --configuration Release

clean-generated:
	$(PYTHON) scripts/clean_generated.py

# Shared-signal stateful mock integration tests (source, meter, oscilloscope).
.PHONY: test-virtual-bench
test-virtual-bench:
	$(DOTNET) test tests/BenchDouble.Tests/BenchDouble.Tests.csproj --configuration Release --filter "FullyQualifiedName~VirtualSignalBusTests|FullyQualifiedName~VirtualBenchTests"

# Full tracked-file source-neutrality audit and XML API documentation verification.
.PHONY: source-audit source-audit-strict xdoc-check
source-audit: $(CHECKS_NATIVE_PATH)
	$(CHECKS_NATIVE) neutrality --jobs 8 --output build/source-neutrality-audit.json

source-audit-strict: $(CHECKS_NATIVE_PATH)
	$(CHECKS_NATIVE) neutrality --jobs 8 --strict --output build/source-neutrality-audit.json

xdoc-check: docs-check
	$(DOTNET) build generated/BenchForge.Instruments/BenchForge.Instruments.csproj --configuration Release -p:GenerateDocumentationFile=true

# Run the measured native preflight; Python remains available without a build toolchain.
# Roslyn remains the authoritative CI checker for complete C# syntax coverage.
.PHONY: docs-preflight docs-preflight-tests
docs-preflight: $(CHECKS_NATIVE_PATH)
	$(CHECKS_NATIVE) preflight --jobs 8 --output build/docs-preflight.json

docs-preflight-tests:
	$(PYTHON) -m unittest discover -s tests/documentation -p 'test_docs_portable.py' -v

.PHONY: docs-compare
docs-compare: $(CHECKS_NATIVE_PATH)
	$(CHECKS_NATIVE) compare --jobs 8 --roslyn build/api-audit.json --portable build/docs-preflight.json --output build/docs-parity.json
