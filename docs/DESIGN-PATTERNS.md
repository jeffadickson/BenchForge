# Design Patterns for BenchDouble
**Status:** architecture guidance. Separate existing code from proposed patterns and avoid indiscriminate abstraction.

## Context
BenchDouble controls **real** lab instruments while also running hardware-free tests. The same client code must be able to use VISA/SCPI, official IVI/vendor drivers, scripted mocks or stateful simulators. The design should avoid a giant lowest-common-denominator instrument API, repetitive driver SCPI plumbing, and hidden hardware side effects.

## 1. Abstract Factory + Dependency Injection
**Purpose:** resolve a logical instrument name such as `DMM1` or `FGEN_A` into the proper class interface and explicitly selected backend.

`IInstrumentFactory.OpenDmm("DMM1")` should produce a typed instrument, based on a provider configuration for physical, simulated or mocked operation. This is **planned**, not yet implemented. A registry/factory must show the resolved backend in logs; never silently substitute hardware for a fake or vice versa.

## 2. Adapter
**Purpose:** map different vendor/IVI representations into common class contracts. Existing `BasicDmmAdapter`, `BasicFunctionGeneratorAdapter`, `ScpiDigitalMultimeterAdapter`, and the optional official IVI bridges already demonstrate it.

Adapters should translate errors, standard capability semantics, unit conventions and lifecycle. They should **not** claim that every driver supports every IVI extension.

## 3. Strategy
**Purpose:** independently replace algorithms behind the same contract.
- `IScpiSession` chooses the communication strategy (real VISA later, fake or strict transcript today).
- `IScpiCodec<T>` chooses conversion between C# values and instrument wire syntax.
- Future retry/timeout and validation policies should also be explicit strategies.

The existing `DoubleCodec` and `BooleanCodec` are implemented examples.

## 4. Parameter descriptor / metadata-driven driver
**Purpose:** minimize repetitive SCPI command encoding and validation, inspired by PyMeasure `control()`, `measurement()` and `setting()`.

Implemented `ScpiParameter.Control`, `Measurement`, `Setting` create immutable typed descriptors. `BoundScpiParameter<T>` binds them to a session/channel and performs Get/Set, validation, parsing, wire formatting. This is **composition** rather than runtime-generated .NET properties.

This is a **domain-specific declarative descriptor**, not literally a GoF design pattern. It should preserve generated API documentation and machine-readable capabilities for future UI/test tooling.

## 5. Composite
**Purpose:** represent repeatable capabilities and a hierarchy: instrument -> channel group -> channel -> parameter. Existing `ScpiChannelGroup` / `ScpiChannel` demonstrate a small, explicit hierarchy.

Future scope supports arbitrary instrument channel IDs, subgroup channels (e.g. trace/math/ref), and trigger/acquisition resources. Avoid building an unrestricted tree solely for cleverness; match how the hardware actually behaves.

## 6. Decorator
**Purpose:** wrap a production session with orthogonal operations while leaving instrument drivers unchanged:
- audit tracing, bounded/redacted diagnostics;
- optional timeout/retry policy (only for explicitly idempotent operations);
- strict interaction verification/record/replay;
- OpenTelemetry spans.

**Planned.** A retry decorator must not blindly repeat a trigger, an output toggle or a write with non-idempotent side effects. Cache decorators must be invalidated after raw SCPI and reset.

## 7. Command + Interpreter
**Purpose:** represent individual SCPI transactions and eventually parse command trees. Current `ExpectedExchange` is a lightweight command record; `StrictSession` acts as a simple ordered interpreter.

For a full SCPI command engine, include query vs write, binary payloads, status, response types, and model-specific command support. A universal SCPI parser is not a substitute for per-model features.

## 8. State and State Machine
**Purpose:** accurately model interlocks, output states, triggering, acquisition and measurement readiness.

Existing `GeneratorSimulator` and `MultimeterSimulator` are elementary stateful fakes. Full DMM acquisition should distinguish Configure -> Initiate -> Fetch / Read / Measure. Safety requirements must be specified as state invariants; do not confuse an issued command with a confirmed physical safe condition.

## 9. Observer / Event Stream
**Purpose:** publish instrument commands, replies, state transitions and faults to diagnostics without embedding logging in drivers. **Planned.** Structured events and trace correlation should feed an OpenTelemetry adapter, not tie core interfaces to one observability package.

## 10. Builder
**Purpose:** express the expected protocol flow declaratively with strict ordering, unordered regions, optional calls, cardinality and fault injection. **Planned.** A scenario builder produces an immutable expectation graph; execution and verification stay separate.

## 11. Specification
**Purpose:** validate requirements and optional capabilities as composable, testable rules. Examples: frequency within allowable range for the selected model; DMM NPLC in supported discrete settings; triggering mode supported by firmware. A reusable validator can decide whether a request is legal *before* SCPI is emitted. Basic `ParameterMetadata` min/max and custom `Validator` implement a first step.

## 12. Anti-patterns and boundaries
- Do not create a monstrous `IInstrument` with every possible feature.
- Do not pretend a common parameter name makes measurements from two vendors semantically identical.
- Do not require all devices to support SCPI; IVI can be backed by vendor SDK calls.
- Do not make retries automatic for commands with potential physical side effects.
- Do not leak native vendor API types into portable instrument contracts.
- Do not turn parameter factories into a covert code generator: generated drivers require schema validation, tests and inspection.
- Do not advertise unsupported profile ranges or claim a simulator is calibrated.
- Do not infer hardware vs mock from a driver name. Require explicit provenance.

## Suggested next increment
1. Stabilize descriptor validation and tests and get green cross-platform builds.
2. Convert one actual device driver (e.g. generator channel frequency/voltage) to descriptors with provenance-correct metadata.
3. Add immutable measurement result types, units, status, uncertainty policy, then channel-scoped descriptors.
4. Add factory/registry and tracing decorator separately from instrument behavior.
5. Add hierarchical Tektronix oscilloscope driver and optional vendor extensions.
