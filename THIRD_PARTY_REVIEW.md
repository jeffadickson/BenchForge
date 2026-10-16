# Third-party source review — centralized register

BenchForge's generated C# API documentation, generated code comments, and product-facing examples should be source-neutral. Source provenance, attribution, and license-review evidence are consolidated in this top-level register, rather than repeated in generated files.

**Legal review pending.** The instrument import corpus was derived from a third-party upstream repository (PyMeasure, https://github.com/pymeasure/pymeasure). The uploaded archive's precise Git revision has not been verified. The current instrument-definition metadata and extraction reports may also contain source references; these are retained as internal traceability data, not statements of license approval.

This register is a tracking document, **not** a replacement for any legally required copyright notice, license text, or attribution. Do not redistribute imported third-party source or remove existing required notices until legal review determines the obligations.

Review checklist: identify source and version, verify archive provenance, identify copied versus independently described material, establish license and notice requirements, record counsel's disposition, then apply any required release packaging and notices here.


## Archived source-comparison notes (internal legal review only)

### Historical research: docs/PYMEASURE-FULL-IMPORT.md

# Full PyMeasure catalog extraction

This is the reproducible full-repository intake workflow for BenchForge. It does **not** claim that all PyMeasure drivers are ported or hardware-validated.

## 1. Pin upstream source

```sh
git clone https://github.com/pymeasure/pymeasure.git external/pymeasure
git -C external/pymeasure rev-parse HEAD
```

Record the resulting 40-character revision. Do not run arbitrary upstream driver code. Respect upstream license and attribution.

## 2. Inventory every source class

```sh
python3 tools/InstrumentDefiner/discover_catalog.py \
  --source-root external/pymeasure \
  --manifest build/pymeasure-all-classes.csv \
  --inventory build/pymeasure-inventory.json
```

The inventory includes every top-level Python class under `pymeasure/instruments`, even helper and channel classes, and reports parse failures rather than silently omitting files. Each class is a **candidate**, not necessarily an instrument model. Reviewers must classify helper classes, inheritance, aliases, dynamic descriptors, mixins and model families before generating production drivers.

## 3. Batch extract candidate instruments

`batch_extract.py` accepts at most 500 rows per run. Split the reviewed manifest into batches of 500 or fewer; select actual instrument classes, not helpers, and use unique manufacturer/model output names.

```sh
python3 tools/InstrumentDefiner/batch_extract.py build/pymeasure-batch-001.csv \
  --source-root external/pymeasure \
  --output-dir instrument-definitions/drafts/pymeasure-batch-001 \
  --report build/pymeasure-batch-001-report.json
```

The batch report includes extracted methods/capabilities and unresolved inherited/dynamic members. Review all failures and zero-capability candidates; do not treat successful AST parsing as full semantic coverage.

## 4. Review and promote

Use `review_gate.py` to validate source provenance, unit and command semantics, manufacturer manual references, inheritance reconciliation and review status. Unreviewed drafts must never be promoted to physical-control drivers. Preserve an immutable source revision and exact source paths for each extracted definition.

## Current execution status

The complete upstream PyMeasure repository could not be cloned into the current execution environment because its network DNS resolution failed. The GitHub connector can read individual files, but that is not equivalent to a full checkout and comprehensive inventory. Consequently, **no claim of complete repository extraction is made**. The committed discovery script makes that full pass reproducible on a network-enabled machine or CI runner. The schema-v2 generator and hardware validation remain separate milestones.


---

### Historical research: docs/PYMEASURE-PORT-STRATEGY.md

# PyMeasure and IVI: selective C# adaptation strategy

## Decision
**Use both:** IVI is the reference for standardized instrument-class semantics and existing vendor driver interoperability; PyMeasure is the implementation reference for ergonomic driver definitions and its catalog of practical SCPI command mappings. We should *not* translate PyMeasure's entire Python project into C#.

BenchDouble is a **production instrument-control framework with interchangeable real hardware, stateful simulation, and strict mocks**. Porting all of PyMeasure would import its experiments, workers, GUI/plotting, Python descriptors and PyVISA runtime assumptions, which are not appropriate as the core .NET API. Instead, design idiomatic C# parameter descriptors and adapt selected real device profiles with regression tests.

## Actual upstream files inspected
- `pymeasure/pymeasure` repository
- `LICENSE.txt` — MIT License, copyright (c) 2013–2026 PyMeasure Developers
- `pymeasure/instruments/instrument.py` — instrument abstraction, adapter separation
- `pymeasure/instruments/common_base.py` — `control()`, `measurement()`, `setting()`, hierarchical channels, response processing
- `pymeasure/instruments/validators.py` — range/discrete validators and helper semantics

Source: https://github.com/pymeasure/pymeasure

The files were inspected via the GitHub connector. A full local repository clone was **attempted but failed** because the runtime could not resolve github.com; do not claim the entire project has been downloaded or audited.

## Comparison
| Area | IVI | PyMeasure | BenchDouble direction |
|---|---|---|---|
| Instrument class | Standard DMM/FGEN/scope class behavior | Concrete model drivers | IVI-aligned typed C# contracts |
| Hardware support | Vendor official drivers + VISA stack | PyVISA-backed adapters | Optional real IVI/VISA providers |
| Declarative property | Less central to interface story | `control`, `measurement`, `setting` | `ScpiParameter` immutable descriptor + bound parameter |
| Channel hierarchy | Repeated capability conventions | ChannelCreator and MultiChannelCreator | Explicit typed channel objects with template hierarchy |
| Validation | Range checking/capabilities | Validators, mapped values | `ParameterValidators`, `MappedCodec<T>`, capability profiles |
| Testing | IVI simulation options | FakeInstrument and fake adapters | Strict EasyMock scripts + stateful models |
| Experiment workflows | Not focus | Procedures, graphing, UI | Separate TestForge/application layer |

## Initial independently implemented C# features
- Typed parameter factory with get/set templates and metadata.
- `DoubleCodec`, `BooleanCodec`, `MappedCodec<T>` for wire token translation.
- `ParameterValidators.InclusiveRange`, `Discrete`, `All`, `Any`.
- Basic channel binding and tests.
- Existing generic DMM/FGEN interfaces, IVI bridges, logging decorator and strict simulator.

These features are inspired by PyMeasure's public design, not an automatic source translation. Any later directly ported driver algorithms or substantive code should preserve MIT copyright notices, trace original upstream paths/commits, and be reviewed for device manuals and hardware-side safety.

## Next concrete ports
1. Manufacturer SCPI profile for Keysight 33500 waveform generator; waveform selection, limits, output load, amplitude/offset and multi-channel tests.
2. Keysight 34461A/34410A DMM profile: NPLC, aperture, ranges, triggering, errors, status and measurement semantics.
3. Tektronix scope waveform transfer: trigger, preamble, binary block decoding, units and timebase.
4. NI/Keysight VISA transport implementations behind optional packages.
5. Driver catalog documentation and coverage matrix.

Each model profile must be checked against a device programming guide, with a command-response and capability matrix, negative tests, and optional real-hardware smoke tests.

## Licensing/attribution
PyMeasure is MIT-licensed. Its license permits adaptation and redistribution when the required copyright and permission notices accompany copied or substantial portions. Official IVI packages/specifications have their own licensing; don't assume MIT applies to them. This document does not import PyMeasure sources or relicence them.


---

### Historical research: docs/PYMEASURE-COMPARISON-STATUS.md

# BenchDouble vs PyMeasure — Evidence-Based Capability Comparison

**Date:** 2026-10-15. **Do not claim feature superiority without implementation and verification evidence.**

| Capability | BenchDouble status | PyMeasure context | Conclusion |
|---|---|---|---|
| Extensive production instrument driver catalog | Keithley 236 partial DC core; 25 PyMeasure entries inventoried, not ported | Broad existing Python instrument driver ecosystem | PyMeasure ahead in established driver coverage |
| Typed UnitsNet quantities | Initial typed C# APIs and plotting adapter committed | Python values/validators and property factories | BenchDouble has a distinctive typed .NET approach |
| Programmable mock behaviors | DI strategy, virtual clock, timed faults committed | Different approach; feature-level comparison needs source review | Potential differentiation, not established overall superiority |
| Multi-instrument virtual physics | Typed signal graph, thermal sensor, experiment composition prototype | Not a primary PyMeasure design goal | Architectural differentiation |
| Remote instrument hosts | Local host prototype, reservations; production auth/leases incomplete | PyMeasure typically uses VISA adapters | Not yet production comparable |
| Experiment execution | Console examples; integrated step editor and signoff incomplete | PyMeasure has experiment/worker/GUI support | PyMeasure has more mature workflow capabilities today |
| Plotting | ScottPlot/Plotly adapters and standalone HTML demos | Python/Matplotlib ecosystem | Both viable, maturity differs |
| Persistence | Local SQLite time-series recorder committed | Experiment results/data support | Need benchmark and end-to-end evidence |
| Automated requirements | StrictDoc baseline, Reqnroll/xUnit/Playwright source | Outside PyMeasure's primary focus | Differentiation in project workflow, not instrument functionality |

**Assessment:** BenchDouble's proposed **architecture** spans additional concepts—typed DI-composed experiments, simulated physical coupling, host discovery, local persistence and formal requirements—but these do not establish that it has surpassed PyMeasure as a functioning instrument-control product. Hardware driver coverage, production reliability, integrated editor, packaging and successful CI evidence are the main gaps.

For a credible comparison, use the same representative bench (DMM, function generator, SMU, chamber), run real device scenarios and record driver completeness, code size, latency, fault handling, reproducibility, installation effort and support across operating systems. See [issue #39](https://github.com/jeffadickson/BenchDouble/issues/39).


---

### Historical research: docs/PYMEASURE-REMOTE-RELIABILITY-REVIEW.md

# PyMeasure remote reliability review

Reviewed 2026-10-15 at upstream commit
`f095b3742691709b75ba12f309498030d7a963cc`.

Sources: [Worker](https://github.com/pymeasure/pymeasure/blob/f095b3742691709b75ba12f309498030d7a963cc/pymeasure/experiment/workers.py),
[Listener](https://github.com/pymeasure/pymeasure/blob/f095b3742691709b75ba12f309498030d7a963cc/pymeasure/experiment/listeners.py),
[Procedure](https://github.com/pymeasure/pymeasure/blob/f095b3742691709b75ba12f309498030d7a963cc/pymeasure/experiment/procedure.py),
[Worker API](https://pymeasure.readthedocs.io/en/latest/api/experiment/workers.html),
[Listener API](https://pymeasure.readthedocs.io/en/latest/api/experiment/listeners.html).

| Question | Finding in inspected path |
|---|---|
| Experiment exceptions? | Worker catches execution exceptions, emits errors and updates failed status. |
| Cleanup? | Worker invokes shutdown in finally; Procedure provides the device-specific shutdown hook. |
| Remote operation? | Worker publishes results/status over optional ZMQ PUB; Listener subscribes with ZMQ SUB. This is telemetry distribution, not the instrument ownership protocol we require. |
| Heartbeat and fenced leases? | No bidirectional health/renewal/ownership-generation mechanism found in these modules. |
| Durable command acceptance/status lookup? | No command ledger with accepted/started/completed receipts found in this path. |
| Shutdown failure isolation? | Worker.shutdown calls procedure.shutdown before stopping recorder and network resources; an exception there can interrupt the later cleanup statements. |
| Safe retries and uncertainty? | No operation-classified replay or crash-window reconciliation found in this path. |

Listener uses cloudpickle deserialization. That protocol should not be copied as
an untrusted network boundary. BenchDouble's protocol should carry versioned,
validated data contracts, authenticated ownership and explicit outcome evidence.

Scope: source inspection of these three upstream modules plus official API pages;
no PyMeasure runtime or hardware tests, and no assertion about all plugins,
adapters or external deployments. The earlier phrase “remote framework” was too
broad for the Worker/Listener facility. We can reuse the conceptual separation
of execution and observation, but must build and verify the stronger ownership,
health and command semantics ourselves. BenchDouble does not yet satisfy them.


---

### Historical research: docs/PYMEASURE-25-INSTRUMENTS-AND-KEITHLEY-236.md

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


---

### Historical research: docs/INSTRUMENT-OPERATIONS-25-PYMEASURE-EXAMPLES.md

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
