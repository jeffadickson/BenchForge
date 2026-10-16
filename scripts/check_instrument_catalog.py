#!/usr/bin/env python3
"""Validate the porting catalog without treating inventory entries as drivers."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "instrument-definitions/instrument-catalog.json"
ALLOWED_EVIDENCE = {"sourceChecked", "catalogListed", "manualReferenced"}
ALLOWED_DEFINITION = {"catalogOnly", "manualDriverPartial", "definitionDraft", "definitionReviewed"}
ALLOWED_IMPLEMENTATION = {"notPorted", "partialDcSourceMeasure", "generatedPrototype", "hardwareValidated"}


def main() -> int:
    """Validate catalog structure, statuses and referenced definitions; raise ValueError on invalid inventory."""
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    if catalog.get("schemaVersion") != 1:
        raise ValueError("Unsupported instrument catalog schema")
    instruments = catalog.get("instruments")
    if not isinstance(instruments, list) or len(instruments) < 26:
        raise ValueError("Catalog must contain at least 25 catalog entries and Keithley 236")
    ids, models = set(), set()
    for item in instruments:
        identity = (item["manufacturer"], item["model"])
        if item["id"] in ids or identity in models:
            raise ValueError(f"Duplicate instrument: {item['id']}")
        ids.add(item["id"])
        models.add(identity)
        if item["sourceEvidence"] not in ALLOWED_EVIDENCE:
            raise ValueError(f"Unknown evidence state: {item['id']}")
        if item["definitionStatus"] not in ALLOWED_DEFINITION:
            raise ValueError(f"Unknown definition state: {item['id']}")
        if item["implementationStatus"] not in ALLOWED_IMPLEMENTATION:
            raise ValueError(f"Unknown implementation state: {item['id']}")
        if item.get("hardwareValidated", False) and item["implementationStatus"] != "hardwareValidated":
            raise ValueError(f"Hardware validation claim not supported: {item['id']}")
        if item["definitionStatus"] in {"definitionDraft", "definitionReviewed"}:
            definition_path = item.get("definitionPath")
            if not definition_path or not (ROOT / definition_path).is_file():
                raise ValueError(f"Missing definition draft for {item['id']}")
            definition = json.loads((ROOT / definition_path).read_text(encoding="utf-8"))
            if definition.get("schemaVersion") not in (1, 2):
                raise ValueError(f"Unsupported definition schema for {item['id']}")
        if item["implementationStatus"] != "notPorted":
            for field in ("driverPath", "mockPath"):
                path = item.get(field)
                if path and not (ROOT / path).is_file():
                    raise ValueError(f"Missing {field} for {item['id']}: {path}")
    catalog_candidates = [x for x in instruments if x["id"].startswith("CAT-")]
    if len(catalog_candidates) != 25 or "LEGACY-KEITHLEY-236" not in ids:
        raise ValueError("Expected 25 catalog entries plus Keithley 236")
    print(f"Catalog OK: {len(instruments)} entries, {len(catalog_candidates)} catalog candidates, "
          f"{sum(x['implementationStatus'] != 'notPorted' for x in instruments)} partial/implemented")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

