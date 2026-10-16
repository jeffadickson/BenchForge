# Declarative instrument profiles and C# driver generation
Status: chosen design and first machine-readable sample; production source generator not implemented.

## Decision
A **validated description is the source of truth for instrument-specific SCPI mappings**, while IVI class interfaces are the source of truth for common device semantics. A Roslyn **incremental** source generator produces idiomatic typed async C# façade methods, UnitsNet quantities, validation, XML documentation, capability descriptors and a matching mock test fixture. Bespoke extension classes remain handwritten partial classes. Runtime dictionaries are an optional dynamic escape hatch, not the normal production programming model.

## Sources
- Roslyn incremental generator cookbook: https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md
- external instrument source control/measurement/setting: https://external instrument source.readthedocs.io/en/latest/dev/adding_instruments/properties.html
- IVI instrument class specifications: https://www.ivifoundation.org/specifications/default.aspx/

## Profile schema v1
See `instrument-definitions/keysight/33522b.instrument.json`. Fields include stable schema version; manufacturer, model, class and driver name; channel count/template; parameter names/kinds; quantity and unit identifier; read/write templates; numeric bounds; description; measurement basis. All unverified bounds MUST be explicitly annotated as illustrative. Future fields: feature availability predicates by model/firmware, enums, inverse conversions, SCPI error check policy, compound/multipart operations, binary waveform data, read/write side effects, safety categories, trigger state machine, channel mappings and commands with multiple arguments.

## Generator rules
1. Use `AdditionalFiles` `*.instrument.json` as input to `IIncrementalGenerator`, parse with versioned schema and produce deterministic source.
2. Never interpolate untrusted source text as identifiers or C# code. Strictly validate IDs, known quantity types, wire templates, invariant number formatting, bounds and channel placeholders.
3. Emit diagnostic IDs for malformed or unsafe schema. Generated APIs preserve UnitsNet quantity types, async cancellation and explicit Vpp/RMS/peak meaning.
4. Generated methods invoke the common `IAsyncScpiSession` and shared validators; communication, reservations, transport logs and mocks remain separate.
5. Generate associated command transcript fixtures, positive/negative validation tests and source traceability comments.
6. Allow handwritten vendor-specific code through explicit `partial` extension classes, not edits to generated files.
7. Preserve model-specific original license/attribution when derived from external instrument source. Capture source URL, commit, file, command manual and verification level.
8. A host advertises driver manifest/schema/hash; cross-version protocol is independent of specific generated C# assemblies.

## Example target API (not yet generated)
```csharp
await generator.Channel(2).SetFrequencyAsync(Frequency.FromKilohertz(10), ct);
await generator.Channel(2).SetAmplitudeVppAsync(ElectricPotential.FromMillivolts(500), ct);
```

## Generation architecture
```text
versioned instrument profile JSON
  -> schema + semantic validator
  -> Roslyn incremental source generator
       -> strongly typed C# driver + docs + capability metadata
       -> generated mock fixture / command compatibility table
  -> compile normally with .NET and existing common runtime
```

## Why not just a dictionary?
An untyped dictionary is convenient for discovery and arbitrary commands, but string names and `object` values lose compile-time type checking. A validated description with generated code retains ease of authoring **and** strongly typed usability. We can expose descriptor metadata at runtime for web UI and instrument introspection without exposing it as the primary application API.

## What to prototype before catalog conversion
- Build minimal generator with 33522B and 34461A profiles.
- Compare generated code against manually written drivers, verify SCPI transcripts and UnitsNet conversions.
- Assert bad quantity, channel, missing template or out-of-range profile fails at **compile time** via diagnostics.
- Benchmark incremental rebuild cost for a catalog of 100+ profiles; keep generation deterministic and cacheable.
- Validate instrument SCPI commands and ranges against manufacturer documentation before production use.

## Implementation status
A draft JSON profile and specification have been committed. Existing `ScpiParameter` descriptors, UnitsNet facades, async sessions and simulator tests can serve as the generator runtime. **No Roslyn generator is claimed to be working in this change.**

## Revised decision — standalone source emission, not compiler-time generator

**This supersedes the earlier Roslyn incremental-generator recommendation above.** Developers edit instrument descriptions and invoke a standalone .NET generator from the build, which writes ordinary `.g.cs` files into `src/BenchDouble/Generated`. Generated files should be committed and indexed by editors, inspected in diffs, debugged, and included in ordinary compilation. No compiler/analyzer magic is required.

Current tool: `tools/BenchDouble.DriverGen`. Run `make generate` (or `dotnet run --project tools/BenchDouble.DriverGen -- instrument-definitions src/BenchDouble/Generated`). The tool currently handles numeric frequency/potential/resistance/current properties with SCPI get/set templates, channel placeholders, basic bounds and UnitsNet conversions. It is an **early generator**, not yet a general external instrument source driver converter or full IVI mapping.

### Mock behavior as data and code
Mock outputs must support not only static responses but scripted behaviors:
- constant, sequence, state-dependent, error, timeout
- sine-wave function of elapsed time, amplitude, frequency, phase and offset
- seeded pseudo-random measurement with bounded range or configured statistical distribution
- composite signal-plus-noise and DUT transfer functions
- handwritten callback hooks only within an isolated, approved simulator process

The initial runtime `ISignalBehavior`, `SineBehavior`, `SeededRandomBehavior` and `SumBehavior` are now implemented. **Binding these behaviors from instrument JSON into generated mock classes is still pending.** Sine parameters are explicit; random samples depend on call order and seed, so tests must control both. No untrusted C# code shall be compiled/executed by instrument hosts based on a remote user-supplied description.

### Build and verification requirements
- `make generate` regenerates files deterministically; `make check-generated` detects committed output drift.
- Build-time generation is a separate program; CI must generate, compile, test and detect stale generated code.
- Validate descriptions before emitting partial results; use atomic writes and reject duplicate driver names, unsupported quantity/unit pairs, bad command templates and unsafe class names.
- Record generator version, schema revision and original source attribution.
- Code changes require parity tests versus manufacturer manuals and any source-adapted external instrument source commands.
- The current Makefile is a convenience entry point, not yet a cross-platform bootstrapper; direct `dotnet run` supports Windows without Make.
- **Current status: source committed; no successful .NET build or generator execution has been verified here.**

## Mock profile generation and time semantics

Instrument drivers and mock behavior definitions have distinct concerns: generated production drivers describe validated commands, quantities and capabilities; versioned mock descriptions define **initialization**, **per-operation measurement behavior**, clock attachment, latency, seeds and state transitions. A proposed `*.mock.json` format should compile to ordinary C# mocks and behavior setup using the same explicit standalone generator. Mock time is a bench-scoped service with real-time, accelerated, paused and manual modes. Delays and jitter have UnitsNet-compatible duration definitions; real hardware and server lease timeouts remain on wall time. See [MOCK-TIME-AND-LIFECYCLE.md](MOCK-TIME-AND-LIFECYCLE.md). The proposed schema is not yet accepted by the current generator.
