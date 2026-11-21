# BenchForge.Procedures — Starlark Procedure Runtime (`starlark-cs`)

**Status:** optional .NET 11 project backed by [`starlark-cs`](https://github.com/jeffadickson/StarLark-cs) (`external/starlark-cs`). This is a **mock-only procedure runtime**, not the released physical-equipment procedure editor.

## Why `starlark-cs`?

The instrument library and mock engine remain free of any scripting dependency. The optional `src/BenchForge.Procedures` project depends on `BenchForge`, `BenchForge.Experiments`, and `starlark-cs` (`Starlark.Syntax` + `Starlark.Eval`), replacing the earlier line-oriented Parlot prototype.

`starlark-cs` provides properties uniquely suited to test-lab procedures:
- **Deterministic and hermetic**: no filesystem, network, clock, or RNG access unless explicitly exposed by the host.
- **No `while` loops and no recursion** (`EnableRecursion = false`, `EnableLoad = false`), paired with **uncatchable** `MaxSteps`, `MaxAllocatedBytes`, `Timeout`, and `Cancellation` budgets.
- **Real Pythonic ergonomics**: `def` helpers, finite `for` loops, list comprehensions, f-strings, and first-class immutable UnitsNet quantities (`ProcedureQuantity : IThreadSafeHostValue`).
- **WebAssembly-ready**: the same `starlark-cs` engine can run in the browser for client-side preflight and simulation previews.

## Example

```python
# Example mock-only Starlark laboratory procedure
instrument("Meter1")
configure("Meter1", "RESISTANCE")

r1 = measure("Meter1", "RESISTANCE", "R1")
verify_between(r1, 95, 105, "Ohm")

wait_s(5)

r2 = measure("Meter1", "RESISTANCE", "R2")
verify_between(r2, 100, 110, "Ohm")
```

- `instrument(role)` validates a simulated typed meter registered in the `ExperimentSession`.
- `configure(role, kind)` selects `"RESISTANCE"` or `"VOLTAGE"`.
- `measure(role, kind, name)` reads a typed `UnitsNet` quantity, emits an attributable `ExperimentSample` with units and `simulatedElapsedSeconds`, and returns a first-class immutable `ProcedureQuantity` (`.value`, `.unit`, `.kind`).
- `verify_between(reading, min, max, unit)` enforces dimensional compatibility and inclusive bounds, throwing `ProcedureAssertionException` with the exact Starlark line/column on failure.
- `wait_s(seconds)` advances **virtual simulation time** only (0–86,400 s per call); it never sleeps wall-clock time.

## Run

```bash
dotnet run --project examples/BenchForge.ProcedureDemo
dotnet test tests/BenchForge.Procedures.Tests/BenchForge.Procedures.Tests.csproj
```

## Security and scope

- Source length is capped at 128 KiB.
- `load()`, `while`, and recursion are disabled in `ProcedureParser.ProcedureDialect`.
- `StarlarkProcedureLimits` enforces uncatchable step (`100,000` default), per-object allocation (`1 MiB` default), wall-clock timeout (`10 s` default), and `CancellationToken` limits.
- Only simulated `ITypedConfigurableMeasurementSource` roles can be bound; physical instrument control remains explicitly out of scope until authorization and safe-state gates are completed.

[Requirement matrix](../requirements/traceability.csv) · [User manual](USER-MANUAL.md) · [Issue #51](https://github.com/jeffadickson/BenchForge/issues/51)
