# BenchDouble — 90-Requirement Runner Audit

**Audit date:** 2026-10-15  
**Scope:** TR-001 through TR-090 in `docs/TEST-LAB-RUNNER-REQUIREMENTS.md`  
**Evidence:** default-branch GitHub repository tree (210 entries), relevant implementation and xUnit source, existing design documents, and GitHub Actions run metadata.  
**Outcome:** source-evidence audit completed; **no requirement has been verified by a passing test run**.

## Method and status semantics

Each requirement was compared to actual repository source and test artifacts. Its row in [legacy-runner-audit.csv](../requirements/legacy-runner-audit.csv) records priority, implementation assessment, concrete source/test evidence, the unmet portion, and a GitHub task. The canonical [traceability.csv](../requirements/traceability.csv) uses the same assessment and references.

- **PartialSourceUnverified (45):** directly relevant implementation source exists, but the entire acceptance criterion is not demonstrated and/or test execution has not passed.
- **DesignOnly (6):** explicit architecture or policy is documented, but implementation evidence was not found.
- **NoImplementationEvidence (39):** no direct implementation evidence was identified in the reviewed repository sources. This does **not** prove no relevant code exists anywhere; follow-up implementation and verification remain necessary.
- **Verified (0):** would require an executed, passing requirement-level verification and reviewed evidence.

These are **audit evidence categories, not percentages of functional completeness**.

## Results by subsystem

| Area | Requirements | Partial source | Design only | No implementation evidence |
|---|---:|---:|---:|---:|
| Structured procedure editor | 14 | 2 | 2 | 10 |
| Runner lifecycle/concurrency | 14 | 9 | 0 | 5 |
| Distributed instrument hosts | 12 | 10 | 1 | 1 |
| Mocks/simulation | 10 | 7 | 1 | 2 |
| Measurements/persistence | 10 | 7 | 0 | 3 |
| Redlines/signatures/review | 8 | 0 | 1 | 7 |
| Browser runner | 7 | 4 | 1 | 2 |
| Logging/security/observability | 6 | 2 | 0 | 4 |
| Verification/acceptance | 9 | 4 | 0 | 5 |
| **Total** | **90** | **45** | **6** | **39** |

## Concrete findings

**Runner:** `ExperimentRunner.cs` has startup/execute/shutdown and basic failure/cancellation handling. However, it exposes only five states and explicitly says bounded cleanup policy is not yet implemented. It does not implement the full required state machine, resource preflight, approved revision pinning, or hardware OutcomeUnknown/RecoveryRequired semantics. See TR-015–019 and [issue #47](https://github.com/jeffadickson/BenchDouble/issues/47).

**Host:** `HostResourceRegistry` has a real local-only exclusive GUID reservation check and sanitized activity log. Its source explicitly states no expiry, authenticated identity, persistence, physical safe-state recovery or remote TLS. Therefore TR-032 and TR-039 cannot be marked complete, regardless of passing local conflict tests. See [issue #46](https://github.com/jeffadickson/BenchDouble/issues/46).

**Mock simulation:** Typed meter modes, seeded signals, timed faults, shared manual clocks and typed virtual wiring have directly relevant source and tests. Strict EasyMock-style cross-device expectations, complete instrument command state models, robust virtual scheduling and deterministic transcript replay remain incomplete. See TR-041–050 and [issue #40](https://github.com/jeffadickson/BenchDouble/issues/40).

**Measurements:** SQLite WAL, append-only samples, typed quantity validation, UTC and virtual-time queries exist in source. Full immutable procedure/run event records, calibration/driver/step provenance, central replication and recovery are not established. See TR-051–060 and [issue #48](https://github.com/jeffadickson/BenchDouble/issues/48).

**Procedure editing, review and signatures:** No working shared structured browser editor/runner, approved procedure revision ledger, attributable signature workflow or post-signature tamper invalidation was found. This is the largest product-level P0 gap. See TR-001–014, TR-061–069 and [issue #45](https://github.com/jeffadickson/BenchDouble/issues/45).

**Browser/logging:** The instrument dashboard and thermal/unattended HTML demonstrations exist, but they do not implement the integrated procedure workspace, cross-host correlated event replay, durable logs or OpenTelemetry propagation. See TR-069–081 and [issue #49](https://github.com/jeffadickson/BenchDouble/issues/49).

## Priority P0 requirements with no implementation evidence

TR-001, TR-002, TR-003, TR-004, TR-006, TR-007, TR-019, TR-039, TR-061, TR-062, TR-063, TR-064, TR-069, TR-079 and TR-080.

These are **15 P0 evidence gaps** requiring implementation or more precise source proof. Other P0 requirements marked PartialSourceUnverified may still have significant missing functionality and are not release-ready.

## CI verification finding

GitHub Actions push run [#38766706928](https://github.com/jeffadickson/BenchDouble/actions/runs/38766706928), created 2026-10-15, reports **failure** for Windows, Ubuntu and macOS jobs. Several adjacent push runs also report failures. Attempts to retrieve full job logs returned unavailable/not found, and the job step summaries were empty. Consequently the **specific failure cause remains unknown**. Do not assume a compiler, StrictDoc, browser, or dependency issue without logs.

[Issue #50](https://github.com/jeffadickson/BenchDouble/issues/50) tracks investigating the actual failure and obtaining successful cross-platform CI evidence.

## Development actions created

- [#45 — Structured procedure revision, editor and signed evidence](https://github.com/jeffadickson/BenchDouble/issues/45)
- [#46 — Secure host identity, leases and fencing](https://github.com/jeffadickson/BenchDouble/issues/46)
- [#47 — Runner lifecycle and safe-state recovery](https://github.com/jeffadickson/BenchDouble/issues/47)
- [#48 — Measurement provenance and offline replay](https://github.com/jeffadickson/BenchDouble/issues/48)
- [#49 — Live browser runner, logs and OpenTelemetry](https://github.com/jeffadickson/BenchDouble/issues/49)
- [#50 — Cross-platform CI failures and verification gates](https://github.com/jeffadickson/BenchDouble/issues/50)

Existing issues remain open for more detailed subsystem implementation.

## Audit automation

`scripts/check_requirements.py` now checks that all 90 TR IDs appear exactly once in the audit, that their assessment/status, source/test evidence paths and issue links agree with the canonical traceability matrix, and that evidence files exist. It does **not** interpret C# or prove acceptance criteria. Actual xUnit, Reqnroll, Playwright and hardware acceptance tests must provide independent verification evidence.

## Recommended execution order

1. Repair CI and obtain a clean cross-platform hardware-free baseline (#50).
2. Complete safe runner lifecycle and secure host resource ownership (#47, #46).
3. Implement structured procedure definition/editor with immutable execution evidence and signatures (#45).
4. Connect the C# runner to live browser/logging and durable provenance (#49, #48).
5. Execute the complete six-step procedure and multi-host end-to-end acceptance tests before moving P0 requirements to Verified.

**Bottom line:** BenchDouble has meaningful instrument, mock, experiment and storage prototypes, but the original test-lab runner specification is not yet a verified integrated product. The audit makes the gaps visible and actionable rather than treating committed source as completion.
