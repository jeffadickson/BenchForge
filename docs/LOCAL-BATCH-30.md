# Thirty additional local iterations

Tests run only at iterations 10, 20 and 30, as requested. Each checkpoint runs the core, host and experiment projects, tracker tests and documentation checks. Earlier commits in a batch await that checkpoint. Static requirement consistency is checked before each commit. No requirement is promoted to Verified.

| Iteration | Change | Requirement | Execution status when committed |
|---|---|---|---|
| 1 | Isolate stateful mock diagnostic sinks | TR-076 | Pending checkpoint 10 |
| 2 | Redact stateful mock fault payloads | TR-076 | Pending checkpoint 10 |
| 3 | Honor cancellation after asynchronous mock models | BD-SIM-001 | Pending checkpoint 10 |
| 4 | Track consumed mock faults in state revisions | BD-SIM-001 | Pending checkpoint 10 |
