#!/usr/bin/env python3
"""Batch-import a reviewed list of ExternalInstrumentSource source files without executing them.

Manifest CSV columns: manufacturer,model,className,sourcePath,revision.
Writes one draft per entry and a deterministic JSON report. Existing drafts
are never overwritten. A failed entry is recorded and makes exit code nonzero.
"""
import argparse
import csv
import json
import re
import sys
from pathlib import Path

from definer import extract, save, validate

MAX_BATCH = 500
SAFE_NAME = re.compile(r"[^A-Za-z0-9_-]+")


def process(manifest, source_root, destination):
    """Return an ordered report; all input source paths must stay in source_root."""
    source_root = source_root.resolve()
    if not source_root.is_dir():
        raise ValueError("Source root does not exist")
    with manifest.open(newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        required = {"manufacturer", "model", "className", "sourcePath", "revision"}
        if not required.issubset(reader.fieldnames or []):
            raise ValueError("Manifest missing columns: " +
                             ", ".join(sorted(required - set(reader.fieldnames or []))))
        rows = list(reader)
    if not rows or len(rows) > MAX_BATCH:
        raise ValueError("Manifest requires 1 to 500 instrument rows")
    report = []
    output_names = set()
    for index, row in enumerate(rows, start=2):
        label = f"{(row.get('manufacturer') or '').strip()} {(row.get('model') or '').strip()}"
        result = {"row": index, "instrument": label, "status": "error"}
        try:
            manufacturer = (row.get("manufacturer") or "").strip()
            model = (row.get("model") or "").strip()
            class_name = (row.get("className") or "").strip()
            revision = (row.get("revision") or "").strip()
            source_path = (row.get("sourcePath") or "").strip()
            if not source_path:
                raise ValueError("Source path is required")
            relative = Path(source_path)
            if not all((manufacturer, model, class_name, revision)):
                raise ValueError("Manufacturer, model, className and revision are required")
            if relative.is_absolute():
                raise ValueError("Source path must be relative to source root")
            source = (source_root / relative).resolve()
            if not source.is_relative_to(source_root) or not source.is_file():
                raise ValueError("Source file missing or outside source root")
            if not re.fullmatch(r"[0-9a-fA-F]{40}", revision):
                raise ValueError("Revision must be a pinned 40-character commit SHA")
            safe_manufacturer = SAFE_NAME.sub("-", manufacturer).strip("-")
            safe_model = SAFE_NAME.sub("-", model).strip("-")
            if not safe_manufacturer or not safe_model:
                raise ValueError("Instrument identity cannot form a safe filename")
            filename = (safe_manufacturer + "-" + safe_model).lower() + ".definition.json"
            if filename in output_names:
                raise ValueError("Duplicate or invalid output instrument identity")
            output_names.add(filename)
            output = destination / filename
            doc = extract(source, manufacturer, model, revision, class_name)
            # Preserve a stable path relative to the checked-out source tree.
            doc["provenance"]["sourcePath"] = relative.as_posix()
            errors = validate(doc)
            if errors:
                raise ValueError("; ".join(errors))
            save(output, doc)
            result.update({"status": "extracted", "definition": str(output),
                           "capabilities": len(doc["capabilities"]),
                           "methods": len(doc["operations"]),
                           "unresolvedInherited": doc["extraction"]["unresolvedInheritedMembers"],
                           "unresolvedDynamic": doc["extraction"]["unresolvedDynamicMembers"]})
        except (OSError, ValueError, SyntaxError, TypeError, KeyError) as exc:
            result["error"] = str(exc)
        report.append(result)
    return {"manifest": str(manifest), "sourceRoot": str(source_root),
            "total": len(report),
            "extracted": sum(r["status"] == "extracted" for r in report),
            "failed": sum(r["status"] != "extracted" for r in report),
            "entries": report,
            "disclaimer": "Extracted drafts require source/manual review before driver generation."}


def main():
    """Run the command-line validation and report results."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.report.exists():
            raise FileExistsError("Refusing to overwrite report " + str(args.report))
        report = process(args.manifest, args.source_root, args.output_dir)
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n",
                               encoding="utf-8")
        print(f"Batch extraction: {report['extracted']}/{report['total']} extracted; "
              f"{report['failed']} failed. Report: {args.report}")
        return 0 if report["failed"] == 0 else 1
    except (OSError, ValueError, TypeError, KeyError) as exc:
        print("ERROR: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
