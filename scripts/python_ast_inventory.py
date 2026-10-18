"""Expose parser metadata only; the C# host performs checks and report generation.

Use CPython's grammar rather than approximating Python syntax in another language.
Never import or execute any inspected file. One process parses the requested batch.
"""
import ast
import json
import sys
from pathlib import Path


def inventory(request):
    """Return declaration locations and docstring presence in stable AST order."""
    results = []
    for item in request:
        tree = ast.parse(Path(item["file"]).read_text(encoding=item["encoding"]))
        rows = []

        def visit(node, prefix="", portable=True):
            """Walk definitions, retaining the reference preflight's traversal subset."""
            if node is tree or isinstance(node, (ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)):
                name = "<module>" if node is tree else prefix + node.name
                rows.append({"symbol": name, "line": getattr(node, "lineno", 1),
                             "documented": bool(ast.get_docstring(node)), "portable": portable})
                prefix = "" if node is tree else name + "."
            for child in ast.iter_child_nodes(node):
                visit(child, prefix, portable and isinstance(child, (ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)))

        visit(tree)
        results.append(rows)
    return results


if __name__ == "__main__":
    json.dump(inventory(json.load(sys.stdin)), sys.stdout, ensure_ascii=True)
