# Strongly typed physical quantities

## Decision
Use **UnitsNet 5.75.1** (stable branch) as the foundation for physical quantities instead of inventing volts/ohms/amps/conversion routines. UnitsNet v6 is prerelease as of this decision. Existing double-returning APIs are retained for compatibility; new application-facing instrument contracts will return quantity types.

## Typed API
- DC voltage: `UnitsNet.ElectricPotential` (e.g. `ElectricPotential.FromVolts(5)`)
- Resistance: `UnitsNet.ElectricResistance` (e.g. `ElectricResistance.FromOhms(1000)`)
- Current: `UnitsNet.ElectricCurrent`
- Frequency: `UnitsNet.Frequency`
- Temperature: `UnitsNet.Temperature`
- Elapsed durations: `UnitsNet.Duration` where physically meaningful; framework timeouts may continue using `TimeSpan`.

Current `TypedDmmAdapter` implements voltage only, explicitly rejects resistance/current until model-supported SCPI drivers are available. `TypedFunctionGeneratorAdapter` converts Hz and volts peak-to-peak into existing generator driver operations.

## Measurement values vs metadata
A typed quantity represents numerical magnitude and unit conversion. It does **not** communicate:
- AC/DC measurement mode
- RMS vs peak-to-peak vs peak voltage semantics
- measurement uncertainty and resolution
- acquisition timestamp accuracy
- overload / stale sample / instrument error
- instrument provenance / calibration information

BenchDouble measurement records therefore retain source, timestamp and quality separately. Proposed next contracts should encode amplitude basis (Vpp, peak, RMS) rather than conflate them even though all use volts.

## Wire protocol
Do **not** serialize arbitrary UnitsNet CLR objects between clients/hosts. Use stable DTOs containing quantity identifier, numeric value in canonical unit (or declared enum), unit symbol/code, optional measurement basis, timestamp, quality, and schema version. Validate dimensions on deserialization; reject ohms supplied to a voltage field. UI may display localized, user-selected units.

## Extending UnitsNet
Prefer UnitsNet's quantity/extension mechanism and upstream contributions for broadly useful engineering units. Maintain niche instrument metadata such as dBm reference impedance, NPLC, ADC counts, or decibels with explicit reference quantities in BenchDouble-specific extensions. Avoid forking all of UnitsNet simply for a proprietary unit.

## Tests and completion
- Verify volts/millivolts, kHz/Hz, ohms/kOhms, Celsius/Kelvin, and invalid dimension rejection.
- Extend typed interfaces for DMM current/resistance and async acquisitions once real driver features exist.
- Add unit-aware range validators and measurement records in experiment result sinks.
- Use a versioned protobuf quantity schema for distributed instrument hosts.
- Test physical quantities in mocks and IVI bridges with identical semantics.

**Current status:** typed facades and unit conversion tests committed, .NET builds not yet verified.

## Mock measurements and simulated time

Mock configuration and generated behaviors must use UnitsNet for voltage, resistance, current, frequency and duration. Record **acquisition simulation timestamp** separately from **wall-clock timestamp** and retain physical quantity, quality, waveform basis, seed and provenance. Integration/warmup/latency durations are explicit; simulation speed changes wall execution pacing without changing simulated physical dynamics. Neither UnitsNet quantity conversion nor random noise alone establishes calibrated measurement uncertainty. See [mock lifecycle and clock specification](MOCK-TIME-AND-LIFECYCLE.md).

## UnitsNet-to-plotting adapter

The [plotting layer](PLOTTING-AND-UNITS.md) defines `QuantityAxis<TQuantity>` so frequency, voltage, resistance, current, temperature and duration are converted to selected display units only at visualization time. `MeasurementPlotSeries` preserves axis units and physical dimension labels; `PlotlyFigure` exports Plotly.js JSON while an optional ScottPlot package generates PNG. Basis (RMS/peak/Vpp), quality, uncertainty and source timestamps must remain separate from physical magnitude; multi-trace dimensional validation, quality/error bars and live subscriptions are still pending.
