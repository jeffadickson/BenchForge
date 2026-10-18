# Full-workflow measurement status

The previous discovery run measured 371.232 s with cold build artifacts and 380.682 s with cached artifacts. These were affected by concurrent work and memory pressure and are not a controlled comparison. A candidate smoke run passed seven checks and 19 focused regressions: 558.926 s build, 8.914 s check phase, before bounded build flags were introduced. No optimized cold/cached speedup is claimed.

The current default uses a managed compiled C# host, avoiding native AOT on the critical path. Source, SDK, runtime, configuration and output-integrity checks guard compiled cache reuse. The full benchmark harness remains available separately; it is not run during the 150-iteration regression checkpoints.

## Checkpoint 350 complete workflow samples

| Artifact state | Seconds including startup | Build performed |
|---|---:|---|
| New empty artifact cache | 51.182 | Yes |
| Reuse of same validated cache | 28.578 | No |

These sequential Windows x64 samples include the PowerShell process, explicit .NET executable selection, SDK discovery, fingerprinting, build/restore if needed, seven compiled checks and 19 checker regressions. Application test runs and cache-corruption regressions finished before these measurements. Existing SDK, package and OS caches were retained. One sample per state on a shared desktop does not establish a statistical speedup; historical figures above were taken under different load. Raw commands and stage times are versioned in `docs/check-evidence/batch350-350.json` and exported with the overall report.

The selected default remains the shared managed compiled C# workflow, which avoids native compilation on a cold path and validates architecture/content before cache reuse. Optional per-command native measurements remain separate. No unsupported foreign runtime execution or GNU Make execution is claimed.
