# Instrument Definer

This is a **source-extraction and validation** tool, not a Python-to-C# translator. It never imports or executes PyMeasure drivers. It creates **schema-v2 review drafts**, which must be checked against instrument manuals before code generation.

## Intern handoff

Prefer one JSON file per instrument in `instrument-definitions/drafts/`. A two-tab spreadsheet can be exported as UTF-8 `properties.csv` and `methods.csv` using the [templates](templates/). Include source file and upstream commit, model, manufacturer, interface, quantity and unit, read/set command, mode, range, default mock, source line, and unresolved questions. **Never guess units or command strings**: leave them null/blank and mark for review.

## Commands

```bash
# No third-party Python dependencies. Never executes the upstream file.
python3 tools/InstrumentDefiner/definer.py extract-pymeasure \
  pymeasure/instruments/keithley/keithley2400.py \
  --manufacturer Keithley --model 2400 --revision PINNED_COMMIT_SHA \
  --output instrument-definitions/drafts/keithley2400.extracted.json

# Validate a reviewed or extracted v2 definition.
python3 tools/InstrumentDefiner/definer.py validate \
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

## Review workflow

**Extracted → Source reviewed → Manual reviewed → Hardware validated.** Reviewer, source revision, manual reference and unresolved questions remain part of the definition. Hardware validation requires actual hardware evidence and a named reviewer. The draft format is [schema v2](../../instrument-definitions/schema/instrument-definition-v2.schema.json). The existing `BenchDouble.DriverGen` consumes the older, limited **schema v1** `*.instrument.json` files only. It must not silently convert v2 extraction drafts into working drivers until command mappings, units, modes and safety are reviewed.

## Generator roadmap

The next increment will use approved v2 definitions to generate typed async interfaces, SCPI/legacy-bus adapters, mode/state transitions, default mock initialization, measurement behaviors, test fixtures, documentation and unit-specific examples. The mode-aware mock engine must refuse incompatible requests, and unsafe output-enabling commands must require explicit authorization. No automatic physical access from extracted source.
