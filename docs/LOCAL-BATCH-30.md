# Thirty additional local iterations

Tests run only at iterations 10, 20 and 30, as requested. Each checkpoint runs the core, host and experiment projects, tracker tests and documentation checks. Earlier commits in a batch await that checkpoint. Static requirement consistency is checked before each commit. No requirement is promoted to Verified.

The first checkpoint-30 host run exposed scheduling-sensitive shutdown tests. Iteration 30 replaced self-expiring callback gates with explicit release, isolated blocking initialization from the worker pool, and widened coordination budgets while retaining cleanup, ownership and deadline assertions. A subsequent failure-injection case also needed a larger scheduling budget to observe its provider outcome before timeout. The initial failure evidence is retained with the final checkpoint results.

| Iteration | Change | Requirement | Execution status when committed |
|---|---|---|---|
| 1 | Isolate stateful mock diagnostic sinks | TR-076 | Pending checkpoint 10 |
| 2 | Redact stateful mock fault payloads | TR-076 | Pending checkpoint 10 |
| 3 | Honor cancellation after asynchronous mock models | BD-SIM-001 | Pending checkpoint 10 |
| 4 | Track consumed mock faults in state revisions | BD-SIM-001 | Pending checkpoint 10 |
| 5 | Add explicit queued mock fault clearing | BD-SIM-001 | Pending checkpoint 10 |
| 6 | Keep absent model removal idempotent | BD-SIM-001 | Pending checkpoint 10 |
| 7 | Avoid revisions for identical mock overrides | BD-SIM-001 | Pending checkpoint 10 |
| 8 | Bound pending mock fault queues | BD-SIM-001 | Pending checkpoint 10 |
| 9 | Prevent overflow in finite virtual ramps | BD-SIM-001 | Pending checkpoint 10 |
| 10 | Freeze imported capability mode metadata | BD-SIM-001 | Checkpoint passed |
| 11 | Require complete imported identifiers | BD-SIM-001 | Pending checkpoint 20 |
| 12 | Bound imported definition size and depth | BD-SIM-001 | Pending checkpoint 20 |
| 13 | Reject duplicate imported JSON fields | BD-SIM-001 | Pending checkpoint 20 |
| 14 | Reject ambiguous imported mode commands | BD-SIM-001 | Pending checkpoint 20 |
| 15 | Reject overlapping imported query bindings | BD-SIM-001 | Pending checkpoint 20 |
| 16 | Reject repeated capability mode references | BD-SIM-001 | Pending checkpoint 20 |
| 17 | Validate imported mock default types | BD-SIM-001 | Pending checkpoint 20 |
| 18 | Reject duplicate AutoMock JSON keys | BD-SIM-001 | Pending checkpoint 20 |
| 19 | Require explicit AutoMock action values | BD-SIM-001 | Pending checkpoint 20 |
| 20 | Require symbolic AutoMock action names | BD-SIM-001 | Checkpoint passed |
| 21 | Bound programmatic AutoMock rule sets | BD-SIM-001 | Pending checkpoint 30 |
| 22 | Validate null rule entries consistently | BD-SIM-001 | Pending checkpoint 30 |
| 23 | Align rule fingerprint validation with execution | BD-SIM-001 | Pending checkpoint 30 |
| 24 | Canonicalize signed zeros in rule provenance | BD-SIM-001 | Pending checkpoint 30 |
| 25 | Separate owner renewal scheduling from HTTP timeouts | BD-HB-002 | Pending checkpoint 30 |
| 26 | Validate initial owner-health ages and expiry budgets | BD-HB-002 | Pending checkpoint 30 |
| 27 | Reject inconsistent owner heartbeat acknowledgments | BD-HB-003 | Pending checkpoint 30 |
| 28 | Renew scope ownership immediately on start | BD-HB-002 | Pending checkpoint 30 |
| 29 | Validate discovery numeric address components | BD-CFG-008 | Pending checkpoint 30 |
| 30 | Prevent AutoMock dependency replacement | BD-EXP-003 | Checkpoint passed |

All pending statuses in the historical column above are resolved by the passing checkpoints at iterations 10, 20 and 30. Each checkpoint includes the complete core, host and experiment test projects, all nine tracker tests, API documentation audit, documentation gate and all ten documentation tests. Tests were not run between checkpoints.

Physical driver activation, authenticated multi-host discovery, durable remote log replay and hardware/cross-platform acceptance remain incomplete. The 228 requirement statuses are retained, with no Verified promotion.
