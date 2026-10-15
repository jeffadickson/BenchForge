# Requirements management

The authoritative machine-readable baseline is [benchdouble.sdoc](benchdouble.sdoc) (StrictDoc). It imports all **90** consolidated test-runner requirements from `docs/TEST-LAB-RUNNER-REQUIREMENTS.md` and adds **64** cross-subsystem requirements for simulation, unattended monitoring, time-series persistence, testing, documentation and cross-platform builds. The companion [traceability.csv](traceability.csv) maps IDs to source, tests, GitHub issues and evidence status.

StrictDoc is a requirements **management** tool; Reqnroll is a separate .NET BDD **acceptance testing** framework. GitHub Issues is the work-item tracker. STRIDE is a security threat-modeling method, not the requirements tracker.

## Setup and validation

- macOS/Linux: `bash scripts/bootstrap.sh`
- Windows: `pwsh -File scripts/bootstrap.ps1`
- Fast local check: `python3 scripts/check_requirements.py` (Windows: `py -3 scripts/check_requirements.py`)
- StrictDoc export: `.tools/requirements-venv/bin/strictdoc export requirements --output-dir build/requirements` (Windows: `.tools/requirements-venv/Scripts/strictdoc.exe ...`).
- BDD: `dotnet test tests/BenchDouble.Acceptance.Tests/BenchDouble.Acceptance.Tests.csproj`
- Browser: `dotnet test tests/BenchDouble.Browser.Tests/BenchDouble.Browser.Tests.csproj` after Playwright Chromium installation.

## Status discipline

- `Planned`: no claimed implementation.
- `SourceCommittedUnverified`: code exists but no current successful CI evidence has been checked.
- `Verified`: test file and actual passing CI/run evidence required; do not mark automatically.
- `Blocked`: external dependency or design blocker, linked to an issue.

Never infer verification from a commit, a test name, or a generated document. Requirement IDs are permanent; changes require review and traceability updates. The static checker fails on missing file references, duplicates, untracked planned requirements and unsupported Verified claims.

StrictDoc output is generated documentation, not proof that tests passed. The existing 90 TR items remain indexed in their original source for continuity. 

The [feature audit](../docs/FEATURE-AUDIT.md) records subsystem coverage and gaps; the [testing guide](../docs/TESTING-AND-TRACEABILITY.md) explains xUnit, Reqnroll and Playwright evidence. All 175 requirements are currently unverified or planned; no requirement is marked Verified without actual successful test evidence.

## Original runner audit — completed 2026-10-15

The 90 TR requirements are now **individually audited**, not a blanket Unverified group. [legacy-runner-audit.csv](legacy-runner-audit.csv) records 45 partial-source, 6 design-only and 39 no-direct-implementation-evidence assessments, each with source/test references, remaining gap and a tracked GitHub issue. [Audit report](../docs/REQUIREMENTS-AUDIT-2026-10-15.md) explains the criteria and P0 gaps. The [canonical traceability](traceability.csv) uses the same statuses. The static checker validates all 90 IDs and agreement with the canonical matrix. No status is Verified without reviewed passing test evidence.

## Optional procedure language

BD-DSL-001..007 cover the [Parlot procedure project](../docs/PROCEDURE-DSL.md), mock-only execution, unit-aware assertions and future shared editor/runner representation. See [issue #51](https://github.com/jeffadickson/BenchDouble/issues/51). The current baseline contains **175 requirements**; no new DSL requirement is marked Verified.

## Generated instrument/mock baseline

BD-GEN-001..006 track definition-driven typed SCPI driver/mock generation, per-channel state, strict validation, catalog provenance, source drift detection and Keithley 236 mode safety. The [instrument generator tutorial](../docs/INSTRUMENT-DEFINITIONS-AND-MOCK-GENERATION.md) documents scope and limitations. The baseline now contains **175 requirements**; no new requirement is marked Verified.

## Instrument Definer v2 requirements

BD-DEF-001..008 specify the new [Instrument Definer](../docs/INSTRUMENT-DEFINER.md), including safe PyMeasure AST extraction, CSV handoff, versioned schema, UnitsNet dimension checks, mode-aware mock previews and review-before-generation. Six source-linked requirements remain unverified and two generator/extraction enhancements remain planned. The current baseline contains **175 requirements**.
