# Local development batch: 150 requested iterations

Started from b296bb852699e8d526179a3290312acafcb99bda, including the uncommitted combined-workflow work inherited from the interrupted measurement task. Tests run only at iterations 50, 100 and 150; local commits at each multiple of 25. No application tests or native AOT benchmarks are included. Later regression-development iterations add explicit contract cases; adding a case is not a passing execution claim.

| Iteration | Change | Execution status |
|---|---|---|
| 1 | Compiled per-check command dispatch | Pending checkpoint 50 |
| 2 | Compiled documentation gate in combined workflow | Pending checkpoint 50 |
| 3 | Compiled standalone Roslyn dispatch | Pending checkpoint 50 |
| 4 | Checks-only execution without test suites | Pending checkpoint 50 |
| 5 | Reject unknown and duplicate options | Pending checkpoint 50 |
| 6 | Validate worker count range | Pending checkpoint 50 |
| 7 | Bound regression subprocess duration | Pending checkpoint 50 |
| 8 | Expose compiled runtime identity | Pending checkpoint 50 |
| 9 | Reject runtime architecture mismatch | Pending checkpoint 50 |
| 10 | Attach architecture to timing reports | Pending checkpoint 50 |
| 11 | Architecture build selection | Pending checkpoint 50 |
| 12 | Debug and Release build selection | Pending checkpoint 50 |
| 13 | Supported OS/runtime validation | Pending checkpoint 50 |
| 14 | Partition build caches by RID and configuration | Pending checkpoint 50 |
| 15 | Fingerprint build identity | Pending checkpoint 50 |
| 16 | Fingerprint installed SDK version | Pending checkpoint 50 |
| 17 | Record compiled artifact hashes | Pending checkpoint 50 |
| 18 | Invalidate altered cached artifacts | Pending checkpoint 50 |
| 19 | Centralize owned-path containment | Pending checkpoint 50 |
| 20 | Exclusive cache ownership | Pending checkpoint 50 |
| 21 | Invalidate success marker before rebuild | Pending checkpoint 50 |
| 22 | Atomically replace cache metadata | Pending checkpoint 50 |
| 23 | Use compiled tools in Make entry points | Pending checkpoint 50 |
| 24 | Require explicit RID for native publishing | Pending checkpoint 50 |
| 25 | Document architecture and check cadence | Pending checkpoint 50 |
