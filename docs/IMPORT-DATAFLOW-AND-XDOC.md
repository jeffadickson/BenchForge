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

## Where Dataflow belongs

Use Dataflow for bounded batches of instrument definitions, CPU-intensive waveform transforms, buffered measurement persistence and optional telemetry fan-out. For small one-off operations, ordinary `async/await` or `Parallel.ForEachAsync` is simpler. **Do not use Dataflow to replace instrument ownership locks, transaction serialization or host leases.**

For continuous capture, distinguish lossless archival (bounded queue, explicit backpressure/fail-fast policy) from lossy visualization (bounded latest-value updates). Do not allow a slow dashboard to hold an instrument's exclusive SCPI transaction. Prefer capture completion before enqueueing immutable data to downstream processing. Avoid unbounded `Post` calls; use `SendAsync` and check rejection/fault propagation.

## Make targets

```sh
make import-check   # Focused schema-v2 and Dataflow tests
make demo-alpha     # Hardware-free waveform/RMS demonstration
make alpha-check    # Generated-code check, importer tests, demo and host tests
make docs-check     # XML API documentation audit
```

These targets require the .NET 10 SDK. Their presence does not imply passing CI; actual execution evidence must be reviewed before closing requirements.
