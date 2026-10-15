# One Typed Instrument API, Three Result Paths — with 25 Verified PyMeasure Examples

**Date:** 2026-10-14 · **Status:** design + source audit; the unified factory/dispatcher is **not yet implemented**.

## 1. Architecture decision

A remote server is **not** a third physical measurement behavior. It is a third *access path*: (1) local physical IVI/SCPI, (2) local stateful simulation/mock, or (3) a remote proxy to a host whose own backend is physical or simulated.

```text
  User C# procedure / experiment runner / integrated step editor
               |
     Strongly typed IVI-aligned instrument class API
               |
      typed operation/parameter definitions (UnitsNet)
               |
           Instrument factory
               |
     +---------+----------+
     |                    |
 local implementation   remote proxy (versioned gRPC)
     |                    |
 +---+----+          instrument host
 |        |               |
 IVI/SCPI mock        leases, auth, logging
 hardware model           |
                      +---+----+
                      |        |
                    IVI/SCPI   mock
                    hardware   model
```

Each operation has a stable ID, parameter types, physical dimension, capability requirement, allowed state, and result schema. The same operation definition is used by the real adapter, mock behavior and remote transport. A host must validate the operation, lease and capability regardless of what a client compiler accepted.

**Never duplicate vendor SCPI strings in remote client proxies.** Client sends typed operation ID + typed values; host maps to its local approved driver. The client does not need the server's driver assembly version. The server does not deserialize CLR/UnitsNet objects: use versioned protobuf numeric quantities with explicit unit identifiers, measurement basis and metadata.

The factory resolves **two orthogonal settings**:
- **Access:** local vs remote.
- **Backend:** physical vs simulated/strict mock.

All measurements carry both provenance fields. `Remote+Simulated` is valid; `Remote` alone is not a measurement provenance category.

### Compile-time and runtime checks
- Typed C# `IInstrumentParameter<T>` / `SetAsync<T>` prevents mixing `Frequency`, `ElectricPotential`, `ElectricResistance`, etc. Thin generated methods can provide `SetFrequencyAsync` IntelliSense.
- Model-specific ranges, firmware capability, driver package version, selected DMM mode and physical resource availability are validated at runtime.
- A `MeasureAsync()` returning different dimensions according to mode needs a discriminated `Measurement` result (or mode-specific methods such as `MeasureVoltageAsync`); it must not claim a universal `ElectricPotential` return type.
- Per-instrument reservations and per-session serialization are enforced by the owning host, not the factory alone.
- Stateful mock models share a simulation clock; client-side protocol mocks can separately inject network errors and stale leases.
- Complex acquisitions and unusual vendor commands can use handwritten adapters or partial classes; don't force them into property factories.

## 2. Source evidence and scope

The following **25 real PyMeasure property definitions** were read directly from three upstream files via the GitHub repository connector. These are **25 operations/properties, not 25 different instruments**. The table retains their real property names and query/set SCPI mappings, and describes what they imply for BenchDouble. This is an architectural source audit, **not** a claim that these instruments or commands have been ported or hardware-tested.

**Source A:** Agilent 34410A DMM, commit/blob `ab0e083298b408d4eb5b46ecbd30eccb6e2575d9`, [source](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/agilent/agilent34410A.py), lines 41–52.

**Source B:** Agilent 33500 series waveform generator, blob `d4045fe538e9e59cdcceb477ae43497ddade52e4`, [source](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/agilent/agilent33500.py), lines 54–145.

**Source C:** Keithley 2000 DMM, blob `bc174b7ba4b2343b85744472e60736a6714d8afa`, [source](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2000.py), lines 60–147.

PyMeasure is MIT licensed; copyright (c) 2013–2026 PyMeasure Developers. This document references operation names and command mappings for comparative study rather than copying complete driver source. Any future substantial code adaptation must retain the upstream copyright and license notices.

### A. Agilent 34410A: six read-only measurement factories

All six are `Instrument.measurement` properties. The `DEF,DEF` arguments in the original queries request instrument defaults; these are **immediate measurement commands**, not necessarily interchangeable with a separate Configure/Initiate/Fetch lifecycle.

| # | PyMeasure property | Actual SCPI query | Typed BenchDouble result | Proposed mock behavior |
|---|---|---|---|---|
| 1 | `voltage_dc` | `MEAS:VOLT:DC? DEF,DEF` | `ElectricPotential` (DC) | DC source with seeded noise |
| 2 | `voltage_ac` | `MEAS:VOLT:AC? DEF,DEF` | `ElectricPotential` (AC RMS basis) | RMS signal model |
| 3 | `current_dc` | `MEAS:CURR:DC? DEF,DEF` | `ElectricCurrent` | Current source |
| 4 | `current_ac` | `MEAS:CURR:AC? DEF,DEF` | `ElectricCurrent` (AC RMS basis) | AC current model |
| 5 | `resistance` | `MEAS:RES? DEF,DEF` | `ElectricResistance` | 2-wire resistance with leads |
| 6 | `resistance_4w` | `MEAS:FRES? DEF,DEF` | `ElectricResistance` | Four-wire resistance model |

### B. Agilent 33500: ten channel-aware read/write factories

All ten are `Instrument.control` properties in the channel class; `{ch}` is a **PyMeasure channel placeholder**, not our current generator's `{channel}`. Numeric ranges are as declared in this upstream driver and must be verified against actual model/firmware, load and waveform before use.

| # | Property | Actual query → setter template | Source behavior/validation | Proposed BenchDouble type |
|---|---|---|---|---|
| 7 | `shape` | `SOUR{ch}:FUNC?` → `SOUR{ch}:FUNC %s` | Allowed SIN/SQU/TRI/RAMP/PULS/PRBS/NOIS/ARB/DC; dynamic | Waveform enum |
| 8 | `frequency` | `SOUR{ch}:FREQ?` → `SOUR{ch}:FREQ %f` | strict range 1e-6 to 120e6 Hz; dynamic | `Frequency` |
| 9 | `amplitude` | `SOUR{ch}:VOLT?` → `SOUR{ch}:VOLT %f` | strict range 0.01 to 10; dynamic | `ElectricPotential` + basis |
| 10 | `amplitude_unit` | `SOUR{ch}:VOLT:UNIT?` → `SOUR{ch}:VOLT:UNIT %s` | VPP/VRMS/DBM; dynamic | amplitude-unit enum with dimension-aware handling |
| 11 | `offset` | `SOUR{ch}:VOLT:OFFS?` → `SOUR{ch}:VOLT:OFFS %f` | strict range -4.995 to 4.995 V; dynamic | `ElectricPotential` |
| 12 | `voltage_high` | `SOUR{ch}:VOLT:HIGH?` → `SOUR{ch}:VOLT:HIGH %f` | strict range -4.999 to 5 V; dynamic | `ElectricPotential` |
| 13 | `voltage_low` | `SOUR{ch}:VOLT:LOW?` → `SOUR{ch}:VOLT:LOW %f` | strict range -5 to 4.999 V; dynamic | `ElectricPotential` |
| 14 | `phase` | `SOUR{ch}:PHAS?` → `SOUR{ch}:PHAS %f` | strict range -360 to 360 degrees | `Angle` |
| 15 | `square_dutycycle` | `SOUR{ch}:FUNC:SQU:DCYC?` → `SOUR{ch}:FUNC:SQU:DCYC %f` | strict range 0.01 to 99.98 percent; dynamic | percent/ratio |
| 16 | `ramp_symmetry` | `SOUR{ch}:FUNC:RAMP:SYMM?` → `SOUR{ch}:FUNC:RAMP:SYMM %f` | strict range 0 to 100 percent; dynamic | percent/ratio |

**Important semantic exception (#10):** `DBM` is a logarithmic power level, not a voltage unit. Its conversion to volts depends on load/reference impedance. Our typed API must not treat `DBM` as an `ElectricPotential` unit.

### C. Keithley 2000: nine stateful DMM controls and measurements

The `mode` property is a mapped SCPI control. A read of `current` uses `:READ?` and **depends on the configured mode**; this is the clearest evidence that a mock needs a state machine rather than a canned query dictionary.

| # | Property | Actual query → setter template | Source behavior/validation | Mock/typed design implication |
|---|---|---|---|---|
| 17 | `mode` | `:CONF?` → `:CONF:%s` | mapped discrete measurement modes | State transition controlling result dimension |
| 18 | `beep_state` | `:SYST:BEEP:STAT?` → `:SYST:BEEP:STAT %g` | enabled=1, disabled=0 mapping | `bool`/enum and state update |
| 19 | `current` | `:READ?` | measurement; active mode determines DC/AC current | Typed result requires mode validation |
| 20 | `current_range` | `:SENS:CURR:RANG?` → `:SENS:CURR:RANG:AUTO 0;:SENS:CURR:RANG %g` | truncated 0–3.1 A; setter disables auto range | Compound state transition, not simple scalar set |
| 21 | `current_reference` | `:SENS:CURR:REF?` → `:SENS:CURR:REF %g` | truncated -3.1 to 3.1 A | Typed current reference |
| 22 | `current_nplc` | `:SENS:CURR:NPLC?` → `:SENS:CURR:NPLC %g` | integration time in power-line cycles | Integration setting and virtual delay |
| 23 | `current_digits` | `:SENS:CURR:DIG?` → `:SENS:CURR:DIG %d` | discrete 4,5,6,7 | Integer resolution setting |
| 24 | `current_ac_range` | `:SENS:CURR:AC:RANG?` → `:SENS:CURR:AC:RANG:AUTO 0;:SENS:CURR:AC:RANG %g` | truncated 0–3.1 A; disables auto range | AC-mode compound state |
| 25 | `current_ac_nplc` | `:SENS:CURR:AC:NPLC?` → `:SENS:CURR:AC:NPLC %g` | integration time in power-line cycles | AC integration behavior |

## 3. Lessons from the 25 examples

1. **Most simple operations are data.** 25 definitions use PyMeasure's three property-factory forms; most can map to typed parameter descriptors.
2. **One command can depend on state.** Keithley `:READ?` returns a measurement dependent on mode. A generated mock must implement state transitions and measurement dispatch.
3. **A setter may have multiple effects.** Range setters disable auto-ranging before setting a value.
4. **Unit symbols are insufficient.** VPP, VRMS and dBm require explicit amplitude basis and load semantics.
5. **Instrument-class abstraction differs from vendor command text.** Two meters can expose DC voltage but use different SCPI sequences.
6. **Channels need first-class identity.** PyMeasure `{ch}` placeholders must map to validated channel addresses, not arbitrary text.
7. **Ranges depend on model and configuration.** PyMeasure's declared bounds are reference data, not universal IVI guarantees.
8. **Source-driven generation should be inspectable.** Prefer explicit standalone generator writing normal `.g.cs` files when thin wrappers are helpful; do not require duplicate handwritten methods solely for IntelliSense.

## 4. Proposed implementation design

### Typed operation definition

```csharp
// Conceptual API, not currently implemented.
var frequency = InstrumentParameter.Control<Frequency>(
    id: "fgen.channel.frequency",
    read: "SOUR{channel}:FREQ?",
    write: "SOUR{channel}:FREQ {value}",
    canonicalUnit: FrequencyUnit.Hertz);
```

### Generic local and remote execution

```csharp
// Conceptual API, not currently implemented.
await using var generator =
    await instruments.OpenAsync<IFunctionGenerator>("FGEN1", ct);

await generator.Channel(1).SetAsync(frequency, Frequency.FromKilohertz(10), ct);
```

The remote proxy sends operation ID, channel, typed value, request ID and lease identity to a host; it **never** sends vendor SCPI command strings by default. The host executes the approved local driver and returns a versioned result with `Access=Remote` and `Backend=Physical|Simulated`. For safe sharing, the host owns sample publication.

### Mock model

```text
Initialize: mode=DC voltage; range=10 V; integration=1 NPLC; clock=bench; seed=42
SetMode(Resistance): state.mode <- Resistance
Measure(): state.mode selects UnitsNet ElectricResistance response behavior
Delay: shared virtual clock + instrument integration/settling time
Expected sequence: record invocation, completion, fault and causal ordering
```

The same operation definitions drive validation, UI metadata, strict expectations and mock dispatch. A mock implementation need only supply meaningful behavior/state transitions rather than reproduce a second copy of the SCPI catalog.

## 5. Engineering acceptance plan

- Create a minimal operation registry covering these 25 definitions and compile typed wrappers for selected methods.
- Implement two-mode DMM mock with voltage/resistance state and clock-aware latency.
- Implement physical SCPI adapter and remote proxy for the same typed operations; verify byte-level SCPI at the physical adapter boundary and class-level semantics at client boundary.
- Test invalid dimensions, unsupported channel, incompatible model/firmware, range, malformed operation IDs and remote version mismatch.
- Test remote+physical, remote+simulated, local+physical and local+simulated combinations.
- Include no-hardware CI, mock/network fault injection and an optional authorized real-device smoke suite.

**Implementation status:** This document and source audit are complete; it does **not** assert the 25 drivers/operations have been ported or that the generic remote dispatcher has been built.
