# Imported instrument data and generated code

BenchForge separates **source data** from **disposable generated C#**.

| Location | Purpose | May delete? |
|---|---|---|
| `instrument-definitions/imports/external source/` | Snapshot provenance and eventually all extracted, unreviewed schema-v2 definitions | **No** |
| `instrument-definitions/drafts/` | Reviewed or illustrative schema-v2 input definitions | **No** |
| `instrument-definitions/keysight/` | Existing schema-v1 driver inputs | **No** |
| `tools/BenchDouble.DriverGen/` | Hand-maintained generator and Dataflow emitter | **No** |
| `generated/BenchForge.Instruments/BenchForge.Instruments.csproj` | Hand-maintained .NET 10 generated-code build project | **No** |
| `generated/BenchForge.Instruments/Code/` | Generated schema-v2 simulation-preview C# files | **Yes, via make clean-generated** |
| `src/BenchDouble/Generated/` | Legacy schema-v1 generated output | Not yet; migrate separately |

## Regenerate

```sh
make clean-generated
make generate-v2-preview
make check-v2-preview
make build-generated
dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
```

The v2 emitter uses bounded TPL Dataflow stages, UnitsNet quantity mappings and XML documentation comments. All generated classes are **simulation-only** until the review gate and production driver generator are complete. CI checks generated-code drift and compiles the isolated generated project.

## Imported external source dataset status

The original uploaded external source snapshot was inventoried: 256 Python source files, 484 top-level classes, 2,099 methods and 2,695 descriptors, with zero AST parse errors. `snapshot-manifest.json` commits the snapshot hash and aggregate coverage. It **does not contain the full class/descriptor/method CSVs**; those were produced as downloadable conversation artifacts and still need to be committed as source data. Do not confuse the inventory with 484 approved instrument drivers.

Two existing schema-v2 drafts currently produce checked-in example preview classes: `ExampleTwoModeDmmPreview` and `Keithley2400Preview`. The Keithley draft lacks reviewed scalar measurement defaults and therefore intentionally emits no measurement methods. This is not a Keithley 2400 production driver.

Future bulk imports must preserve the raw source expression, original description, normalized description, source file/line, quantity/unit **candidates**, review status, measurement basis and unresolved inheritance. Do not turn guessed units into typed hardware APIs.
