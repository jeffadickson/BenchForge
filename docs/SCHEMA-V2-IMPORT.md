# Instrument import: schema-v2 scalar runtime prototype

BenchForge can now parse a limited subset of schema-v2 instrument definitions directly into `ImportedInstrumentDefinition` and instantiate a `ImportedInstrumentMock` with mode-dependent scalar readings.

```csharp
using BenchDouble.Generated;

string json = File.ReadAllText("instrument-definitions/drafts/example-two-mode-dmm.definition.json");
var definition = ImportedInstrumentDefinition.Parse(json);
var mock = new ImportedInstrumentMock(definition);
await mock.ConfigureModeAsync("voltage");
double volts = await mock.MeasureAsync("voltage"); // 0.25 (simulation default)
await mock.ConfigureModeAsync("resistance");
double ohms = await mock.MeasureAsync("resistance"); // 4700 (simulation default)
```

The adapter rejects invalid mode identifiers, duplicate capability names, unsafe command strings, unknown quantity/unit combinations and unsupported measurement modes. It does not execute any commands from imported JSON.

**Maturity boundary:** This is a runtime **simulation-only scalar subset**, not completed source generation. It does not yet emit C# classes, enforce the full JSON Schema, verify provenance/review status, implement source/measure writes, parse binary data, support trigger operations or connect to physical hardware. An illustrative definition is accepted for mock preview only; do not treat that as manual review or hardware validation.

Run `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj` on .NET 10. Source tests are committed but CI results remain unverified.

Next: integrate reviewed schema-v2 definitions into the standalone DriverGen, generate typed async methods and stateful mocks, enforce review gating for hardware-backed drivers, and add oscilloscope waveform capabilities. Track #63 and #66.
