#!/usr/bin/env python3
"""Reject new API comment gaps; retain a reviewable inventory of legacy gaps."""
import ast
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / "docs/api-documentation-baseline.json"


def collect(root=ROOT):
    """Combine Roslyn's declaration audit with an AST-only Python docstring audit."""
    rows = json.loads((root / "build/api-audit.json").read_text())
    for area in ("src", "tools", "scripts", "examples", "tests"):
        for path in sorted((root / area).rglob("*.py")):
            if any(x in path.parts for x in ("bin", "obj", "__pycache__")):
                continue
            tree = ast.parse(path.read_text(encoding="utf-8"))
            relative = path.relative_to(root).as_posix()
            rows.append(dict(path=relative, symbol="<module>", line=1,
                             issues=[] if ast.get_docstring(tree) else ["docstring"]))
            def visit(node, prefix=""):
                """Visit definitions without importing or executing their modules."""
                for child in ast.iter_child_nodes(node):
                    if isinstance(child, (ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)):
                        name = prefix + child.name
                        rows.append(dict(path=relative, symbol=name, line=child.lineno,
                                         issues=[] if ast.get_docstring(child) else ["docstring"]))
                        visit(child, name + ".")
                    else:
                        visit(child, prefix)
            visit(tree)
    return sorted(rows, key=lambda r: (r["path"], r["symbol"]))


def gaps(rows):
    """Return stable gap keys independent of line numbers and source ordering."""
    return sorted({r["path"] + "::" + r["symbol"] + "::" + issue
                   for r in rows for issue in r["issues"]})


def main():
    """Write the complete audit and fail on new or undocumented generated APIs."""
    rows = collect()
    current = set(gaps(rows))
    baseline = set(json.loads(BASELINE.read_text()))
    generated = {key for key in current if "/Generated/" in key or ".g.cs::" in key}
    added = current - baseline
    removed = baseline - current
    (ROOT / "build/documentation-audit.json").write_text(json.dumps(rows, indent=2) + "\n")
    print(f"Documentation: {len(rows)} declarations/modules, {len(current)} gaps, "
          f"{len(added)} new, {len(removed)} resolved, {len(generated)} generated gaps")
    if added or generated:
        print("\n".join(sorted(added | generated)))
        return 1
    if removed:
        print("Remove resolved entries from docs/api-documentation-baseline.json after review.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
