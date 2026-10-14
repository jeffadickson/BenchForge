# Instrument Interface Research and Design

## Purpose

Design **real instrument-control interfaces** with interchangeable test doubles. This document reports established external patterns, the IVI source dependencies to study, and how BenchDouble should represent device-specific extensions without forcing every DMM or scope to support every option.

## Source projects investigated

### Official IVI Foundation — https://github.com/IviFoundation/IviDrivers
Official driver specs, shared components, .NET interface assemblies and example code. This is the **normative starting point** for instrument-class vocabulary, not a drop-in implementation of every vendor's commands.

Read the official IVI class specifications and the exact .NET interface assemblies, especially DMM, Fgen, Scope, DC Power and RF Sig Gen. Instrument class standards have common base functions, class-specific functions and extension capabilities. The newer IVI Generation 2026 driver core must not be conflated with legacy class-driver APIs.

Dependency packages already isolated in `BenchDouble.Ivi.Official`:
`IviFoundation.Driver`, `IviFoundation.Driver.Dmm`, `IviFoundation.Driver.Fgen` at version 2.1.0, and `IviFoundation.DriverCore` version 1.0.1. These packages are restored by .NET tooling; their code is **not copied** into this repository.

### PyMeasure — https://github.com/pymeasure/pymeasure
Useful production patterns: instrument base class, transport adapter, channels, declarative parameters with validation, common SCPI mixins, and a sizable catalog with real device examples (Agilent/Keysight 33500 family, several DMMs, Tektronix and Siglent oscilloscopes). Strong inspiration for separating **instrument feature modeling** from low-level I/O. PyMeasure also has FakeInstrument support for hardware-free tests.

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
- https://github.com/pymeasure/pymeasure
- https://github.com/microsoft/Qcodes
- https://github.com/pyvisa/pyvisa-sim
- https://github.com/klasyc/ScpiNet
