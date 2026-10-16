#!/usr/bin/env python3
"""Instrument Definer: extract ExternalInstrumentSource metadata without importing or executing drivers."""
import argparse
import ast
import csv
import json
import math
import re
import sys
from pathlib import Path

IDENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
QUANTITIES = {
    "ElectricPotential": ["Volt"], "ElectricCurrent": ["Ampere"],
    "ElectricResistance": ["Ohm"], "Frequency": ["Hertz"],
    "Temperature": ["DegreeCelsius", "Kelvin"], "Duration": ["Second"],
}
REVIEW = {"unreviewed", "sourceReviewed", "manualReviewed", "hardwareValidated"}
KINDS = {"control", "measurement", "setting", "status"}


def literal(node):
    """Evaluate a literal AST node; return None for unsupported expressions or evaluation errors."""
    try:
        return ast.literal_eval(node)
    except (ValueError, TypeError, SyntaxError, RecursionError, MemoryError):
        return None


def safe_json(value):
    """Keep only JSON-representable upstream literals, without guessing."""
    try:
        json.dumps(value, allow_nan=False)
        return value
    except (TypeError, ValueError):
        return None


def dotted(node):
    """Return a dotted name for Name/Attribute syntax; return an empty string for other expressions."""
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        return dotted(node.value) + "." + node.attr
    return ""


def draft(manufacturer, model, name, source="", revision=""):
    """Create an unreviewed schema-v1 draft with provenance; no hardware capability is validated."""
    return {
        "schemaVersion": 2, "definitionStatus": "extracted",
        "identity": {"manufacturer": manufacturer, "model": model,
                     "driverName": name, "instrumentClass": "Unclassified",
                     "protocol": "unknown", "interfaces": []},
        "provenance": {"upstreamProject": "ExternalInstrumentSource" if source else "manual",
                       "sourcePath": source, "sourceRevision": revision,
                       "license": "verify-upstream-license", "manualReference": ""},
        "modes": [], "capabilities": [], "operations": [],
        "mock": {"initialize": [], "measurementModels": [],
                 "virtualTime": True, "realTimeDelayScale": 1.0},
        "review": {"status": "unreviewed", "reviewer": "",
                   "hardwareTested": False,
                   "unresolved": ["Verify units and dimensions", "Review manual commands",
                                  "Review modes, ranges and output safety",
                                  "Verify license and source revision"]}
    }


def extract(path, manufacturer, model, revision, class_name=None):
    """Parse a Python source file into a review-only draft. Raise ValueError for missing or ambiguous classes; never import the source."""
    if path.stat().st_size > 2 * 1024 * 1024:
        raise ValueError("Upstream Python source exceeds 2 MiB extraction limit")
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    classes = [node for node in tree.body if isinstance(node, ast.ClassDef)]
    if not classes:
        raise ValueError("No class found in Python file")
    def count(cls):
        return sum(isinstance(n, ast.Assign) and isinstance(n.value, ast.Call)
                   and dotted(n.value.func) in
                   {"Instrument.control", "Instrument.measurement", "Instrument.setting"}
                   for n in cls.body)
    if class_name:
        candidates = [cls for cls in classes if cls.name == class_name]
    else:
        highest = max(map(count, classes))
        candidates = [cls for cls in classes if count(cls) == highest]
    if len(candidates) != 1:
        raise ValueError("Select exactly one upstream class with --class-name; available: "
                         + ", ".join(cls.name for cls in classes))
    cls = candidates[0]
    result = draft(manufacturer, model, cls.name, str(path), revision)
    result["provenance"]["upstreamClass"] = cls.name
    # Coverage is conservative: unresolved inherited and dynamic members
    # remain explicit blockers until a reviewer reconciles them.
    classes_by_name = {candidate.name: candidate for candidate in classes}
    inherited = []
    for base in cls.bases:
        base_name = dotted(base)
        if base_name in classes_by_name:
            for member in classes_by_name[base_name].body:
                if isinstance(member, (ast.FunctionDef, ast.AsyncFunctionDef)) and not member.name.startswith("_"):
                    inherited.append(base_name + "." + member.name)
                elif isinstance(member, ast.Assign):
                    inherited.extend(base_name + "." + target.id
                                     for target in member.targets if isinstance(target, ast.Name))
        else:
            inherited.append(base_name + " (external base; members unknown)")
    dynamic = []
    for member in ast.walk(cls):
        if isinstance(member, ast.Call) and dotted(member.func) in {
                "setattr", "exec", "eval", "globals", "locals"}:
            dynamic.append(f"line {member.lineno}: dynamic {dotted(member.func)}")
        elif isinstance(member, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if member.name in {"__getattr__", "__getattribute__", "__setattr__"} or any(
                    dotted(dec) in {"property", "cached_property", "functools.cached_property"}
                    for dec in member.decorator_list):
                dynamic.append(f"line {member.lineno}: dynamic/property {member.name}")
        elif isinstance(member, ast.AnnAssign) and isinstance(member.value, ast.Call) and (
                dotted(member.value.func).startswith("Instrument.")):
            dynamic.append(f"line {member.lineno}: annotated instrument descriptor")
    result["extraction"] = {
        "baseClasses": [ast.unparse(base) for base in cls.bases],
        "inheritedCandidates": sorted(set(inherited)),
        "dynamicCandidates": sorted(set(dynamic)),
        "unresolvedInheritedMembers": len(set(inherited)),
        "unresolvedDynamicMembers": len(set(dynamic))
    }
    if cls.bases:
        result["review"]["unresolved"].append(
            "Inherited members are not extracted; review bases: "
            + ", ".join(ast.unparse(base) for base in cls.bases))
    for node in cls.body:
        if (isinstance(node, ast.Assign) and len(node.targets) == 1
                and isinstance(node.targets[0], ast.Name)
                and isinstance(node.value, ast.Call)):
            descriptor = dotted(node.value.func)
            if descriptor not in {"Instrument.control", "Instrument.measurement", "Instrument.setting"}:
                continue
            kind = descriptor.split(".")[-1]
            args = [literal(arg) for arg in node.value.args]
            kw = {item.arg: literal(item.value) for item in node.value.keywords if item.arg}
            query = args[0] if kind != "setting" and args else None
            setter = args[1] if kind == "control" and len(args) > 1 else (
                args[0] if kind == "setting" and args else None)
            result["capabilities"].append({
                "name": node.targets[0].id, "kind": kind,
                "access": {"control": "readwrite", "measurement": "read",
                           "setting": "write"}[kind],
                "quantity": None, "unit": None,
                "query": kw.get("get_command", query)
                    if isinstance(kw.get("get_command", query), str) else None,
                "set": kw.get("set_command", setter)
                    if isinstance(kw.get("set_command", setter), str) else None,
                "allowedValues": safe_json(kw.get("values")),
                "validator": next((dotted(item.value) for item in node.value.keywords
                                   if item.arg == "validator"), ""),
                "modes": [], "mockDefault": None, "sourceLine": node.lineno,
                "reviewStatus": "unreviewed"
            })
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and not node.name.startswith("_"):
            result["operations"].append({
                "name": node.name,
                # Preserve defaults, annotations, positional-only/keyword-only
                # separators and variadics as source text; never evaluate them.
                "arguments": method_arguments(node),
                "notes": "Python signature: " + ast.unparse(node.args),
                "async": isinstance(node, ast.AsyncFunctionDef),
                "returnQuantity": None, "returnUnit": None,
                "sourceLine": node.lineno, "semantics": "manualReviewRequired",
                "reviewStatus": "unreviewed"
            })
    result["extraction"]["directMembers"] = (
        len(result["capabilities"]) + len(result["operations"]))
    return result


def method_arguments(node):
    """Return all parameter names in declaration order, excluding the bound receiver."""
    import copy
    args = copy.deepcopy(node.args)
    positional = args.posonlyargs or args.args
    is_static = any(dotted(item) == "staticmethod" for item in node.decorator_list)
    if not is_static and positional and positional[0].arg in {"self", "cls"}:
        # Defaults align with the trailing combined positional parameters.
        if len(args.defaults) == len(args.posonlyargs) + len(args.args):
            args.defaults.pop(0)
        positional.pop(0)
    names = [arg.arg for arg in args.posonlyargs + args.args]
    if args.vararg:
        names.append("*" + args.vararg.arg)
    names.extend(arg.arg for arg in args.kwonlyargs)
    if args.kwarg:
        names.append("**" + args.kwarg.arg)
    return names


def import_csv(properties, methods, manufacturer, model, name, manual):
    """Import manual-derived CSV tables into a draft. Reject missing columns or invalid numeric metadata rather than guessing."""
    result = draft(manufacturer, model, name)
    result["provenance"]["manualReference"] = manual
    with properties.open(newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        required = {"name", "kind", "access", "quantity", "unit", "query",
                    "set", "mode", "minimum", "maximum", "sourceLine", "notes"}
        if not required.issubset(reader.fieldnames or []):
            raise ValueError("Missing property columns: " +
                             ", ".join(sorted(required - set(reader.fieldnames or []))))
        for row in reader:
            result["capabilities"].append({
                "name": row["name"].strip(), "kind": row["kind"].strip(),
                "access": row["access"].strip(),
                "quantity": row["quantity"].strip() or None,
                "unit": row["unit"].strip() or None,
                "query": row["query"].strip() or None, "set": row["set"].strip() or None,
                "modes": [row["mode"].strip()] if row["mode"].strip() else [],
                "minimum": float(row["minimum"]) if row["minimum"].strip() else None,
                "maximum": float(row["maximum"]) if row["maximum"].strip() else None,
                "sourceLine": int(row["sourceLine"]) if row["sourceLine"].strip() else None,
                "notes": row["notes"],
                "responseFormat": row.get("responseFormat", ""),
                "basis": row.get("basis", ""),
                "sideEffects": row.get("sideEffects", ""),
                "sourcePath": row.get("sourcePath", ""),
                "sourceRevision": row.get("sourceRevision", ""),
                "mockDefault": None,
                "reviewStatus": "unreviewed"
            })
    # A spreadsheet may name a mode before its command is known. Record the
    # mode as a review placeholder rather than rejecting the imported draft.
    modes = sorted({mode for cap in result["capabilities"] for mode in cap["modes"]})
    for mode in modes:
        result["modes"].append({
            "id": mode, "selectionCommand": None, "initial": False,
            "notes": "Imported from CSV; mode command requires manual review"
        })
    if methods:
        with methods.open(newline="", encoding="utf-8-sig") as handle:
            reader = csv.DictReader(handle)
            required = {"name", "arguments", "returnQuantity", "returnUnit",
                        "sourceLine", "notes"}
            if not required.issubset(reader.fieldnames or []):
                raise ValueError("Missing method columns: " +
                                 ", ".join(sorted(required - set(reader.fieldnames or []))))
            for row in reader:
                result["operations"].append({
                    "name": row["name"].strip(),
                    "arguments": [x.strip() for x in row["arguments"].split(";") if x.strip()],
                    "returnQuantity": row["returnQuantity"].strip() or None,
                    "returnUnit": row["returnUnit"].strip() or None,
                    "sourceLine": int(row["sourceLine"]) if row["sourceLine"].strip() else None,
                    "notes": row["notes"],
                    "commands": row.get("commands", ""),
                    "preconditions": row.get("preconditions", ""),
                    "postconditions": row.get("postconditions", ""),
                    "timeoutMs": int(row["timeoutMs"]) if row.get("timeoutMs", "").strip() else None,
                    "sourcePath": row.get("sourcePath", ""),
                    "sourceRevision": row.get("sourceRevision", ""),
                    "semantics": "manualReviewRequired",
                    "reviewStatus": "unreviewed"
                })
    return result


def validate(doc):
    """Return schema/semantic validation errors for a draft; an empty list does not establish hardware compatibility."""
    errors = []
    def check(condition, message):
        if not condition:
            errors.append(message)
    check(doc.get("schemaVersion") == 2, "schemaVersion must be 2")
    identity = doc.get("identity") or {}
    for field in ("manufacturer", "model", "driverName"):
        check(bool(str(identity.get(field) or "").strip()), "Missing identity." + field)
    check(bool(IDENT.fullmatch(identity.get("driverName") or "")),
          "Invalid C# driverName")
    provenance = doc.get("provenance") or {}
    check(bool(provenance.get("sourcePath") or provenance.get("manualReference")),
          "sourcePath or manualReference required")
    caps, ops, modes = doc.get("capabilities"), doc.get("operations"), doc.get("modes")
    for name, value, limit in (("capabilities", caps, 1000),
                               ("operations", ops, 1000), ("modes", modes, 128)):
        check(isinstance(value, list) and len(value) <= limit, "Invalid " + name)
    caps, ops, modes = caps if isinstance(caps, list) else [], (
        ops if isinstance(ops, list) else []), modes if isinstance(modes, list) else []
    names = set()
    mode_names = {m.get("id") for m in modes if isinstance(m, dict)}
    check(len(mode_names) == len(modes), "Duplicate mode IDs")
    for mode in mode_names:
        check(isinstance(mode, str) and bool(IDENT.fullmatch(mode)),
              "Invalid mode identifier")
    for i, cap in enumerate(caps):
        name = cap.get("name") or ""
        prefix = f"capabilities[{i}]"
        check(bool(IDENT.fullmatch(name)) and name not in names, prefix + ": invalid/duplicate name")
        names.add(name)
        check(cap.get("kind") in KINDS, prefix + ": invalid kind")
        check(cap.get("access") in {"read", "write", "readwrite"}, prefix + ": invalid access")
        quantity, unit = cap.get("quantity"), cap.get("unit")
        check((quantity is None and unit is None) or
              (quantity in QUANTITIES and unit in QUANTITIES.get(quantity, [])),
              prefix + ": invalid quantity/unit")
        for key in ("minimum", "maximum", "mockDefault"):
            value = cap.get(key)
            check(value is None or (type(value) in (int, float) and math.isfinite(value)),
                  prefix + ": nonfinite " + key)
        low, high = cap.get("minimum"), cap.get("maximum")
        if type(low) in (int, float) and type(high) in (int, float):
            check(low <= high, prefix + ": invalid range")
        for mode in cap.get("modes") or []:
            check(mode in mode_names, prefix + ": undeclared mode " + str(mode))
        default = cap.get("mockDefault")
        if type(default) in (int, float):
            if type(low) in (int, float):
                check(default >= low, prefix + ": mockDefault below minimum")
            if type(high) in (int, float):
                check(default <= high, prefix + ": mockDefault above maximum")
    for op in ops:
        name = op.get("name") or ""
        check(bool(IDENT.fullmatch(name)) and name not in names,
              "Invalid/duplicate operation: " + name)
        names.add(name)
    review = doc.get("review") or {}
    check(review.get("status") in REVIEW, "Invalid review status")
    if review.get("status") == "hardwareValidated":
        check(review.get("hardwareTested") is True and bool(review.get("reviewer")),
              "Hardware validation requires evidence and reviewer")
    check(bool(caps or ops), "No capabilities or operations extracted")
    return errors


def save(path, doc):
    """Write a new UTF-8 JSON draft. Raise FileExistsError instead of overwriting reviewed work; reject nonfinite JSON numbers."""
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        raise FileExistsError("Refusing to overwrite " + str(path))
    path.write_text(json.dumps(doc, indent=2, ensure_ascii=False, allow_nan=False) + "\n",
                    encoding="utf-8")


def main():
    """Run the extract, CSV import or validation command selected by CLI arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    ex = sub.add_parser("extract-external_instrument_source")
    ex.add_argument("source", type=Path)
    ex.add_argument("--manufacturer", required=True)
    ex.add_argument("--model", required=True)
    ex.add_argument("--revision", required=True)
    ex.add_argument("--class-name", help="Explicit upstream class (required for ambiguous files)")
    ex.add_argument("--output", required=True, type=Path)
    imp = sub.add_parser("import-csv")
    imp.add_argument("properties", type=Path)
    imp.add_argument("--methods", type=Path)
    imp.add_argument("--manufacturer", required=True)
    imp.add_argument("--model", required=True)
    imp.add_argument("--driver-name", required=True)
    imp.add_argument("--manual", required=True)
    imp.add_argument("--output", required=True, type=Path)
    val = sub.add_parser("validate")
    val.add_argument("definition", type=Path)
    report = sub.add_parser("report", help="List unresolved quantities and review work")
    report.add_argument("definition", type=Path)
    args = parser.parse_args()
    try:
        if args.command == "extract-external_instrument_source":
            doc = extract(args.source, args.manufacturer, args.model, args.revision, args.class_name)
            errors = validate(doc)
            if errors:
                raise ValueError("; ".join(errors))
            save(args.output, doc)
            print(f"EXTRACTED {len(doc['capabilities'])} properties, "
                  f"{len(doc['operations'])} methods; review required")
        elif args.command == "import-csv":
            doc = import_csv(args.properties, args.methods, args.manufacturer,
                             args.model, args.driver_name, args.manual)
            errors = validate(doc)
            if errors:
                raise ValueError("; ".join(errors))
            save(args.output, doc)
            print("IMPORTED review draft " + str(args.output))
        else:
            doc = json.loads(args.definition.read_text(encoding="utf-8"))
            errors = validate(doc)
            for error in errors:
                print("ERROR: " + error, file=sys.stderr)
            if errors:
                return 1
            if args.command == "report":
                caps = doc["capabilities"]
                unknown = [cap["name"] for cap in caps
                           if cap.get("quantity") is None or cap.get("unit") is None]
                print(f"Instrument: {doc['identity']['manufacturer']} "
                      f"{doc['identity']['model']}")
                print(f"Capabilities: {len(caps)}, methods: {len(doc['operations'])}, "
                      f"modes: {len(doc['modes'])}")
                print(f"Unknown UnitsNet mappings ({len(unknown)}): " +
                      (", ".join(unknown) if unknown else "none"))
                print("Review status: " + doc["review"]["status"])
                coverage = doc.get("extraction", {})
                print("Unresolved inherited candidates: " +
                      str(coverage.get("unresolvedInheritedMembers", "not analyzed")))
                print("Unresolved dynamic candidates: " +
                      str(coverage.get("unresolvedDynamicMembers", "not analyzed")))
                for item in doc["review"].get("unresolved", []):
                    print("TODO: " + item)
            else:
                print(f"VALID schema v2 ({len(doc['capabilities'])} capabilities, "
                      f"{len(doc['operations'])} operations); review={doc['review']['status']}")
        return 0
    except (OSError, ValueError, SyntaxError, TypeError, KeyError) as exc:
        print("ERROR: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

