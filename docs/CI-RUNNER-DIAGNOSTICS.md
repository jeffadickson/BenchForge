# GitHub Actions runner allocation diagnostic

**Observed:** 2026-10-15. **Status:** blocked before job steps; root cause not yet identified.

## Controlled experiment

A dependency-free workflow, [`.github/workflows/runner-smoke.yml`](../.github/workflows/runner-smoke.yml), was added to test whether GitHub can allocate standard hosted runners. It uses only a built-in shell `echo` step on `ubuntu-latest`, `windows-latest`, and `macos-latest`. It has no checkout, .NET, NuGet, Python, StrictDoc, or third-party actions.

[Runner smoke run #38771580547](https://github.com/jeffadickson/BenchDouble/actions/runs/38771580547) failed on **all three** operating systems with:
- `conclusion = failure`
- `steps = []` (no commands executed)
- `runner_id = 0`
- `runner_name = ""`

The regular [Tests workflow run #38769300188](https://github.com/jeffadickson/BenchDouble/actions/runs/38769300188) showed the same behavior. Its failed jobs were explicitly retried (attempt 2) and failed again before runner allocation.

**This isolates the current CI blocker from the BenchDouble build and tests.** It does not establish the precise account/platform cause.

## Actions already performed in the repository

- Added a minimal independent three-OS runner smoke workflow.
- Enabled `workflow_dispatch` on the main workflow.
- Added workflow concurrency cancellation to avoid overlapping push runs.
- Kept read-only repository permissions for workflow jobs.
- Retried failed jobs in the existing Tests workflow.
- Preserved requirement statuses as unverified until a passing run is available.

## Account-level diagnostic needed

Open the failed runner-smoke job in GitHub and read the red error annotation, which should identify why GitHub cannot start a runner. Review:
1. Repository **Settings → Actions → General** for whether actions and hosted runners are permitted.
2. Account/organization **Billing and licensing** for Actions spending limits, payment method, or usage restrictions on this private repository.
3. GitHub Actions service status and any repository-specific runner policy or plan limitation.

The exact UI labels vary. Do not change billing or enable paid usage without the account owner's approval.

The available GitHub connector can commit code, view workflow/job metadata and retry runs, but cannot read these diagnostic annotation contents or modify billing/runner policy settings.

## Exit criteria

A successful runner-smoke run with at least one executed shell step on each target OS; then a successful full Tests workflow, followed by real test evidence linked to requirements. After runner allocation is restored, compilation, dependency, and test failures may still require independent fixes.

Tracking: [Issue #50](https://github.com/jeffadickson/BenchDouble/issues/50).
