# Instrument Interface Research and Design

## Purpose

Design **real instrument-control interfaces** with interchangeable test doubles. This document reports established external patterns, the IVI source dependencies to study, and how BenchDouble should represent device-specific extensions without forcing every DMM or scope to support every option.

## Source projects investigated

### Official IVI Foundation — https://github.com/IviFoundation/IviDrivers
Official driver specs, shared components, .NET interface assemblies and example code. This is the **normative starting point** for instrument-class vocabulary, not a drop-in implementation of every vendor's commands.

Read the official IVI class specifications and the exact .NET interface assemblies, especially DMM, Fgen, Scope, DC Power and RF Sig Gen. Instrument class standards have common base functions, class-specific functions and extension capabilities. The newer IVI Generation 2026 driver core must not be conflated with legacy class-driver APIs.

Dependency packages already isolated in `BenchDouble.Ivi.Official`:
`IviFoundation.Driver`, `IviFoundation.Driver.Dmm`, `IviFoundation.Driver.Fgen` at version 2.1.0, and `IviFoundation.DriverCore` version 1.0.1. These packages are restored by .NET tooling; their code is **not copied** into this repository.

### external instrument source — https://github.com/external instrument source/external instrument source
Useful production patterns: instrument base class, transport adapter, channels, declarative parameters with validation, common SCPI mixins, and a sizable catalog with real device examples (Agilent/Keysight 33500 family, several DMMs, Tektronix and Siglent oscilloscopes). Strong inspiration for separating **instrument feature modeling** from low-level I/O. external instrument source also has FakeInstrument support for hardware-free tests.

### QCoDeS — https://github.com/microsoft/Qcodes
Useful parameter model: validated get/set parameters, channel lists, hierarchy, metadata snapshots, units, and parameterized measurement workflows. Consider adopting its **design patterns**, not a Python dependency.

### PyVISA / pyvisa-sim — https://github.com/pyvisa/pyvisa and https://github.com/pyvisa/pyvisa-sim
Useful transport interchangeability and YAML-defined response simulators. Distinguish protocol response stubbing from a stateful electrical simulation.

### Scpi.NET — https://github.com/klasyc/ScpiNet
Pure-C# USB/Ethernet transport implementation. Its documented experience explains why generic SCPI transport alone cannot solve the high-level instrument problem: device reflection is often poor, and a complete driver API becomes huge. We should keep raw SCPI escape hatches and explicit profile metadata.

## Canonical contract decomposition

```text
IInstrument
  + Identify, Reset, Dispose, Backend, Features
       |
       + IFunctionGenerator -> IFunctionGeneratorChannel collection
       + IDigitalMultimeter
       + IOscilloscope -> IOscilloscopeChannel collection
       + (planned) IDcPowerSupply, IRfSignalGenerator, ISwitch, ICounter
       |
       + optional feature/capability interfaces:
           INplcIntegration
           IApertureIntegration
           (planned) IBurstMode, IArbitraryWaveform, ISegmentedAcquisition
           (planned) IAdvancedTriggering, IMeasurementStatistics
       |
       + IRawScpiInstrument  (explicit, optional vendor command escape hatch)
```

A Tektronix or Keysight scope implements `IOscilloscope` plus the optional features its driver actually supports. A DMM implements `IDigitalMultimeter`, and a model supporting selectable integration time can also implement `INplcIntegration` or `IApertureIntegration`. A feature keyed only by string is insufficient for compile-time validation: use narrow typed optional interfaces for standardized operations; use discoverable metadata for UI generation and diagnostics; use vendor extension objects for truly proprietary options.

### Custom fields and vendor features

Three layers:
1. **Common typed contract:** universally meaningful operations only.
2. **Optional typed extension:** e.g. integration NPLC, digital filtering, statistics, trigger burst, bandwidth limit.
3. **Vendor/model-specific extension:** strongly typed Tek/Keysight-specific capability plus an explicitly configured raw SCPI escape hatch.

Drivers must reject unsupported features; no silent no-ops. Units, min/max ranges and firmware constraints should be available through a machine-readable capability map and validated in the adapter. A future declarative instrument profile may map common functions to SCPI templates, response parsers and validators; arbitrary vendor operations stay in model-specific code.

### Important behavioral nuances

- Changing integration time may alter resolution, accuracy, acquisition duration or line-noise rejection. `INplcIntegration` must not imply all instruments can set an arbitrary NPLC value.
- Oscilloscope trace values require metadata (sample interval, time origin, vertical offset/scale, acquisition state). `double[]` in the initial interface is a minimal starting point, not the final acquisition contract.
- Some DMM commands (e.g. `MEAS?`) reconfigure acquisition state. A driver must distinguish configuring, initiating, fetching and reading; not all operations have equivalent side effects.
- VISA/SCPI are transport/protocol layers; a driver may use a vendor binary API instead.
- Scope channel numbering and feature availability are per instrument model, not assumed universal.
- Caching can change wire-level command sequences; verify functional expectations at the class interface and exact SCPI expectations separately.

## Work implemented now

- `BenchDouble.Contracts`: instrument identity, backend provenance, capability record, `IInstrument`, `IDigitalMultimeter`, `IFunctionGenerator`, `IFunctionGeneratorChannel`, `IOscilloscope`, `IOscilloscopeChannel`, and optional extension contracts.
- `BasicDmmAdapter` and `BasicFunctionGeneratorAdapter`: minimal SCPI-backed implementations using existing typed device drivers.
- Tests cover generic DMM measurement, independently addressed function-generator channels and failure on unavailable functions.

This is **source scaffolding inside the existing assembly**, deliberately not a new published package. IVI compatibility/conformance, native VISA transport and real Tektronix oscilloscope drivers remain future work.

## Remaining tasks

1. Obtain and review exact official IVI class-interface assemblies and specification files via NuGet restore / official GitHub source; record class/member mapping and distribution licenses.
2. Compile and run the existing projects with .NET 8 CI; correct any namespace/package mismatch.
3. Implement a proper measured-quantity and waveform-result type including SI units and calibration context.
4. Add `IOscilloscope` contract-first model implementation and a Tektronix concrete driver backed by documented command profiles.
5. Introduce the common instrument registry/factory and explicit real/mock/simulated backend configuration.
6. Bind `INplcIntegration` and `IApertureIntegration` to supported DMM profiles with range tests; do not advertise features not implemented.
7. Expand IVI conformance matrix to oscilloscopes, DC supplies, RF generators and switching systems.
8. Implement API compatibility tests exercising identical application workflows through actual and virtual instruments.

## References
- https://github.com/IviFoundation/IviDrivers
- https://www.ivifoundation.org/specifications/default.aspx/
- https://github.com/external instrument source/external instrument source
- https://github.com/microsoft/Qcodes
- https://github.com/pyvisa/pyvisa-sim
- https://github.com/klasyc/ScpiNet


## external instrument source source/API audit — 2026-10-14

Reviewed upstream documentation and concrete driver patterns:
- https://external instrument source.readthedocs.io/en/latest/api/instruments/instruments.html
- https://external instrument source.readthedocs.io/en/latest/dev/adding_instruments/properties.html
- https://external instrument source.readthedocs.io/en/latest/dev/adding_instruments/channels.html
- https://github.com/external instrument source/external instrument source/blob/master/external instrument source/instruments/keithley/keithley2400.py
- https://external instrument source.readthedocs.io/en/stable/api/instruments/teledyne/teledyne_bases.html

### Audit findings / corrections to our first interfaces

| Finding | Current BenchDouble | Decision |
|---|---|---|
| Transport separated from command protocol | `IScpiSession` mixes abstract session with textual SCPI | Retain compatibility API, introduce lower level message session with binary buffers, timeout, connection metadata, explicit ownership |
| Validated declarative parameter | `InstrumentFeature` is passive metadata | Add typed `IInstrumentParameter<T>` with read/write, units, constraints and device-side mapping; do not use reflection to guess capabilities |
| Channels are child devices | `IFunctionGeneratorChannel` and `IOscilloscopeChannel` are separate | Keep; add channel identity/name, parent link, capability discovery, stable channel keys and noninteger selectors |
| Optional functions vary | `INplcIntegration`, `IApertureIntegration` defined, no adapter implements them | Preserve optional feature interfaces; do **not** add mandatory NPLC member to universal DMM |
| Results have metadata | DMM `Measure()` returns double; scope returns bare `double[]` | Create `MeasurementResult` and `WaveformResult` preserving units, trigger/time, scale, quality, overload and provenance |
| Stateful operations matter | `MEAS:VOLT:DC?` in minimal adapter | Distinguish `Configure`, `Initiate`, `Fetch`, `Read`, `Measure`; side effects vary by instrument |
| SCPI common operations and error checking | Simplified handwritten models | Add separate SCPI semantics mixin, error queue and status APIs; no silently accepted unrecognized commands |
| Vendor-specific extensions | `IInstrumentExtension<T>` property only | Introduce explicit extension retrieval / capability negotiation, rather than mandatory giant interfaces |
| Parameter state/cache | Only model-specific dictionary values | Explicit cache policy so strict expected SCPI sequences are not altered unintentionally |
| Simulator validity | Logical enum defaults to Simulated | Backend provenance must be supplied by composition root, not accidentally mislabeled as simulated/physical |

### Priority engineering issues found

**P0:** Exact IVI/driver member mapping is not yet validated; do not advertise conformance.  
**P0:** The generic function-generator contract permits any frequency/amplitude that existing driver accepts (>0) without model-based maximum checks.  
**P0:** `BasicDmmAdapter.Measure()` invokes an immediate-measure SCPI query, which can reconfigure acquisition state; it is not equivalent to an IVI `Fetch` after a trigger.  
**P1:** Scope waveform contract lacks x-axis/time metadata, samples/unit, acquisition state and scaling.  
**P1:** Capability map units attach a single `Hz` label to a feature describing both frequency and amplitude; metadata should instead be per-parameter.  
**P1:** The raw SCPI escape hatch deliberately breaks driver state/cache knowledge; require an invalidation strategy.  
**P1:** Current channel IDs are assumed 1..2, which is a profile-specific detail, not generic semantics.

### Recommended shape (future interface, not implemented)

```csharp
public interface IInstrumentParameter<T>
{
    string Key { get; }
    string? Unit { get; }
    bool CanRead { get; }
    bool CanWrite { get; }
    T Get();
    void Set(T value);
}
public interface IInstrumentCapabilities
{
    bool Supports(string featureKey);
    bool TryGetParameter<T>(string key, out IInstrumentParameter<T>? parameter);
}
```

**Design rule:** generic interfaces expose universal semantics; optional feature interfaces and parameters represent advanced functions; model-specific drivers translate semantics to vendor commands. The same typed calls should target a physical instrument, stateful fake, or strict mock. Avoid premature large interface inheritance trees.

**Research status:** based on upstream public documentation and representative source files. This is not a complete line-by-line audit of every external instrument source manufacturer driver or every official IVI interface assembly.
