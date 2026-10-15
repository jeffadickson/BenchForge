# BenchForge import pipeline standards: TPL Dataflow and XML API documentation

Instrument definition intake uses `System.Threading.Tasks.Dataflow` for bounded asynchronous processing. `InstrumentImportPipeline.ValidateAsync` accepts schema-v2 JSON documents, validates them with a configurable `TransformBlock`, and collects ordered results through an `ActionBlock`.

```csharp
using BenchDouble.Generated;

string[] jsonDefinitions = Directory.GetFiles("instrument-definitions/drafts", "*.definition.json")
    .Select(File.ReadAllText).ToArray();
var validated = await InstrumentImportPipeline.ValidateAsync(
    jsonDefinitions, capacity: 16, parallelism: 2, cancellationToken: CancellationToken.None);
Console.WriteLine($"Validated {validated.Count} simulation-subset definitions");
```

The current parser validates a **limited simulation-only subset** of schema-v2 definitions. It does not replace the full schema/review gate, generate C# source, or authorize physical commands. Batch processing is bounded; exceptions and cancellation terminate the pipeline. Do not use the Dataflow pipeline to bypass instrument lease/transaction serialization.

## Documentation requirements

All public handwritten and generated C# types and members require meaningful `/// <summary>` comments, `<param>`, `<returns>`, and `<exception>` where applicable. Document quantity units, measurement basis, cancellation, resource ownership and safety semantics. `GenerateDocumentationFile` remains enabled and DocFX renders API documentation. The legacy documentation gap baseline is tracked in issue #61; adding XML comments does not itself prove semantic completeness.

## Verification

Run `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj`, `make docs-check` and `make check-generated`. These commands must pass in .NET 10 CI before marking this increment verified.
