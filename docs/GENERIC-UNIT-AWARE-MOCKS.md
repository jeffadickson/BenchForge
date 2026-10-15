# Generic UnitsNet Measurements and Default AutoMocks

**Status:** typed measurement foundation implemented in source; instrument-definition-driven generator not yet implemented. Compilation/CI execution remains unverified.

## 1. Why C# generics solve part of the problem

Java generics traditionally use JVM type erasure. .NET generics preserve runtime type arguments, so `typeof(TQuantity)` can identify `ElectricResistance`, `ElectricPotential`, `Temperature` and other UnitsNet quantity types. For value types, .NET can also specialize generic code.

A method can use an explicit generic argument:

```csharp
ElectricResistance r = await meter.MeasureAsync<ElectricResistance>();
ElectricPotential v = await meter.MeasureAsync<ElectricPotential>();
```

However, **C# generally cannot infer `TQuantity` from the variable receiving the return value**:

```csharp
// Not supported: no generic argument or parameter from which to infer T.
ElectricResistance r = await meter.MeasureAsync();
```

A typed capability-specific API remains preferable when the operation has a fixed meaning:

```csharp
ElectricResistance r = await meter.MeasureResistanceAsync();
```

The generic API is useful for a shared meter/test-runner layer, but it does not magically make all instruments support all physical quantities.

## 2. Implemented API

`ITypedMeasurementSource.MeasureAsync<TQuantity>()` returns `ValueTask<TQuantity>` with `where TQuantity : struct, IQuantity`. `ITypedConfigurableMeasurementSource.ConfigureAsync<TQuantity>()` explicitly selects the meter's measurement mode.

`TypedMockMeasurementsBuilder` registers supported quantity types and strongly typed behaviors:

```csharp
var clock = new ManualSimulationClock();
var capabilities = new TypedMockMeasurementsBuilder()
    .Constant(ElectricPotential.FromMillivolts(250))
    .Register<ElectricResistance>(t =>
        ElectricResistance.FromOhms(4700 + 2*t.TotalSeconds))
    .Build();

ITypedConfigurableMeasurementSource meter =
    new TypedMockMeter(capabilities, clock);

await meter.ConfigureAsync<ElectricResistance>();
ElectricResistance resistance =
    await meter.MeasureAsync<ElectricResistance>();
```

The internal dispatch table uses `typeof(TQuantity)` as a key; the **public API is fully generic and typed**. There is no public string-keyed property lookup and no reflection-based guessing from a caller's assignment.

### Important invariants

- Only explicitly registered quantity types are supported.
- The requested type must equal the configured instrument mode.
- Requesting voltage while in resistance mode raises an exception; it does not silently reconfigure the meter.
- Registering the same quantity twice raises an exception.
- Mock behaviors receive the shared run-scoped logical elapsed time.
- UnitsNet performs physical-unit conversions within a quantity (e.g. kΩ to Ω), never dimension conversion (Ω to V).
- Each mock instance serializes mode changes and measurements through an async semaphore.
- The typed registry does not yet validate measurement ranges, accuracy, calibration, overload indicators, open-circuit resistance, or command-level SCPI semantics.

## 3. Why wait for the instrument dataset?

**Do not infer an instrument's supported quantities from the generic API.** The instrument-definition dataset should provide:
- capability type: voltage, resistance, current, temperature, frequency, etc.;
- mode selection commands and legal transitions;
- canonical UnitsNet quantity type and display unit;
- valid ranges, resolution, accuracy, integration time and instrument-specific limits;
- read commands and response parsing;
- unsupported combinations and status/error handling;
- optional default behavior and fault hooks.

Once the definitions are sufficiently complete, the existing offline driver generator can produce **both strongly typed instrument APIs and typed default mock registrations**. That preserves compile-time discoverability without writing repetitive boilerplate. Until then, hand-register the few quantities actually supported by each prototype.

## 4. Generic mock versus instrument mode

Suppose the test calls `MeasureAsync<ElectricResistance>()` while the DMM is in voltage mode. The correct behavior is to reject it (or require a documented operation that explicitly changes mode). Returning a random resistance merely because the requested type is `ElectricResistance` would conceal real test-program bugs.

A source meter has an even richer state model: source voltage vs source current, sense voltage vs sense current, compliance and four-wire configurations. The generic type alone cannot express all these constraints. Instrument-specific typed capabilities and state machines remain necessary.

## 5. Tests and example

- `tests/BenchDouble.Tests/TypedMockMeasurementsTests.cs`: Ω and V types, unit conversion, mode mismatch, unsupported capability, time-indexed behavior, duplicate registration.
- `examples/BenchDouble.TypedMockDemo`: standalone resistance/voltage DMM mock.
- `examples/BenchDouble.ExperimentDemo`: adds a named `Meter1` to the same experiment as the thermal chamber and power supply.

Run:

```bash
dotnet run --project examples/BenchDouble.TypedMockDemo
dotnet run --project examples/BenchDouble.ExperimentDemo
dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
```

## 6. Future integration

BD-AM-015 and issue #40 track generation from instrument definitions, including quantity capabilities, modes, range validation, command/response mapping and typed default behaviors. We should then add automatic mock overrides by command, measurement type and instrument state, with explicit initialization and deterministic time-dependent faults.

This is a simulation foundation, not a substitute for actual instrument drivers or hardware qualification.
