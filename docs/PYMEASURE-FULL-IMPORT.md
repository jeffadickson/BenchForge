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
