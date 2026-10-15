# BenchDouble Requirements Coverage Snapshot

**Date:** 2026-10-15 · **Scope:** 138 machine-readable StrictDoc requirements.

| Traceability status | Count | Meaning |
|---|---:|---|
| Unverified | 90 | Earlier runner requirements pending granular code/test evidence audit |
| SourceCommittedUnverified | 31 | Relevant source/tests exist; no reviewed successful CI evidence |
| Planned | 17 | Requirement not implemented in the tracked scope |
| Verified | 0 | No actual passing CI evidence reviewed in this audit |
| **Total** | **138** | Every requirement has a GitHub issue link |

The existing 90 runner requirements include a mix of prototype source and unfinished features. Their conservative `Unverified` status does not imply none are implemented.

New requirements **BD-EXP-001..005**, **BD-DASH-001..006**, **BD-VERSION-001..004** and **BD-PARITY-001** describe experiment-scoped DI, registered instrument dashboard, versioning and comparative evidence. Issues [#35–#39](https://github.com/jeffadickson/BenchDouble/issues) track outstanding integration, security and verification.

The static source is [traceability.csv](traceability.csv). This is a source audit snapshot, **not** an automated passing-test report. Run `python3 scripts/check_requirements.py` and review CI before changing any requirement to Verified.
