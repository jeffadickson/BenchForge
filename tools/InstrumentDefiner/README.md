# Instrument Definer

This is a **source-extraction and validation** tool, not a Python-to-C# translator. It never imports or executes external instrument source drivers. It creates **schema-v2 review drafts**, which must be checked against instrument manuals before code generation.

## Intern handoff

Prefer one JSON file per instrument in `instrument-definitions/drafts/`. A two-tab spreadsheet can be exported as UTF-8 `properties.csv` and `methods.csv` using the [templates](templates/). Include source file and upstream commit, model, manufacturer, interface, quantity and unit, read/set command, mode, range, default mock, source line, and unresolved questions. **Never guess units or command strings**: leave them null/blank and mark for review.

## Commands

```bash
# No third-party Python dependencies. Never executes the upstream file.
python3 tools/InstrumentDefiner/definer.py extract-external instrument source \
  external instrument source/instruments/keithley/keithley2400.py \
  --manufacturer Keithley --model 2400 --revision PINNED_COMMIT_SHA \
  --output instrument-definitions/drafts/keithley2400.extracted.json

# Validate a reviewed or extracted v2 definition.
python3 tools/InstrumentDefiner/definer.py validate \
  instrument-definitions/drafts/keithley2400.definition.json
python3 tools/InstrumentDefiner/definer.py report \
  instrument-definitions/drafts/keithley2400.definition.json

# Import hand-entered CSV exported from spreadsheet.
python3 tools/InstrumentDefiner/definer.py import-csv \
  tools/InstrumentDefiner/templates/properties.csv \
  --methods tools/InstrumentDefiner/templates/methods.csv \
  --manufacturer Keithley --model 2400 --driver-name Keithley2400 \
  --manual "Keithley 2400 Series SourceMeter User's Manual" \
  --output instrument-definitions/drafts/keithley2400.csv-import.json
```

The tool refuses to overwrite existing files. The extracted draft contains all statically recognizable `Instrument.control`, `Instrument.measurement`, and `Instrument.setting` descriptors plus public class methods. It **cannot infer all semantics**, especially properties defined dynamically, inherited capabilities, mappings, unit dimensions, safety interlocks, decorators, complex methods, or device-specific response parsing. The intern must resolve these manually.

When multiple classes tie for the most recognized descriptors, extraction stops:
select the intended class with `--class-name Keithley2400`. An explicit selection
also overrides automatic selection for files containing helper/channel classes.
The selected class is recorded in `provenance.upstreamClass`; inheritance is
flagged as unresolved rather than presented as complete coverage.

Method extraction preserves positional-only and keyword-only parameter names,
`*args`/`**kwargs`, and the async marker. The complete Python argument signature,
including annotations and defaults, is retained as text in `notes`. Default
expressions are never evaluated. `arguments` remains a list of parameter names
(variadics include their stars); these are review metadata, not executable C#.

Local verification: `make definer-check` passed 12 Python tests and both checked-in
draft validations on 2026-10-15. This verifies extraction behavior, not full JSON
Schema conformance, generated C# compilation, or hardware correctness.

## Review workflow

**Extracted → Source reviewed → Manual reviewed → Hardware validated.** Reviewer, source revision, manual reference and unresolved questions remain part of the definition. Hardware validation requires actual hardware evidence and a named reviewer. The draft format is [schema v2](../../instrument-definitions/schema/instrument-definition-v2.schema.json). The existing `BenchDouble.DriverGen` consumes the older, limited **schema v1** `*.instrument.json` files only. It must not silently convert v2 extraction drafts into working drivers until command mappings, units, modes and safety are reviewed.

## Generator roadmap

The next increment will use approved v2 definitions to generate typed async interfaces, SCPI/legacy-bus adapters, mode/state transitions, default mock initialization, measurement behaviors, test fixtures, documentation and unit-specific examples. The mode-aware mock engine must refuse incompatible requests, and unsafe output-enabling commands must require explicit authorization. No automatic physical access from extracted source.


## Batch extraction for the intern

Use [batch-manifest.csv](templates/batch-manifest.csv) as the handoff sheet.
Every row needs manufacturer, model, upstream class name, source path relative
to a checked-out external instrument source tree, and the **40-character pinned Git commit SHA**.
Replace the placeholders before running:

```bash
python3 tools/InstrumentDefiner/batch_extract.py \
  tools/InstrumentDefiner/templates/batch-manifest.csv \
  --source-root path/to/external instrument source-checkout \
  --output-dir instrument-definitions/drafts/batch-001 \
  --report build/instrument-extraction-batch-001.json
```

Each source file is parsed statically, not imported. The batch report records
extracted/failed models, direct property/method counts, and unresolved inherited
or dynamic members. The tool rejects path traversal, ambiguous classes,
un-pinned revisions and overwriting existing drafts or reports. A partially
successful batch exits nonzero; fix failed rows rather than hiding them.

## Review gate: do not generate physical drivers from raw extraction

```bash
python3 tools/InstrumentDefiner/review_gate.py \
  instrument-definitions/drafts/keithley2400.definition.json \
  --target manualReviewed --json
```

The current illustrative Keithley draft **must fail** this gate. A definition
becomes metadata-ready only when reviewer status, source provenance, license,
manual evidence, all command/mode mappings and unresolved issues are complete.
For extracted Python sources, the upstream commit must be a full 40-character
SHA. Manual-only definitions can cite the manufacturer manual instead.

The extractor now reports `extraction.inheritedCandidates` and
`extraction.dynamicCandidates`, with unresolved counts. The review gate
requires explicit reconciliation evidence; simply changing the counts to zero
does not establish completeness. Hardware-validated status additionally
requires a fixture ID and test-evidence reference. These checks validate
**metadata**, not actual instrument behavior or physical safety.

Run `make definer-check` for the source-extraction, batch, review-gate and
schema-draft unit tests. The reviewed schema-v2-to-C# driver generator is
still outstanding in [issue #53](https://github.com/jeffadickson/BenchDouble/issues/53).
