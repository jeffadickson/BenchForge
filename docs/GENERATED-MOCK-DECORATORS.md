# Generated mock decorators and provenance

The schema-v2 emitter now generates two simulation-only classes per supported definition: `<Instrument>Preview` and `<Instrument>MockDecorator`. The decorator wraps the preview and exposes explicitly injectable, asynchronous, UnitsNet-typed measurement behaviors. It delegates mode selection and validates mode eligibility **before** invoking an injected behavior. Setting a behavior to null restores the definition's illustrative default, if one was explicitly supplied.

```csharp
using BenchDouble.Generated;
using UnitsNet;

var preview = new ExampleTwoModeDmmPreview();
var mock = new ExampleTwoModeDmmMockDecorator(preview);
mock.SetvoltageBehavior(_ => ValueTask.FromResult(ElectricPotential.FromMillivolts(750)));
await mock.ConfigureModeAsync("voltage");
var reading = await mock.GetvoltageAsync(); // 0.75 V, simulation only
```

No mock behavior is invented for an unreviewed instrument capability. The uploaded PyMeasure corpus was augmented with **disabled, explicit-injection-only** decorator descriptors for its read-capable members. Those descriptors are review data, not executable SCPI commands or calibrated physical models. Until quantities, modes and defaults have been reviewed, a definition may generate only a mode skeleton or remain in the import dataset.

## Reproducible provenance in logs

Generated preview classes expose:

- `DefinitionSha256`: SHA-256 of the exact schema-v2 input bytes; changes when the definition changes.
- `UpstreamRevision`: verified 40-character upstream commit from definition metadata, otherwise `unverified`.
- `GeneratorVersion`: explicit emitter format revision, currently `v2-decorator-1`.
- `BuildCommit`: the BenchForge Git commit stamped into the generated assembly via `SourceRevisionId`, otherwise `unavailable`.

Example logging:

```csharp
logger.LogInformation(
    "Instrument {Name} definition={Definition} upstream={Upstream} generator={Generator} build={Build}",
    nameof(ExampleTwoModeDmmPreview),
    ExampleTwoModeDmmPreview.DefinitionSha256,
    ExampleTwoModeDmmPreview.UpstreamRevision,
    ExampleTwoModeDmmPreview.GeneratorVersion,
    ExampleTwoModeDmmPreview.BuildCommit);
```

**Do not embed the moving repository HEAD in the generated source.** That would make deterministic generated-code checks fail on every unrelated commit. GitHub Actions supplies `SourceRevisionId` during compilation; local builds can use `dotnet build -p:SourceRevisionId=<commit-sha>` to stamp a known revision.

## Rebuild and verification

```sh
make clean-generated
make generate-v2-preview
make check-v2-preview
make build-generated
dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
```

The `generated/BenchForge.Instruments/Code/` directory is disposable; its parent project file is not. New xUnit tests check mode guards, injected UnitsNet values, reset-to-default behavior, and provenance metadata.

**Scope:** This is a schema-v2 *simulation preview* generator. It is not a manufacturer-validated SCPI driver, a general-purpose mock physics engine, or proof of physical safety. The imported ZIP has no verified upstream Git commit and its source revision remains `unverified`. Successful .NET 10 CI execution has not yet been established.
