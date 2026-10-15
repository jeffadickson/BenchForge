# Stateful generated mocks

BenchForge's schema-v2 preview emitter now generates typed stateful mock decorators for scalar capabilities with **explicit** definition defaults. The generated mock retains a value across reads, allows test-specific async behavior injection, enforces the preview's mode eligibility, and supports reset to the definition default.

```csharp
using BenchDouble.Generated;
using UnitsNet;

var meter = new ExampleTwoModeDmmMockDecorator(new ExampleTwoModeDmmPreview());
await meter.ConfigureModeAsync("voltage");
meter.SetMockvoltage(ElectricPotential.FromVolts(1.25));
var measured = await meter.GetvoltageAsync(); // 1.25 V
meter.ResetMockvoltage();
measured = await meter.GetvoltageAsync(); // 0.25 V illustrative default
```

No real instrument commands are sent. Generated source includes XML API documentation and quantity-specific UnitsNet types. Concurrent reads and state updates are protected by the decorator's state lock; async behavior overrides are explicitly provided by tests.

**Scope limitation:** this increment supports deterministic state for already typed scalar measurement capabilities, not every instrument control, every source/measure state transition, or complex binary acquisitions. Unknown or unreviewed quantities do not receive fabricated defaults. The generated Keithley 2400 preview currently has mode selection only because the draft has no approved scalar measurement defaults.

## Inventory versus supported instruments

The imported snapshot has **484 top-level Python classes**, including helpers and channel classes. That is not 484 instrument models. The repository currently contains **two schema-v2 generated simulation preview classes** (an illustrative two-mode DMM and a Keithley 2400 mode skeleton) and an existing schema-v1 generated Keysight 33522B driver/mock pair. None of these counts establishes hardware-validated coverage. Classifying the 484 records into distinct instruments remains outstanding.

## Naming and attribution

Generated public API names and documentation use BenchForge terminology only. import metadata is retained separately for traceability and pending licensing review. Do not remove mandatory attribution or claim independent provenance for imported source material.

## Verification

Run `make check-v2-preview`, `make build-generated`, and `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj`. Passing CI evidence is still required before closing the generator issue.
