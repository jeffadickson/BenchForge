#!/usr/bin/env python3
"""Run a local XDoc/docstring preflight without .NET or third-party packages.

This lexical C# scanner is advisory: Roslyn remains authoritative in CI.
"""
from __future__ import annotations

import argparse
import ast
import json
import re
from pathlib import Path

AREAS = ("src", "tools", "scripts", "examples", "tests", "generated")
SKIP = {"bin", "obj", "__pycache__", ".git", ".venv"}
DECL = re.compile(
    r"\b(public|protected)\s+(?:(?:static|sealed|abstract|partial|async|override|virtual|new|readonly|required|unsafe|ref|extern)\s+)*"
    r"(?:(class|struct|interface|enum|record|delegate)\s+(\w+)|"
    r"(?:[\w<>,?.\[\] ]+?)\s+(\w+)\s*\(([^;{}()]*)\)\s*(?:=>|\{|;)|"
    r"[\w<>,?.\[\] ]+?\s+(\w+)\s*(?:\{|=>))",
    re.S,
)
LITERALS = re.compile(
    r"//[^\n]*|/\*[\s\S]*?\*/|@\"(?:[^\"]|\"\")*\"|\"(?:\\.|[^\"\\])*\"|'(?:\\.|[^'\\])*'",
    re.S,
)
PARAM = re.compile(r'<param\s+name=["\']([^"\']+)["\']\s*>\s*(.*?)\s*</param>', re.S)


def mask_csharp(source: str) -> str:
    """Mask strings and comments while retaining character offsets and newlines."""
    return LITERALS.sub(lambda m: "".join("\n" if c == "\n" else " " for c in m.group()), source)


def xml_before(lines: list[str], line: int) -> str:
    """Collect contiguous triple-slash documentation before C# attributes."""
    i = line - 2
    while i >= 0 and (not lines[i].strip() or lines[i].lstrip().startswith("[")):
        i -= 1
    docs = []
    while i >= 0 and lines[i].lstrip().startswith("///"):
        docs.append(lines[i].split("///", 1)[1])
        i -= 1
    return "\n".join(reversed(docs))


def parameter_names(signature: str) -> list[str]:
    """Extract parameter identifiers without splitting nested generic type arguments."""
    parts, chunk, depth = [], "", 0
    for char in signature + ",":
        if char in "<[(":
            depth += 1
        elif char in ">])":
            depth = max(0, depth - 1)
        if char == "," and depth == 0:
            parts.append(chunk)
            chunk = ""
        else:
            chunk += char
    names = []
    for part in parts:
        words = re.findall(r"\b\w+\b", part.split("=")[0])
        if words:
            names.append(words[-1])
    return names



# Supplemental declaration patterns for constructs missed by the fast matcher.
RECORD_PRIMARY = re.compile(
    r"(?m)^[ \t]*(?:public|protected)\s+(?:(?:sealed|abstract|partial|readonly)\s+)*"
    r"record\s+(?:(?:class|struct)\s+)?(?P<name>\w+)\s*\((?P<params>[^{};()]*)\)"
)
CONSTRUCTOR = re.compile(
    r"(?m)^[ \t]*(?:public|protected)\s+(?P<name>[A-Za-z_]\w*)\s*"
    r"\((?P<params>[^{};()]*)\)\s*(?::[^{};]*?)?(?:\{|=>|;)"
)


def supplemental_csharp(masked: str, lines: list[str], path: Path, root: Path) -> list[dict]:
    """Detect record primary parameters and explicit constructors missed by DECL."""
    rows = []
    for pattern, is_record in ((RECORD_PRIMARY, True), (CONSTRUCTOR, False)):
        for match in pattern.finditer(masked):
            line = masked.count("\n", 0, match.start()) + 1
            docs = xml_before(lines, line)
            if re.search(r"<inheritdoc\s*/?>", docs):
                continue
            documented = {name for name, _ in PARAM.findall(docs)}
            missing = ["param:" + name for name in parameter_names(match.group("params"))
                       if name not in documented]
            if not is_record and not re.search(r"<summary(?:\s+[^>]*)?>\s*[^<\s]", docs, re.S):
                missing.insert(0, "summary")
            if missing:
                rows.append({"path": path.relative_to(root).as_posix(),
                             "symbol": match.group("name"), "line": line, "issues": missing})
    return rows

def scan_csharp(path: Path, root: Path) -> list[dict]:
    """Report missing XML elements in lexically recognized public C# declarations."""
    source = path.read_text(encoding="utf-8-sig")
    masked = mask_csharp(source)
    lines = source.splitlines()
    findings = []
    for match in DECL.finditer(masked):
        line = masked.count("\n", 0, match.start()) + 1
        docs = xml_before(lines, line)
        issues = []
        if not re.search(r"<inheritdoc\s*/?>", docs):
            if not re.search(r"<summary(?:\s+[^>]*)?>\s*[^<\s]", docs, re.S):
                issues.append("summary")
            if match.group(5) is not None:
                documented = {name for name, _ in PARAM.findall(docs)}
                issues.extend("param:" + name for name in parameter_names(match.group(5))
                              if name not in documented)
                before = masked[match.start():match.start(4)]
                if not re.search(r"\bvoid\s*$", before) and "<returns>" not in docs:
                    issues.append("returns")
        if issues:
            findings.append({
                "path": path.relative_to(root).as_posix(),
                "symbol": match.group(3) or match.group(4) or match.group(6),
                "line": line,
                "issues": issues,
            })
    # Merge supplemental findings into existing declarations at the same source line.
    for extra in supplemental_csharp(masked, lines, path, root):
        current = next((item for item in findings
                        if item["line"] == extra["line"] and item["symbol"] == extra["symbol"]), None)
        if current is None:
            findings.append(extra)
        else:
            current["issues"] = list(dict.fromkeys(current["issues"] + extra["issues"]))
    return findings


def scan_python(path: Path, root: Path) -> list[dict]:
    """Use Python AST to find undocumented modules, functions and classes."""
    tree = ast.parse(path.read_text(encoding="utf-8-sig"), filename=str(path))
    findings = []
    def visit(node, prefix=""):
        if node is tree or isinstance(node, (ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)):
            name = "<module>" if node is tree else prefix + node.name
            if not ast.get_docstring(node):
                findings.append({"path": path.relative_to(root).as_posix(),
                                 "symbol": name, "line": getattr(node, "lineno", 1),
                                 "issues": ["docstring"]})
            prefix = "" if node is tree else name + "."
        for child in ast.iter_child_nodes(node):
            if isinstance(child, (ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)):
                visit(child, prefix)
    visit(tree)
    return findings


def audit(root: Path) -> dict:
    """Scan the checkout and return deterministic, machine-readable findings."""
    findings, counts = [], {"cs": 0, "py": 0}
    for area in AREAS:
        folder = root / area
        if not folder.is_dir():
            continue
        for path in sorted(folder.rglob("*")):
            if not path.is_file() or any(part in SKIP for part in path.relative_to(root).parts):
                continue
            if path.suffix == ".cs":
                findings.extend(scan_csharp(path, root))
                counts["cs"] += 1
            elif path.suffix == ".py":
                findings.extend(scan_python(path, root))
                counts["py"] += 1
    return {"checker": "portable-lexical-preflight", "authoritative": False,
            "files": counts, "gapCount": sum(len(row["issues"]) for row in findings),
            "findings": findings}


def main() -> int:
    """Print local findings and optionally fail when documentation is incomplete."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", default=".")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--strict", action="store_true")
    args = parser.parse_args()
    result = audit(Path(args.root).resolve())
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Portable documentation preflight: {result['files']}, "
          f"{result['gapCount']} gaps; Roslyn CI remains authoritative")
    for item in result["findings"][:50]:
        print(f"{item['path']}:{item['line']}: {', '.join(item['issues'])}")
    return int(args.strict and result["gapCount"] > 0)


if __name__ == "__main__":
    raise SystemExit(main())
