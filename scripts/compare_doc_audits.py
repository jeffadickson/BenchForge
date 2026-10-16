#!/usr/bin/env python3
"""Compare SDK-free C# documentation preflight with authoritative Roslyn findings.

This is a diagnostic comparison, not a substitute for Roslyn or a release gate.
Match declaration locations within a small tolerance and compare issue categories.
"""
from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path


def issue_rows(rows: list[dict], suffix: str = ".cs") -> list[tuple[str, int, str]]:
    """Expand one finding per documentation issue for C# source files."""
    return [(r["path"], int(r["line"]), issue)
            for r in rows if r["path"].endswith(suffix)
            for issue in r.get("issues", [])]


def compare(roslyn: list[dict], portable: list[dict], tolerance: int = 3) -> dict:
    """Compare findings by path, issue type and nearby declaration line."""
    expected = issue_rows(roslyn)
    observed = issue_rows(portable)
    by_key: dict[tuple[str, str], list[tuple[int, int]]] = defaultdict(list)
    for index, (path, line, issue) in enumerate(observed):
        by_key[(path, issue)].append((index, line))
    used: set[int] = set()
    matched, missed = [], []
    for path, line, issue in expected:
        candidates = [(abs(line - candidate_line), index)
                      for index, candidate_line in by_key.get((path, issue), [])
                      if index not in used and abs(line - candidate_line) <= tolerance]
        if candidates:
            _, index = min(candidates)
            used.add(index)
            matched.append({"path": path, "line": line, "issue": issue})
        else:
            missed.append({"path": path, "line": line, "issue": issue})
    extra = [{"path": path, "line": line, "issue": issue}
             for i, (path, line, issue) in enumerate(observed) if i not in used]
    by_path = defaultdict(lambda: {"roslyn": 0, "matched": 0, "missed": 0, "portableOnly": 0})
    for row, kind in [(x, "roslyn") for x in matched + missed] + [
        (x, "matched") for x in matched] + [(x, "missed") for x in missed] + [
        (x, "portableOnly") for x in extra]:
        by_path[row["path"]][kind] += 1
    return {
        "method": "C# issue-level path/line/category comparison, tolerance +/- %d lines" % tolerance,
        "limitations": ["Portable scanner is lexical and may miss C# syntax constructs.",
                        "Line-tolerant matching is diagnostic and not proof of parser equivalence.",
                        "Python docstrings are checked independently by Python AST."],
        "roslynIssueCount": len(expected),
        "portableIssueCount": len(observed),
        "matched": len(matched),
        "recall": len(matched) / len(expected) if expected else 1.0,
        "precision": len(matched) / len(observed) if observed else (1.0 if not expected else 0.0),
        "missed": missed, "portableOnly": extra,
        "byFile": dict(sorted(by_path.items())),
        "roslynIssuesByKind": dict(Counter(issue for _, _, issue in expected)),
    }


def main() -> int:
    """Read audit reports and write a reproducible, non-gating parity report."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--roslyn", type=Path, required=True)
    parser.add_argument("--portable", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    roslyn = json.loads(args.roslyn.read_text(encoding="utf-8"))
    portable = json.loads(args.portable.read_text(encoding="utf-8"))
    report = compare(roslyn, portable["findings"])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("Portable/Roslyn parity: recall={:.1%} precision={:.1%}, "
          "{} missed, {} portable-only".format(
              report["recall"], report["precision"],
              len(report["missed"]), len(report["portableOnly"])))
    for row in report["missed"][:20]:
        print("  missed: {path}:{line} {issue}".format(**row))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
