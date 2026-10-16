#!/usr/bin/env python3
"""Build a pinned, comprehensive ExternalInstrumentSource class manifest from a local checkout.

This discovery tool does not execute upstream Python. It inventories every
class declared under external_instrument_source/instruments and records unsupported files.
Output is a manifest for manual review and batch_extract.py, not approved drivers.
"""
import argparse
import ast
import csv
import json
import re
import subprocess
from pathlib import Path

COLUMNS = ("manufacturer", "model", "className", "sourcePath", "revision")


def discover(root: Path):
    """Return a complete per-class inventory and explicit parse errors."""
    root = root.resolve()
    instrument_root = root / "external_instrument_source" / "instruments"
    if not instrument_root.is_dir():
        raise ValueError("Expected external_instrument_source/instruments inside source root")
    revision = subprocess.check_output(
        ["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("Checkout revision must be a full commit SHA")
    rows, failures = [], []
    files = sorted(instrument_root.rglob("*.py"))
    for file in files:
        relative = file.relative_to(root).as_posix()
        if file.name == "__init__.py":
            continue
        try:
            tree = ast.parse(file.read_text(encoding="utf-8-sig"), filename=relative)
        except (SyntaxError, UnicodeError, OSError) as exc:
            failures.append({"path": relative, "error": str(exc)})
            continue
        manufacturer = file.parent.name
        for node in tree.body:
            if not isinstance(node, ast.ClassDef):
                continue
            bases = [ast.unparse(b) for b in node.bases]
            rows.append({
                "manufacturer": manufacturer,
                "model": node.name,
                "className": node.name,
                "sourcePath": relative,
                "revision": revision,
                "line": node.lineno,
                "bases": bases,
                "directMethods": sum(isinstance(x, (ast.FunctionDef, ast.AsyncFunctionDef)) for x in node.body),
                "directAssignments": sum(isinstance(x, (ast.Assign, ast.AnnAssign)) for x in node.body),
                "review": "unreviewed",
            })
    return {"revision": revision, "sourceFiles": len(files), "classes": rows, "parseErrors": failures}


def main():
    """Run the command-line validation and report results."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--inventory", required=True, type=Path)
    args = parser.parse_args()
    if args.manifest.exists() or args.inventory.exists():
        parser.error("Refusing to overwrite existing outputs")
    result = discover(args.source_root)
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.inventory.parent.mkdir(parents=True, exist_ok=True)
    with args.manifest.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=COLUMNS)
        writer.writeheader()
        for item in result["classes"]:
            writer.writerow({name: item[name] for name in COLUMNS})
    args.inventory.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Inventory: {len(result['classes'])} classes in {result['sourceFiles']} files; "
          f"{len(result['parseErrors'])} parse failures")
    return 1 if result["parseErrors"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
