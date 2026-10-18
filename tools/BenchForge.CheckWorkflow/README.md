# Complete check workflow

From the checkout root on Windows:

```
powershell -NoLogo -NoProfile -NonInteractive -File scripts/checks.ps1
```

Use `pwsh` on Unix with `-Python python3`, or `make checks`. The .NET SDK and
Python are required. Native compiler tools are not required by this workflow.

The bootstrap builds one managed host from the **same** C# checker sources and
the shared Roslyn auditor. Independent requirements, scoreboard, source audit,
preflight, Roslyn and the two focused test suites overlap. The documentation gate
is compiled C# and waits for Roslyn; report comparison waits for Roslyn and preflight. Output is
printed in canonical stage order. A failed producer prevents its dependent
check from consuming a stale report. All stage failures produce a nonzero exit.

The nine tracker tests and ten documentation tests are the only test suites run.
This does not run application tests, benchmarks, the exhaustive port parity
matrix, native compilation, StrictDoc export, Sphinx or DocFX. Those remain
separate commands. Every check and all six reports are regenerated on each run;
only the compiled tooling is cached.

The default cache base is `.tools/check-workflow`; each runtime/configuration uses a separate subdirectory (for example `win-x64/Release`). Source content fingerprints include
the shared code, resource, helper, project and repository build configuration.
Changed content is detected even if timestamps are preserved. Missing or modified required output files trigger a rebuild. SDK version, runtime and configuration are included in the fingerprint. An exclusive file lock prevents competing builds from mutating a cache. A manifest removes obsolete owned source copies
after validating their resolved paths remain inside the cache source directory.
Fingerprint updates occur only after successful builds. `-Rebuild` forces a
build, and `-PrepareOnly` prepares tooling without running checks.

Builds use one MSBuild worker, no persistent build servers, and workstation GC in
the build subprocess. This bounds the build's memory use on small machines while
retaining parallel checking (`-Jobs 8` by default). The bootstrap restores the
caller's GC environment after the build. Other applications and global caches
are left alone.

`-Root`, `-Cache`, `-Python`, `-Jobs`, `-Architecture`, `-Configuration`, `-Command`, `-TimeoutSeconds` and `-Timing` select the checkout, tooling
cache, interpreter, scan concurrency and timing report. The timing report includes
build and check-host wall time; its `.checks.json` companion includes overlapping
stage durations, host CPU time and peak working set. Do not sum overlapping stage
durations as a workflow total. The benchmark's external timer additionally
includes fresh-shell startup, fingerprinting and timing/report writes.

For focused cache validation:

```
python tests/check_ports/workflow_cache.py
```

For the full cold/cached benchmark, with no concurrent measurement runs:

```
python tests/check_ports/workflow_benchmark.py --label my-run --cached-runs 3
```

The benchmark uses a new empty build directory per variant and the previous
revision's native/standalone workflow as its baseline. Baseline tracker fixtures
are loaded from that pinned revision; the optimized workflow uses the smaller
equivalent fixture. It verifies SHA-256 equality of all six generated reports
for every successful run. The SDK, package download cache and OS filesystem cache
are retained, so “cold” means **cold build artifacts**, not a fresh OS or first
SDK installation. Source checkout setup is outside the measured interval.

## Architecture selection and focused commands

`make checks-plan` prints the selected build identity without compiling. `make checks-prepare CHECKS_ARCH=arm64` builds a separate arm64 cache; cross-architecture builds require prepare-only mode. Execution requires the actual .NET process architecture to match the request. Supported runtime identifiers: win-x64, win-x86, win-arm64, linux-x64, linux-arm64, linux-arm, osx-x64, osx-arm64. Only the local host runtime is execution-tested here.

`make checks-only` runs checks without regression suites. Individual Make report/check targets also use the compiled host. Set `CHECKS_CONFIGURATION=Debug` for a separate Debug cache. Optional native publishing requires `CHECKS_RID` explicitly and is outside the normal check workflow. Python checker scripts remain independent output references.
