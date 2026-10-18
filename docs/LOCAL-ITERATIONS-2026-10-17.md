# Local development iterations — 2026-10-17

Windows / .NET SDK 10.0.402. Each row records a completed change followed by the full indicated test project, the requirement gate, and tracker tests. No remote pushes. These are local regression results, not full requirement or hardware verification.

| Iteration | Improvement | Requirement | Test project | Result |
|---|---|---|---|---|
| 1 | Enable local .NET tests and release failed ledger locks | BD-CFG-006 | BenchDouble.Host.Tests | Passed |
| 2 | Validate scope heartbeat routes and periods | BD-HB-002 | BenchDouble.Host.Tests | Passed |
| 3 | Make scope heartbeat start and stop race safe | BD-HB-002 | BenchDouble.Host.Tests | Passed |
| 4 | Stop scope renewal on terminal host rejections | BD-HB-003 | BenchDouble.Host.Tests | Passed |
| 5 | Isolate every scope heartbeat observer | BD-HB-002 | BenchDouble.Host.Tests | Passed |
| 6 | Bound each scope heartbeat HTTP renewal | BD-HB-002 | BenchDouble.Host.Tests | Passed |
| 7 | Isolate SCPI logging sink failures | TR-076 | BenchDouble.Tests | Passed |
| 8 | Fail closed when SCPI payload redactors fail | TR-076 | BenchDouble.Tests | Passed |
| 9 | Exclude vendor exception payloads from SCPI logs | TR-076 | BenchDouble.Tests | Passed |
| 10 | Reject routing control characters and repair short-period heartbeat timeouts | BD-CFG-003 | BenchDouble.Host.Tests | Passed |
| 11 | Normalize overflowing routing schema errors | BD-CFG-003 | BenchDouble.Host.Tests | Passed |
| 12 | Isolate routing logs and stabilize late-cleanup regression | BD-CFG-002 | BenchDouble.Host.Tests | Passed |
| 13 | Validate discovery hostnames and isolate deadline-sensitive tests | BD-CFG-008 | BenchDouble.Host.Tests | Passed |
| 14 | Reject incomplete VISA endpoint descriptors | BD-CFG-008 | BenchDouble.Host.Tests | Passed |
