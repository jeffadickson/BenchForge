# Local artifact storage validation — 2026-10-15

Work item #62; requirements BD-ART-001..004 remain PartialSourceUnverified.
BD-ART-005 (comparative performance benchmark) remains DesignOnly. This report
records focused local evidence, not complete production acceptance.

## Executed

- The new library and xUnit project restored and compiled with .NET SDK 8.0.100
  using its MSBuild entry point. Recompilation after the final quota/test changes
  also succeeded. No new API-comment gaps were introduced.
- Standard VSTest crashed in `TestEngine.ShouldRunInProcess` before executing
  tests, matching the earlier environment limitation.
- An in-process reflection harness invoked all 12 xUnit Fact methods with their
  actual xUnit assertions: all passed. This is a test-method execution result,
  not a passing VSTest result. Covered restart/readback, trace/screenshot links,
  exact retries and conflicts, short/long streams, midstream I/O failure,
  cancellation, quota enforcement, corrupted bytes, nonseekable 2 MiB input,
  invalid descriptors/schema versions, missing/run-scoped lookups, competing
  store instances and quiescent database relocation.
- The committed `ArtifactCrashProbe` was compiled directly with Roslyn and run by
  `scripts/check_artifact_recovery.py`. It saved one capture, then terminated
  without stack unwinding during a second transaction. The independent Python
  SQLite connection recovered a valid database with exactly the first capture,
  correct original bytes/digest and exactly its pending outbox entry.
- The capture demo compiled directly with Roslyn and ran twice. Both runs returned
  the same waveform and screenshot IDs and digest, with two captures total. The
  verified screenshot was exported only after the streaming hash check passed. Its PNG
  structure/CRCs were independently checked after correcting the initial fixture.
- C# syntax audit: 868 visible declarations. Combined Python/C# inventory: 931
  declaration/module records; 1,538 legacy gaps, zero new gaps, zero generated gaps.
  No legacy baseline exemptions were added for this increment.

The environment's MSBuild process-metadata failure recurred when building the
probe/demo projects, so those were compiled with the SDK compiler directly against
successfully built artifact dependencies. Normal clean-host demo/probe project
builds still need confirmation. Routine commit skips hosted CI; new CI steps will
exercise the normal paths on the next authorized run.

Nine requirement gate tests and three documentation gate tests passed. StrictDoc
exported all 220 requirements, and Sphinx built with warnings treated as errors.
An assembly-based DocFX build rendered the core and artifact APIs successfully
with zero errors and 37 existing guide-link warnings; normal project-based
rendering remains subject to the environment limitation above.

## Reproduce on a supported development host

```sh
make setup
make artifact-check
make demo-capture
make docs-check
```

The process-exit check uses a fresh temporary database and requires exit code 97
from the probe; other failures are not misreported as successful crash injection.
It tests application process termination, **not power loss** or every filesystem's
flush/locking behavior. It does not replace sustained recording, disk-full,
large-memory-limit, backup/restore or remote-replication tests.

## Remaining acceptance

No comparative BLOB/file/object-store performance claims. No remote delivery,
retention/garbage collection, online backup service, independent waveform decoder,
control-loop scheduling integration or authenticated artifact API. The outbox is
pending intent only. Public save success means local SQLite commit under its
filesystem guarantees. Disk errors propagate; the runner's recording-failure
policy integration remains open.
