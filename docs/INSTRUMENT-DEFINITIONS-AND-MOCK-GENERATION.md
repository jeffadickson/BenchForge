# Instrument Definitions and Generated AutoMocks

**Revision:** 2026-10-15. **Status:** first typed driver/mock generation increment committed; build and hardware validation not yet completed.

## One definition, two implementations

An `*.instrument.json` file describes the supported instrument properties, physical quantities, canonical units, SCPI query/write templates, ranges, channels, and initial mock values. The offline `BenchDouble.DriverGen` tool generates:

1. `Keysight33522B.g.cs` — typed driver using `IAsyncScpiSession` and UnitsNet.
2. `Keysight33522BMock.g.cs` — typed mock configuration and a stateful SCPI transport that uses the **same command templates**.

The mock inherits `DefinitionDrivenScpiMockSession`, which validates supported commands, channel bounds and numeric limits. The driver does not know whether the transport is a physical VISA/SCPI adapter or the generated mock. No repeated handwritten `GetFrequency` or `SetAmplitude` mock implementation is necessary.

### Example definition

```json
{
  "schemaVersion": 1,
  "manufacturer": "Keysight",
  "model": "33522B",
  "driverName": "Keysight33522B",
  "channels": { "count": 2, "template": "SOUR{channel}" },
  "parameters": [
    {
      "name": "Frequency",
      "kind": "control",
      "quantity": "Frequency",
      "unit": "Hertz",
      "query": "SOUR{channel}:FREQ?",
      "set": "SOUR{channel}:FREQ {value}",
      "minimum": 1,
      "maximum": 20000000,
      "mock": { "defaultValue": 1000 }
    }
  ]
}
```

The actual [33522B definition](../instrument-definitions/keysight/33522b.instrument.json) includes frequency and amplitude. Its SCPI subset and bounds are **illustrative, not hardware validated**. We must verify against the instrument programming manual before use with real equipment.

### Using the generated mock

```csharp
var mock = new Keysight33522BMock();
var driver = mock.CreateDriver();

mock.SetMockFrequency(1, Frequency.FromKilohertz(1.5));
Frequency frequency = await driver.GetFrequencyAsync(1);

await driver.SetAmplitudeAsync(2, ElectricPotential.FromVolts(2));
ElectricPotential amplitude = await driver.GetAmplitudeAsync(2);
```

The mock supports separate state per channel. An unsupported command raises `NotSupportedException`; an out-of-range value/channel raises an exception rather than silently accepting a nonexistent capability. Values are converted with invariant culture. The mock is deterministic and contains no arbitrary C# evaluation.

## Generate and verify

From the repository root:

```bash
python3 scripts/check_instrument_catalog.py
make generate
make check-generated
make demo-generated-mock
dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
```

On Windows, use the equivalent `dotnet run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- instrument-definitions src/BenchDouble/Generated` and append `--check` to compare checked-in generated files without modifying them.

Generated source is committed to the repository so IDE navigation and ordinary `dotnet build` work without running the generator first. The no-write `--check` mode guards against source drift. The Makefile and cross-platform CI include catalog validation and generated-source checks. **CI is still blocked by GitHub-hosted runner allocation**, so this is not a verified green build.

## Instrument inventory: 25 external instrument source entries plus Keithley 236

[Machine-readable catalog](../instrument-definitions/instrument-catalog.json) records the 25 previously identified external instrument source driver entries plus the independently implemented legacy Keithley 236. It distinguishes:
- `catalogOnly`: model known from the published catalog; no ported driver claimed.
- `manualDriverPartial`: Keithley 236 DC source/measure core and resistive DUT mock exist.
- `definitionDraft` / `definitionReviewed`: future definition maturity levels.
- `hardwareValidated`: requires documented physical testing; **none of the current entries has this status**.

The catalog does not contain fabricated command mappings. Only the illustrative Keysight 33522B schema currently participates in SCPI code generation. The Keithley 236 is a separate **non-SCPI** IEEE-488 driver and must not be passed through a SCPI property generator.

## Keithley 236 reference implementation

The legacy [Keithley236](../src/BenchDouble/Instruments/Keithley/Keithley236.cs) source and [mock bus](../src/BenchDouble/Simulation/Keithley236MockBus.cs) now reject invalid mode enum values, reset old compliance/bias state on source-mode changes, serialize mode checks with GPIB bus operations, and require mode/bias/compliance configuration before requesting OPERATE. Tests cover both DC source/measure modes, compliance clipping, standby, mode change, invalid values and simulated write failures.

**Not yet complete:** source/measure sweeps, trigger and status parsing, fixed ranges, hardware error bits, calibrated GPIB TALK/EOI timing, physical safe-state verification, and authorized instrument leases. A software standby request does not prove safe terminals. See [Keithley and external instrument source catalog](external instrument source-25-INSTRUMENTS-AND-KEITHLEY-236.md) and [issue #19](https://github.com/jeffadickson/BenchDouble/issues/19).

## Architectural limitations and next increments

The generated mock currently models scalar control/readback parameters with exact SCPI templates. It is **not** a complete instrument behavioral model: no command-mode transitions, triggers, acquisition timing, output enable, waveforms, uncertainty, error queues, device-specific response parsing, or current/voltage compliance physics.

Before generating hundreds of drivers, expand the schema to cover capability/mode state machines, read-only measurements, initialize/reset behavior, quantity basis (RMS/peak/peak-to-peak), channel hierarchies, device status/errors, and default mock behavior functions. The typed mock adapter should then consume the schema while custom C# behaviors remain injected through experiment-scoped DI.

Track these tasks in [issue #44](https://github.com/jeffadickson/BenchDouble/issues/44). All new requirement IDs BD-GEN-001..006 remain SourceCommittedUnverified until actual tests pass.
