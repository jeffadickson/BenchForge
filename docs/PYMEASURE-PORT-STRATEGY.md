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
