# 25 Real PyMeasure Instrument Drivers — BenchDouble Porting Catalog

**Status:** inventory / port plan, not 25 implemented drivers. The models below are listed in PyMeasure's published instrument catalog (2026). Source was directly inspected for entries marked **source checked**. Other entries are verified as catalog entries, but their exact source path and command mappings remain to be inspected. **Keithley 236 is not in this PyMeasure catalog**; its BenchDouble implementation comes from the manufacturer's 236/237/238 manuals.

References:
- [PyMeasure Keithley catalog](https://pymeasure.readthedocs.io/en/stable/api/instruments/keithley/)
- [PyMeasure Agilent catalog](https://pymeasure.readthedocs.io/en/stable/api/instruments/agilent/index.html)
- [PyMeasure source](https://github.com/pymeasure/pymeasure)

| # | Real instrument or family | Function | Source verification | Planned BenchDouble mapping |
|---|---|---|---|---|
| 1 | Keithley 2000 | DMM | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2000.py) | Typed DMM, NPLC, state |
| 2 | Keithley 2200 | DC supply | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2200.py) | Supply channels |
| 3 | Keithley 2260B | DC supply | Catalog listed | Supply control |
| 4 | Keithley 2281S | Battery simulator | Catalog listed | Battery model |
| 5 | Keithley 2306 | Dual-channel battery supply | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2306.py) | Multi-channel supply |
| 6 | Keithley 2400 | SourceMeter | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2400.py) | IVI-style SMU |
| 7 | Keithley 2450 | SourceMeter | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2450.py) | Modern SMU |
| 8 | Keithley 2510 | TEC SourceMeter | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2510.py) | TEC control |
| 9 | Keithley 2600 | SMU family | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2600.py) | Multi-SMU scripting |
| 10 | Keithley 2700 | DMM/switch | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2700.py) | Switch/scan |
| 11 | Keithley 2750 | DMM/switch | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2750.py) | Switch/scan |
| 12 | Keithley 6221 | Current source | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley6221.py) | Current source |
| 13 | Keithley 6517B | Electrometer | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley6517b.py) | Low-current measurements |
| 14 | Keithley DMM6500 | DMM | Catalog listed | DMM/scan |
| 15 | Keithley 2182 | Nanovoltmeter | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley2182.py) | Nanovoltmeter |
| 16 | Keithley DAQ6510 | Data logger/DMM | Catalog listed | Scan/acquisition |
| 17 | Keithley 4200A-SCS | Parameter analyzer | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/keithley/keithley4200.py) | Multi-SMU analyzer |
| 18 | Agilent 8257D | Signal generator | Catalog listed | RF generator |
| 19 | Agilent 8722ES | Vector network analyzer | Catalog listed | VNA traces |
| 20 | Agilent E4408B | Spectrum analyzer | Catalog listed | Spectrum |
| 21 | Agilent E4980 | LCR meter | Catalog listed | Impedance |
| 22 | Agilent E5062A | VNA | Catalog listed | Channel/trace |
| 23 | Agilent 34410A | DMM | [checked](https://github.com/pymeasure/pymeasure/blob/master/pymeasure/instruments/agilent/agilent34410A.py) | Typed DMM |
| 24 | Agilent 34450A | DMM | Catalog listed | Typed DMM |
| 25 | Agilent 4155/4156 | Semiconductor analyzer | Catalog listed | SMU/voltage monitor |

**Important:** Some PyMeasure entries are instrument *families* rather than individual model numbers. Count here is 25 distinct documented instrument driver entries, not 25 source-translated and verified implementations. Further work must inspect each file, license, device-specific command coverage, and hardware manuals before claiming parity.

## Keithley 236 — first independent legacy driver

PyMeasure's catalog does not list a Keithley 236 driver. BenchDouble now has an independently implemented **DC source/measure core** in `src/BenchDouble/Instruments/Keithley/Keithley236.cs` and a resistive-DUT mock in `src/BenchDouble/Simulation/Keithley236MockBus.cs`. This is **not a complete 236 driver** yet.

The 236 uses its proprietary **IEEE-488 device-dependent command language**, not SCPI. A separate bus interface supports `WriteAsync` and `ReadAsync` to model GPIB TALK behavior. Current typed operations:
- `ConfigureAsync`: standby, F0,0 / F1,0 DC source/measure selection
- `SetBiasAsync`: B(level), auto source range and delay (volts or amperes)
- `SetComplianceAsync`: L(level), auto measure range
- `SetIntegrationAsync`: S0–S3 timing selection
- `SetOperateAsync`: N0/N1 standby/operate
- `MeasureCurrentAsync` / `MeasureVoltageAsync`: G4,2,0 ASCII data format, bus read
- Unit-safe UnitsNet values, argument validation and per-operation serialization
- Mock resistive load with compliance clipping and strict unsupported-command rejection
- xUnit source tests for both source modes, transcript, limits and standby

**Hardware validation is NOT complete:** command termination/EOI, GPIB talk timing, actual response formatting, real trigger/acquisition, range encodings, compliance status, fault and interlock behavior require testing against the actual instrument.

### Remaining for full Keithley 236 coverage
1. Sweep creation/append/modify (A/Q), sweep buffer and timestamped data, triggering R/T/H.
2. Read and parse U status families, G data formats, compliance and status bits, error/warning masks.
3. Remote/local sense (O), filters (P), suppress (Z), display (D), default delay (W), EOI/holdoff (K) and terminator (Y).
4. Source and measurement fixed-range mappings and model-specific range/accuracy restrictions.
5. Integration and filter timing, physical interlock and recovery, calibration lockout, multi-point readout.
6. Actual GPIB provider with IEEE-488 TALK/EOI, timeouts, cancellation and safe-state verification.
7. Manufacturer-manual and physical-instrument acceptance suite. No physical output should be enabled until validated.

Sources:
- [Keithley 236/237/238 quick reference manual](https://www.manualslib.com/manual/4057246/Keithley-236.html)
- [Keithley 236/237/238 operator manual (Tektronix)](https://www.tek.com/en/manual/model-236-237-238-source-measure-units-operators-manual-rev-e-13-mb)

**Safety:** The Keithley 236 can source hazardous voltages/currents. The mock is not a safety interlock. The current driver's output-enable call requires explicit caller authorization and a verified test fixture before real use; software-only checks do not prove electrical safety.

## Machine-readable catalog and generated-mock increment (2026-10-15)

The 25 listed PyMeasure driver entries now have a [machine-readable inventory](../instrument-definitions/instrument-catalog.json) with model/class, source-evidence level, definition/implementation status and hardware-validation flag. All 25 remain **catalog-only/not ported**. The separately implemented Keithley 236 remains a partial, unverified legacy IEEE-488 driver. The new [definition-driven generator](INSTRUMENT-DEFINITIONS-AND-MOCK-GENERATION.md) emits both a UnitsNet-typed SCPI driver and matching default mock from a validated `*.instrument.json` file; only the illustrative Keysight 33522B schema currently participates. Keithley 236 now invalidates old compliance/bias on mode changes, checks mode under the bus serialization lock, and requires source mode, bias and compliance before OPERATE. These are source-level safety improvements, **not hardware qualification**. Follow issues #19 and #44 for remaining driver and generator work.
