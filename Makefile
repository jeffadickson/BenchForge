# BenchForge developer entrypoints. Requires GNU Make on Unix; Windows can
# use scripts/bootstrap.ps1 and dotnet CLI directly without Make.
.DEFAULT_GOAL := setup
DOTNET ?= $(if $(wildcard .tools/dotnet/dotnet),$(CURDIR)/.tools/dotnet/dotnet,dotnet)
PYTHON ?= python3
PWSH ?= $(if $(filter Windows_NT,$(OS)),powershell,pwsh)
CHECKS_ARCH ?= auto
CHECKS_CONFIGURATION ?= Release
CHECKS_RID ?=
CHECK_HOST = $(PWSH) -NoLogo -NoProfile -NonInteractive -File scripts/checks.ps1 -Python "$(PYTHON)" -Dotnet "$(DOTNET)" -Architecture "$(CHECKS_ARCH)" -Configuration "$(CHECKS_CONFIGURATION)"

# Windows local measurements: docs/CHECK-PORT-BENCHMARKS.md. Build native tools
# only for optional microbenchmarks; normal checks use the validated managed host.
CHECKS_NATIVE_PATH := .tools/checks/native/$(CHECKS_RID)/$(CHECKS_CONFIGURATION)/BenchForge.Checks$(if $(filter Windows_NT,$(OS)),.exe,)
CHECKS_NATIVE := "$(CURDIR)/$(CHECKS_NATIVE_PATH)" --python "$(PYTHON)"
CHECKS_SOURCES := $(wildcard tools/BenchForge.Checks/*.cs) tools/BenchForge.Checks/BenchForge.Checks.csproj tools/BenchForge.Checks/scoreboard.html scripts/python_ast_inventory.py
ROSLYN_DLL := .tools/roslyn/$(CHECKS_RID)/$(CHECKS_CONFIGURATION)/BenchDouble.DocumentationAudit.dll
ROSLYN_JOBS ?= 8

$(CHECKS_NATIVE_PATH): $(CHECKS_SOURCES)
	$(if $(CHECKS_RID),,@$(error Set CHECKS_RID explicitly, for example win-x64 or linux-arm64))
	$(DOTNET) publish tools/BenchForge.Checks -c $(CHECKS_CONFIGURATION) -r $(CHECKS_RID) -p:PublishAot=true -p:IlcOptimizationPreference=Speed -p:ILLinkTreatWarningsAsErrors=true -o .tools/checks/native/$(CHECKS_RID)/$(CHECKS_CONFIGURATION)

$(ROSLYN_DLL): tools/BenchDouble.DocumentationAudit/Program.cs tools/BenchDouble.DocumentationAudit/RoslynAudit.cs tools/BenchDouble.DocumentationAudit/BenchDouble.DocumentationAudit.csproj
	$(if $(CHECKS_RID),,@$(error Set CHECKS_RID explicitly for the optional standalone Roslyn build))
	$(DOTNET) build tools/BenchDouble.DocumentationAudit -c $(CHECKS_CONFIGURATION) -r $(CHECKS_RID) -o .tools/roslyn/$(CHECKS_RID)/$(CHECKS_CONFIGURATION) --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false

.PHONY: checks-native check-ports checks
# Full check/report workflow: one managed build, content-validated cache, parallel checks.
checks:
	$(CHECK_HOST)

.PHONY: checks-prepare checks-plan checks-only
checks-prepare:
	$(CHECK_HOST) -PrepareOnly

checks-plan:
	$(CHECK_HOST) -PlanOnly

checks-only:
	$(CHECK_HOST) -Command checks-only

.PHONY: checks-native check-ports
checks-native: $(CHECKS_NATIVE_PATH)

check-ports: $(CHECKS_NATIVE_PATH) $(ROSLYN_DLL)
	$(DOTNET) build tools/BenchForge.Checks -c Release
	$(PYTHON) tests/check_ports/benchmark.py --prepare-baseline 68113cc72de785e5329a3df2bcf3087d0059c294 --prepare-only
	BENCHFORGE_CHECKS_NATIVE="$(CURDIR)/$(CHECKS_NATIVE_PATH)" BENCHFORGE_ROSLYN_DLL="$(CURDIR)/$(ROSLYN_DLL)" $(PYTHON) tests/check_ports/verify.py


.PHONY: alpha-check demo-alpha import-check setup setup-windows requirements generate build test test-browser verify demo demo-normal demo-thermal demo-experiment demo-automock demo-automock-json demo-typed-meter demo-generated-mock demo-procedure demo-dashboard check-generated definer-check setup-browser scoreboard requirements-check

setup:
	bash scripts/bootstrap.sh

setup-windows:
	pwsh -File scripts/bootstrap.ps1

requirements-check:
	$(CHECK_HOST) -Command requirements
	$(PYTHON) -m unittest discover -s tests/requirements -v

scoreboard:
	$(CHECK_HOST) -Command scoreboard

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
docs-check:
	$(CHECK_HOST) -Command roslyn
	$(CHECK_HOST) -Command docs
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
source-audit:
	$(CHECK_HOST) -Command neutrality

source-audit-strict:
	$(CHECK_HOST) -Command neutrality-strict

xdoc-check: docs-check
	$(DOTNET) build generated/BenchForge.Instruments/BenchForge.Instruments.csproj --configuration Release -p:GenerateDocumentationFile=true

# Run the measured native preflight; Python remains available without a build toolchain.
# Roslyn remains the authoritative CI checker for complete C# syntax coverage.
.PHONY: docs-preflight docs-preflight-tests
docs-preflight:
	$(CHECK_HOST) -Command preflight

docs-preflight-tests:
	$(PYTHON) -m unittest discover -s tests/documentation -p 'test_docs_portable.py' -v

.PHONY: docs-compare
docs-compare:
	$(CHECK_HOST) -Command compare
