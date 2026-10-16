#!/usr/bin/env python3
"""Audit tracked BenchForge files for external-source branding.

The designated repository-root legal review file is the only allowed location
for that name. Use --strict in CI to reject any other occurrence, including
filenames, generated code, tests and documentation. Does not rewrite files.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LEGAL_REGISTER = "THIRD_PARTY_REVIEW.md"
# Construct the search term to keep the audit implementation source-neutral.
PROHIBITED = ("Py" + "Measure").casefold()

def tracked_files() -> list[str]:
    """Return Git-tracked paths, including generated source and historical docs."""
    result = subprocess.run(
        ["git", "ls-files", "-z"], cwd=ROOT, check=True, capture_output=True)
    return [name.decode("utf-8", "surrogateescape")
            for name in result.stdout.split(b"\0") if name]


def audit() -> list[dict[str, object]]:
    """Find source-name references outside the root legal-review register."""
    findings: list[dict[str, object]] = []
    for name in tracked_files():
        if name == LEGAL_REGISTER:
            continue
        if PROHIBITED in name.casefold():
            findings.append({"path": name, "line": 0, "kind": "filename"})
        path = ROOT / name
        if not path.is_file():
            continue
        try:
            raw = path.read_bytes()
            if b"\\0" in raw:
                continue
            lines = raw.decode("utf-8").splitlines()
        except (UnicodeError, OSError):
            continue
        for number, line in enumerate(lines, 1):
            if PROHIBITED in line.casefold():
                findings.append({"path": name, "line": number, "kind": "content"})
    return findings


def main() -> int:
    """Emit machine-readable audit evidence and optionally fail on violations."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--strict", action="store_true")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    findings = audit()
    report = {"allowedRegister": LEGAL_REGISTER, "violations": len(findings),
              "findings": findings}
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Source-neutrality audit: {len(findings)} violation(s)")
    for item in findings[:40]:
        print(f"  {item['path']}:{item['line']} ({item['kind']})")
    return 1 if args.strict and findings else 0


if __name__ == "__main__":
    sys.exit(main())
