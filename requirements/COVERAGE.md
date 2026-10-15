# BenchDouble Requirements Coverage — Evidence Audit

**Date:** 2026-10-15 · **Total:** 167 requirements · **Verified:** 0

| Status | Count | Meaning |
|---|---:|---|
| PartialSourceUnverified | 45 | Legacy runner requirement has relevant source but incomplete or unverified acceptance |
| DesignOnly | 6 | Legacy requirement has design documentation only |
| NoImplementationEvidence | 39 | No direct implementation evidence found for legacy requirement |
| SourceCommittedUnverified | 53 | Newer requirement has linked source/tests but no reviewed passing CI evidence |
| Planned | 24 | Requirement awaits implementation |
| Verified | **0** | Reviewed passing requirement-level verification |
| **Total** | **167** | All requirements have GitHub task links |

The original 90 runner requirements retain their individually audited findings (45 partial-source, 6 design-only, 39 without direct implementation evidence) in [legacy-runner-audit.csv](legacy-runner-audit.csv).

New **BD-GEN-001..006** requirements track generation of typed SCPI driver/mock pairs, unit-aware default values, per-channel state, catalog evidence, generated-source drift checks and Keithley 236 mode/bias/compliance safety. Source and tests are committed, but execution remains unverified while GitHub Actions runner allocation is blocked. The 25 PyMeasure entries in the [instrument catalog](../instrument-definitions/instrument-catalog.json) are an **inventory, not 25 completed drivers**.

[Traceability matrix](traceability.csv) · [Instrument definition and mock tutorial](../docs/INSTRUMENT-DEFINITIONS-AND-MOCK-GENERATION.md) · [Original runner audit](../docs/REQUIREMENTS-AUDIT-2026-10-15.md)
