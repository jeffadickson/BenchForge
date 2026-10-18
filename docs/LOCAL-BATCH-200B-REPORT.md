# BenchForge local development batch 200B

This batch adds deterministic phase latency, a virtual event queue, opt-in timed scalar mock reads, and bounded standalone scalar transcript replay. It comprises 40 implementation increments and 160 named regression additions, each recorded as a separate local iteration. The ledger names every change; the count is not a claim of 200 completed features.

## Changes

- Simulation latency: seven independently described phases; bounded durations and jitter; seed/index sampling without shared mutable randomness; immutable samples and checked logical deadlines.
- Virtual scheduler: explicitly injected canonical run clock; bounded queue; stable deadline/acceptance ordering; cancellation, clear, diagnostics, checked deadlines and clock rollback rejection. Explicit pumping introduces no background timers or workers.
- Main mock engine: explicit scalar preparation integrated with its existing transaction gate, mode, sequence and fault state. Early reads preserve pending faults and sequences. Success consumes preparation and sequence advancement with one revision. Mode changes and reset invalidate preparations; the imported facade uses the same engine.
- Standalone replay: bounded copied canonical scalar samples, exact mode/capability/logical-time matching, read-only snapshots, reset, independent fork and serialized concurrent consumption. A consuming replay cursor must not be used as an ordinary engine callback without a transactional adapter.
- 160 new focused facts cover rejection boundaries, cancellation, state preservation, deterministic repetition, immutable snapshots, independent runs and actual concurrent operations. Final coverage includes an explicit run-clock latency/scheduler/mock/replay timeline.

## Executed checkpoints

Tests ran only at iterations 50, 100, 150 and 200. Counts overlap between checkpoints; they must not be added as unique tests. Existing unrelated suites were not run. Compiled checks include the fresh Roslyn documentation scan and requirement reports, comparison checks and 19 checker regression tests at each checkpoint.

| Iteration | Targeted xUnit passes | Checker passes | End-to-end command times (test / checks / parity if applicable) |
|---|---:|---:|---|
| 50 | 47 | 19 | 46.77s / 10.39s |
| 100 | 80 | 19 | 77.20s / 19.66s |
| 150 | 163 | 19 | 73.49s / 11.04s |
| 200 | 201 | 19 | 45.06s / 9.70s / 26.59s |

At iteration 200, the focused checker workflow contract also compares all six generated reports for exact serial/parallel parity. Command times include startup and required incremental builds; xUnit's own short test duration excludes that overhead. These checkpoints are validation timings, not a controlled cold/cached performance benchmark.

The initial checkpoint-150 documentation failure is retained separately; adding the missing partial-class XML summary fixed it, and the affected checks then passed. Runtime code did not change between that checkpoint's passing test run and check retry.

Execution evidence records the full parent SHA and explicitly includes pending checkpoint changes. The manifest fingerprints the final tested code. The iteration-200 commit records those changes and the reviewed evidence; it cannot contain its own SHA.

## Requirements and limitations

TR-047 now has partial source and test evidence; TR-043 and TR-049 retain their existing canonical links and gain supplemental evidence. Existing GitHub issue URLs are preserved; remote issue state was not changed. There are 228 requirements and zero Verified requirements. Full acceptance is still open; local passing regressions do not qualify physical devices or promote requirement status.

Remaining work includes a transactional engine replay adapter, complete cross-device recording/replay, automatic step-runner pacing, UI integration and platform/device qualification. Existing fleet/browser work and legacy documentation debt remain open. Unprepared scalar reads and separately captured immutable acquisitions retain their existing timing contracts. The scheduler/replay have no ownership or hardware command authority.

The compiled C# check workflow and architecture-aware build.ps1/Make compatibility entry points remain in use. GNU Make is unavailable on this host; its wrapper behavior is covered by the focused workflow contract rather than a Make execution. Only Windows x64 was exercised; architecture selection is not cross-platform runtime qualification.

## Repository review

Base: `ebe558c93eac75436f61eef70df041df90955d71`. The finalization verifies exactly one numbered, nonempty commit for each of 200 iterations, a clean worktree, normal fast-forward ancestry, and remote/local SHA agreement after the sole final push. Actual final SHA and post-push status are written to the external overall report after publication. No intermediate push is part of this batch.

See [iteration ledger](LOCAL-BATCH-200B-2026-10-18.md) and [source/evidence manifest](check-evidence/batch200B-20261018-manifest.json).
