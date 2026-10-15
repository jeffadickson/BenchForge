# BenchDouble.Procedures — Parlot Procedure Language Prototype

**Status:** source-committed experimental .NET 8 project. No successful build/CI evidence yet. This is a **mock-only DSL prototype**, not the released test-lab procedure editor.

## Why a separate project?

The instrument library and mock engine must not depend on a parser. The optional `src/BenchDouble.Procedures` project depends on `BenchDouble`, `BenchDouble.Experiments` and stable **Parlot 1.5.9**. It translates human-readable procedure steps into a typed abstract syntax tree (AST), validates instrument roles and quantity dimensions, then executes against existing simulated instrument interfaces using the existing `ExperimentRunner`.

The eventual browser procedure editor and textual DSL should share one structured procedure representation. This prototype is the beginning of that representation, **not** a complete reversible editor format. Comments and stable step IDs are not yet round-tripped.

## First example

```text
# This is a simulated experiment, not a hardware command script
INSTRUMENT Meter1
CONFIGURE Meter1 RESISTANCE
MEASURE Meter1 RESISTANCE AS R1
VERIFY R1 BETWEEN 95 Ohm AND 105 Ohm
WAIT 5 s
MEASURE Meter1 RESISTANCE AS R2
VERIFY R2 BETWEEN 100 Ohm AND 110 Ohm
```

`INSTRUMENT` references a role registered in the experiment definition. It does not create or discover a physical device. `CONFIGURE` selects a typed mock DMM mode. `MEASURE` reads `ElectricResistance` or `ElectricPotential` from UnitsNet and records a result with units. `VERIFY` checks inclusive limits and fails the run with the source line on a limit violation. `WAIT` advances **virtual simulation time**, never blocks for wall-clock seconds.

Supported v1 statements: `INSTRUMENT`, `CONFIGURE`, `MEASURE ... AS`, `VERIFY ... BETWEEN ... AND`, and `WAIT`. Supported measurement quantities: `RESISTANCE` (Ohm) and `VOLTAGE` (V). No `SET`, `IF`, `LOOP`, `SIGN`, `C#`, or physical instrument operations exist in this prototype.

## How the pieces connect

1. `ProcedureParser.Parse(source)` uses Parlot fluent parser combinators to create `ProcedureDocument` AST nodes and `ProcedureDiagnostic` source-line errors.
2. `ProcedureValidator.Validate(document, session.RegisteredInstruments)` checks declared roles, simulated typed meter capabilities, configured mode, variable existence and dimensional compatibility.
3. `MockProcedure` implements the existing `ExperimentProcedure` contract. It receives an `ExperimentSession` (DI-composed run scope), not a global instrument service locator.
4. `ExperimentRunner.RunAsync` executes and captures failures, while `ExperimentContext.EmitAsync` records measurements in the existing result sink.
5. The mock's `ManualSimulationClock` drives deterministic time-dependent values. Physical VISA/GPIB/remote host access is deliberately not available.

## Run

From the repository root:

```bash
dotnet run --project examples/BenchDouble.ProcedureDemo
dotnet test tests/BenchDouble.Procedures.Tests/BenchDouble.Procedures.Tests.csproj
```

Or `make demo-procedure`. The demo's `Meter1` resistance mock returns 100 Ohm initially and 105 Ohm after five simulated seconds, so both assertions should pass.

The test project covers parse errors, typed quantities, virtual time, result metadata, unknown roles, wrong modes, undefined variables, wrong dimensions and limit failures. These are **written tests, not yet verified passing**.

## Why Parlot rather than Roslyn?

Parlot is an appropriate lightweight front end for a controlled procedure syntax, and its stable 1.x package supports .NET 8. Roslyn remains appropriate for approved custom C# mock behavior, but ordinary test procedures should not require users to compile arbitrary executable code. This separation also lets a future graphical editor and text editor target the same validated procedure AST.

Parlot itself does not solve procedure revisioning, round-trip comments, stable step IDs, electronic signatures or operator permissions. Those remain tracked in [issue #51](https://github.com/jeffadickson/BenchDouble/issues/51) and existing runner requirements TR-001..TR-014.

## Security and scope

- Parser input is limited to 128 KiB and 2048 executable steps.
- Unknown syntax is rejected with a line diagnostic.
- The only supported instrument interface is a **simulated** `ITypedConfigurableMeasurementSource`.
- No reflection-based arbitrary method invocation, arbitrary expressions, Roslyn compilation or network/hardware access.
- `WAIT` is limited to 0–86400 simulated seconds per step.
- Semantic preflight rejects missing roles, incorrect measurement modes, undefined variables and unit-dimension mismatches.
- This does not yet provide physical safe-state, authorization, a full structured editor or revision control.

## Next milestones

Stable step IDs and lossless text/GUI round trips; structured procedure revisions; instrument-definition-driven command metadata; more UnitsNet quantities; branching/loops with bounded execution; operator review/redlines; browser editor integration; approved instrument-host operations; Reqnroll/Playwright acceptance; verified cross-platform builds.

[Requirement matrix](../requirements/traceability.csv) · [User manual](USER-MANUAL.md) · [Issue #51](https://github.com/jeffadickson/BenchDouble/issues/51)
