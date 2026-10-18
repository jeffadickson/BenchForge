# Local development batch: 150 requested iterations

Started from b296bb852699e8d526179a3290312acafcb99bda, including the uncommitted combined-workflow work inherited from the interrupted measurement task. Tests run only at iterations 50, 100 and 150; local commits at each multiple of 25. No application tests or native AOT benchmarks are included. Later regression-development iterations add explicit contract cases; adding a case is not a passing execution claim.

| Iteration | Change | Execution status |
|---|---|---|
| 1 | Compiled per-check command dispatch | Checkpoint 50 passed |
| 2 | Compiled documentation gate in combined workflow | Checkpoint 50 passed |
| 3 | Compiled standalone Roslyn dispatch | Checkpoint 50 passed |
| 4 | Checks-only execution without test suites | Checkpoint 50 passed |
| 5 | Reject unknown and duplicate options | Checkpoint 50 passed |
| 6 | Validate worker count range | Checkpoint 50 passed |
| 7 | Bound regression subprocess duration | Checkpoint 50 passed |
| 8 | Expose compiled runtime identity | Checkpoint 50 passed |
| 9 | Reject runtime architecture mismatch | Checkpoint 50 passed |
| 10 | Attach architecture to timing reports | Checkpoint 50 passed |
| 11 | Architecture build selection | Checkpoint 50 passed |
| 12 | Debug and Release build selection | Checkpoint 50 passed |
| 13 | Supported OS/runtime validation | Checkpoint 50 passed |
| 14 | Partition build caches by RID and configuration | Checkpoint 50 passed |
| 15 | Fingerprint build identity | Checkpoint 50 passed |
| 16 | Fingerprint installed SDK version | Checkpoint 50 passed |
| 17 | Record compiled artifact hashes | Checkpoint 50 passed |
| 18 | Invalidate altered cached artifacts | Checkpoint 50 passed |
| 19 | Centralize owned-path containment | Checkpoint 50 passed |
| 20 | Exclusive cache ownership | Checkpoint 50 passed |
| 21 | Invalidate success marker before rebuild | Checkpoint 50 passed |
| 22 | Atomically replace cache metadata | Checkpoint 50 passed |
| 23 | Use compiled tools in Make entry points | Checkpoint 50 passed |
| 24 | Require explicit RID for native publishing | Checkpoint 50 passed |
| 25 | Document architecture and check cadence | Checkpoint 50 passed |
| 26 | Regression for supported runtime win-x64 | Checkpoint 50 passed |
| 27 | Regression for supported runtime win-x86 | Checkpoint 50 passed |
| 28 | Regression for supported runtime win-arm64 | Checkpoint 50 passed |
| 29 | Regression for supported runtime linux-x64 | Checkpoint 50 passed |
| 30 | Regression for supported runtime linux-arm64 | Checkpoint 50 passed |
| 31 | Regression for supported runtime linux-arm | Checkpoint 50 passed |
| 32 | Regression for supported runtime osx-x64 | Checkpoint 50 passed |
| 33 | Regression for supported runtime osx-arm64 | Checkpoint 50 passed |
| 34 | Regression for automatic Windows architecture | Checkpoint 50 passed |
| 35 | Regression for automatic Linux architecture | Checkpoint 50 passed |
| 36 | Regression for automatic macOS architecture | Checkpoint 50 passed |
| 37 | Regression for cross-architecture execution restriction | Checkpoint 50 passed |
| 38 | Regression for unsupported host architecture | Checkpoint 50 passed |
| 39 | Regression for unsupported runtime pair | Checkpoint 50 passed |
| 40 | Regression for canonical configuration names | Checkpoint 50 passed |
| 41 | Regression for parent traversal | Checkpoint 50 passed |
| 42 | Regression for absolute manifest path | Checkpoint 50 passed |
| 43 | Regression for sibling-prefix escape | Checkpoint 50 passed |
| 44 | Regression for empty manifest path | Checkpoint 50 passed |
| 45 | Adapt cache regressions to runtime partitioning | Checkpoint 50 passed |
| 46 | Regression for corrupted compiled artifact | Checkpoint 50 passed |
| 47 | Regression for Debug and Release cache isolation | Checkpoint 50 passed |
| 48 | Regression for recorded SDK identity | Checkpoint 50 passed |
| 49 | Make compiled-entrypoint contract checks | Checkpoint 50 passed |
| 50 | Whole compiled workflow report-byte parity at one and eight workers | Checkpoint 50 passed |

Checkpoint 50: 19 architecture/path contracts, 11 cache scenarios, seven compiled checks, 19 tracker/documentation tests, and six exact report hashes at jobs=1/8 passed. Initial failures exposed corrupt-output incremental reuse and sandbox temporary-directory handling; both were fixed and affected checks rerun successfully. Native AOT and application tests were not run. GNU Make target contracts were inspected; GNU Make itself is unavailable.
