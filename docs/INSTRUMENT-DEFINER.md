# Instrument Definer — Intern Extraction Specification

**Version:** 0.1 / schema v2. **Status:** extraction, validation and mock-preview prototype; no physical hardware or automatic production driver generation.

## Objective

Capture instrument semantics once, then generate strongly typed asynchronous C# interfaces, driver adapters, mode-aware UnitsNet mocks, documentation and tests. Engineers should never have to manually duplicate every SCPI command or implement mock equivalents for every property. The definition is the source of truth; the generator owns threading, cancellation, serialization, units and mock registration.

## What the intern should deliver

**Preferred:** one UTF-8 JSON definition per model, named `manufacturer/model.definition.json`, conforming to [schema v2](../instrument-definitions/schema/instrument-definition-v2.schema.json). A spreadsheet is acceptable as an intermediate source: export a properties CSV and methods CSV using the [templates](../tools/InstrumentDefiner/templates/). Each instrument should have a pinned external instrument source commit SHA and exact source path, upstream class and parent classes, manufacturer manual reference, and notes about uncertain mappings.

For **every property**, capture name, type (control, measurement, setting, status), access, getter and setter command templates, C# UnitsNet quantity/unit (or null if unknown), valid values, minimum/maximum, source line, operating modes, and any side effects (such as auto-range disabled by setting range). For **every method**, capture name, argument names/types/defaults, return quantity and units, instrument commands, preconditions, postconditions, exceptions, timeouts, triggers, and source line. Methods that implement complex algorithms (sweeps, buffers, ramps) should be marked manual-review-required rather than reduced to a single SCPI command.

For **each operating mode**, document legal transitions, mode-selection commands, default/reset state, source versus measurement channels, legal measurement types, output/compliance prerequisites, and what changes are invalidated by a mode switch. Capture hardware-specific status/error handling, data framing and query/response parsing. **Do not guess missing values.**

For **mocks**, specify initialization behavior, per-command state transitions, measurement behavior, optional noise/IV curves, shared simulated signals, virtual-time response and realistic delays. Unknown measurements must fail rather than return invented zeroes.

## Example: extracted external instrument source Keithley 2400

[Representative draft](../instrument-definitions/drafts/keithley2400.definition.json) includes source-mode selection, source delay, source enable and public methods from external instrument source. This is a **subset for illustrating the format**, not a complete extraction. Source commands are visible in [the centralized review register](../THIRD_PARTY_REVIEW.md). Pin the actual upstream revision before review; source-line numbers in the illustrative draft must be checked against that revision.

Imported source files include license notices. The intern should record the source revision and preserve required attribution. We should primarily extract facts about instrument behavior and independently generate code; do not blindly copy third-party Python implementations into generated C#.

## Automated extraction and review

```bash
python3 tools/InstrumentDefiner/definer.py import-csv \
  path/to/external instrument source/instruments/keithley/keithley2400.py \
  --manufacturer Keithley --model 2400 \
  --revision YOUR_PINNED_external instrument source_COMMIT \
  --output instrument-definitions/drafts/keithley2400.extracted.json

python3 tools/InstrumentDefiner/definer.py validate \
  instrument-definitions/drafts/keithley2400.extracted.json

make definer-check
```

The extraction tool uses Python's **AST** to inspect descriptors and public methods without executing source. It deliberately leaves quantity and unit null when they cannot be established. Its validation checks unique names, mode references, recognized UnitsNet quantity/unit pairs, numeric ranges, and review state. The JSON Schema defines the full intended shape; the current stdlib CLI checks the most critical semantic subset. Full schema validation and inheritance analysis remain tracked in issue #52.

## Mode-aware mock preview

[Illustrative two-mode DMM](../instrument-definitions/drafts/example-two-mode-dmm.definition.json) demonstrates the schema. `InstrumentDefinitionMockLoader.CreatePreview(json, clock)` consumes its explicit modes and `mockDefault` values to build a `ModeAwareMockInstrument`. `ConfigureModeAsync("resistance")` permits `MeasureAsync<ElectricResistance>()`; requesting voltage in that mode fails. Switching to voltage mode permits `ElectricPotential`. Missing defaults do not produce arbitrary measurements. This preview performs **no SCPI I/O or physical access**.

## Separation of maturity levels

1. **Extracted:** AST/CSV facts with unresolved mappings; cannot generate production drivers.
2. **Source reviewed:** descriptors and methods checked against pinned external instrument source revision; provenance complete.
3. **Manual reviewed:** commands, ranges, modes, safety, timeouts and response formats checked against vendor documentation; eligible for future v2 generator.
4. **Hardware validated:** tested on the actual instrument with signed-off results and safety controls.

A successful mock demonstration is **not** hardware validation. A manual-reviewed definition may still require dedicated hardware tests.

## Generation roadmap

The existing `BenchForge.DriverGen` continues to support schema-v1 scalar SCPI definitions, including the illustrative Keysight 33522B driver/mock pair. The new schema-v2 Definer is a **separate intake and review pipeline**. The next integration step is to generate typed async drivers and mode-aware mocks from *reviewed* v2 definitions, preserving source compatibility and ensuring no unreviewed command can enable a real output.

**Tracking:** [#52 Instrument Definer](https://github.com/jeffadickson/BenchForge/issues/52) and [#53 Mode-aware generation](https://github.com/jeffadickson/BenchForge/issues/53). Requirement IDs BD-DEF-001..008.

## 2026-10-15: batch intake, coverage evidence and review gating

The next increment adds [batch_extract.py](../tools/InstrumentDefiner/batch_extract.py)
for a manifest of pinned external instrument source files and
[review_gate.py](../tools/InstrumentDefiner/review_gate.py) for a stricter
metadata-readiness check. The batch tool produces separate draft definitions,
never overwrites previous work, and emits a JSON report for every requested
model. It rejects source paths outside the selected checkout and requires
40-character upstream Git revisions.

The AST extractor now records unresolved inherited and dynamic member
candidates, including inherited members from classes in the same file,
external mixins, dynamic `setattr` calls and Python properties. This is a
**conservative completeness report**, not a claim that all inherited methods
were extracted. Reviewers must reconcile those members and attach evidence
before marking a definition manual-reviewed.

The gate rejects unknown physical units for measurement capabilities,
unreviewed commands, missing mode-selection commands, unresolved method
semantics, placeholder upstream provenance, missing manufacturer manual
evidence, and unreviewed inheritance/dynamic behavior. The gate can also
check hardware-evidence metadata, but it does not itself test hardware.
A successful metadata gate does not yet authorize code generation; the
schema-v2 driver generator and host safety controls remain separate.

For the intern's complete commands and CSV templates, see
[Instrument Definer README](../tools/InstrumentDefiner/README.md).
Requirements BD-DEF-009..011 and [issue #52](https://github.com/jeffadickson/BenchForge/issues/52)
track these additions. CI execution and hardware qualification are not
claimed by this increment.


## End-to-end walkthrough

See [Examples](EXAMPLES.md) for a generator/checked-in mock workflow. Schema-v2 review drafts must not be treated as production driver inputs until manual review and generator support are complete.
