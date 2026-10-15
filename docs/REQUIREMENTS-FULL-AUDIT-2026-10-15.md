# Complete baseline and traceability audit — revision 2

Audit input: BenchDouble commit `e219b3eaf2f4be42e55bbf451f3ae759e18555df`.
Reviewed the entire 175-statement baseline, canonical matrix, original 90-row audit,
repository tree, linked source/test artifacts, design documentation and all 54
existing GitHub issues. Added 30 requirements, giving **205 tracked requirements**.
[Full row inventory](../requirements/full-audit.csv) includes a statement fingerprint,
status, source/test paths, work item, residual finding and acceptance field for every ID.

Scope is requirements/traceability and source-evidence review. It is not a full
runtime, security, hardware or line-by-line code audit. Existing TR findings are
retained, with current runner corrections noted below. Many historical acceptance
criteria remain broad; their audit rows explicitly require the complete statement
and recorded gaps, not merely the existence of one test file.

## Findings and corrections

| Finding | Action / tracking |
|---|---|
| Last turn's identification code had no dedicated canonical IDs | Added BD-ID and BD-OWN requirements; #54 remains open. |
| Lifecycle/heartbeat/command behavior was only partly covered by broad TR requirements | Added explicit acceptance and fault cases; #55–58. |
| Report said runner had five states and no bounded cleanup | Corrected: six enum states, bounded independent cleanup and RecoveryRequired latch exist. Full lifecycle and durable physical recovery do not. #47/#55. |
| README mixed old counts; coverage still said Python tests had not run | Replaced counts with current coverage and qualified previous 12-test local evidence. |
| BD-REQ-004, BD-AM-011/015, BD-DEF-008 remained Planned despite partial source | Corrected to source-linked unverified/partial status without claiming acceptance. |
| Gate accepted Verified with only a test filename | Requires versioned execution evidence, commit/platform/command/result/requirement mapping; actual evidence still needs review. #31. |
| Only historical TR items had an exhaustive audit inventory | Added full-audit.csv and gate checks for every ID, matching fields, criteria and statement fingerprints. |
| Dashboard retains old cards/counts after failed refresh | BD-DASH-003 remains unverified; stale rendering/browser tests tracked in #36. |
| Instrument validator is not full JSON Schema validation | BD-DEF-004 remains unverified; malformed nested shapes and review evidence tracked in #52. |
| Demo host release removes a lease without physical cleanup | Local simulator prototype only; #46/#55 must integrate cleanup and quarantine before physical use. |
| Reader identifies only demo-host synchronous opens; logger retention depends on sinks | BD-ID-001 is partial; durable identity, other backends and reconnect remain planned. #54. |
| No heartbeat/renewal/command journal in host/client paths | #56/#57, not inferred from HTTP transport success. |
| Setup/CI installs unpinned StrictDoc | Existing reproducibility gap remains #28. |

## Subsystem disposition

Original procedure/editor/signature requirements remain tracked and deferred, not
deleted. Instrument core, simulations, UnitsNet, SQLite, plotting, DI, rules, DSL,
generation and Definer have source evidence of varying completeness. Remote
security, durable recovery, immutable signed evidence, central replication and
integrated browser execution remain incomplete. Issue links were checked against
the fetched issue inventory: all original referenced work items exist and are open.
New tickets #55–58 provide acceptance without duplicating the older umbrella work.

No requirement is promoted to Verified. Current counts are generated from the
matrix in [COVERAGE.md](../requirements/COVERAGE.md). Tracker tests demonstrate
structural regression detection, not product behavior. StrictDoc parser/export
and .NET/hardware tests remain separate execution obligations.

Local checks executed for this revision:

- `python scripts/check_requirements.py`: passed, 205 requirements, zero Verified,
  111 source-linked unverified and 90 historical TR assessments synchronized.
- `python -m unittest discover -s tests/requirements -v`: 7/7 passed, including
  deliberate missing/duplicate audit rows, stale requirement text, missing issue,
  missing acceptance and a false Verified claim.

Neither StrictDoc export nor C# compilation was executed in this increment.

## Development discipline

AGENTS.md now requires requirements, traceability, issue and audit updates in the
same change as each accepted feature. The gate catches missing IDs, evidence,
criteria and stale statement fingerprints. It cannot infer an unrecorded request
from conversation or prove code semantics; developer review must enforce that part.

[Reliability architecture](RELIABILITY-ARCHITECTURE.md) and
[PyMeasure source comparison](PYMEASURE-REMOTE-RELIABILITY-REVIEW.md) establish the
implementation order. No graphical expansion or runtime reliability implementation
is claimed by this requirements-only increment.
