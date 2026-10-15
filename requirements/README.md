# Requirements management

The authoritative machine-readable baseline is [benchdouble.sdoc](benchdouble.sdoc) (StrictDoc). It imports all **90** consolidated test-runner requirements from `docs/TEST-LAB-RUNNER-REQUIREMENTS.md` and adds **32** cross-subsystem requirements for simulation, unattended monitoring, time-series persistence, testing, documentation and cross-platform builds. The companion [traceability.csv](traceability.csv) maps IDs to source, tests, GitHub issues and evidence status.

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

The [feature audit](../docs/FEATURE-AUDIT.md) records subsystem coverage and gaps; the [testing guide](../docs/TESTING-AND-TRACEABILITY.md) explains xUnit, Reqnroll and Playwright evidence. All 138 requirements are currently unverified or planned; no requirement is marked Verified without actual successful test evidence.
