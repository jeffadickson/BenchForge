# Full-workflow measurement status

The previous discovery run measured 371.232 s with cold build artifacts and 380.682 s with cached artifacts. These were affected by concurrent work and memory pressure and are not a controlled comparison. A candidate smoke run passed seven checks and 19 focused regressions: 558.926 s build, 8.914 s check phase, before bounded build flags were introduced. No optimized cold/cached speedup is claimed.

The current default uses a managed compiled C# host, avoiding native AOT on the critical path. Source, SDK, runtime, configuration and output-integrity checks guard compiled cache reuse. The full benchmark harness remains available separately; it is not run during the 150-iteration regression checkpoints.
