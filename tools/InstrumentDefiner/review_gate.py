#!/usr/bin/env python3
"""Review and generation-readiness gate for Instrument Definer schema-v2 drafts.

This is a *metadata quality gate*, not hardware validation or approval.
No source files are imported or executed. The tool never modifies a definition.
"""
import argparse
import json
import re
import sys
from pathlib import Path

from definer import validate

PINNED_SHA = re.compile(r"^[0-9a-fA-F]{40}$")
REVIEW_LEVELS = {"sourceReviewed": 1, "manualReviewed": 2, "hardwareValidated": 3}
PLACEHOLDERS = {"", "unknown", "none", "tbd", "todo", "placeholder",
                "pin_upstream_commit", "pinned_sha", "verify-upstream-license"}


def specified(value):
    """Return true only for a non-placeholder, nonempty evidence string."""
    return isinstance(value, str) and value.strip().lower().replace(" ", "_") not in PLACEHOLDERS


def audit(definition, target="manualReviewed"):
    """Return deterministic findings. An empty blockers list means metadata-ready only."""
    if target not in REVIEW_LEVELS:
        raise ValueError("Unknown review target " + target)
    errors = validate(definition)
    if errors:
        return {"target": target, "ready": False, "blockers": sorted(set(errors)),
                "warnings": [], "metrics": {}}
    identity = definition["identity"]
    provenance = definition["provenance"]
    review = definition["review"]
    caps = definition["capabilities"]
    operations = definition["operations"]
    modes = definition["modes"]
    blockers = []
    warnings = []
    def block(condition, reason):
        """Record a review blocker with supporting evidence."""
        if condition:
            blockers.append(reason)

    if provenance.get("upstreamProject") == "manual":
        block(not specified(provenance.get("manualReference")),
              "Manual-entry definition requires manufacturer documentation")
    else:
        block(not specified(provenance.get("sourcePath")), "Missing upstream source path")
        block(not PINNED_SHA.fullmatch(provenance.get("sourceRevision") or ""),
              "Upstream source revision must be a full 40-character commit SHA")
    block(not specified(provenance.get("license")) or
          "verify" in provenance["license"].lower(),
          "Upstream license review is missing")
    block(not specified(review.get("reviewer")), "Named reviewer required")
    block(bool(review.get("unresolved")), "Unresolved review items remain")
    block(review.get("status") not in REVIEW_LEVELS or
          REVIEW_LEVELS.get(review.get("status"), 0) < REVIEW_LEVELS[target],
          f"Review status has not reached {target}")
    block(REVIEW_LEVELS.get(definition.get("definitionStatus"), 0) < REVIEW_LEVELS[target],
          f"Definition status has not reached {target}")

    if target in ("manualReviewed", "hardwareValidated"):
        block(not specified(provenance.get("manualReference")),
              "Manufacturer programming manual reference required")
        block(not specified(review.get("manualEvidence")),
              "Manual-review evidence reference required")
        block(identity.get("protocol") == "unknown",
              "Transport protocol must be classified")
        for mode in modes:
            block(not specified(mode.get("selectionCommand")),
                  f"Mode {mode['id']}: selection command not reviewed")
        for cap in caps:
            name = cap["name"]
            block(cap.get("reviewStatus") not in ("manualReviewed", "hardwareValidated"),
                  f"Capability {name}: manual review not recorded")
            access = cap["access"]
            block(access in ("read", "readwrite") and not specified(cap.get("query")),
                  f"Capability {name}: missing query command")
            block(access in ("write", "readwrite") and not specified(cap.get("set")),
                  f"Capability {name}: missing set command")
            if cap.get("kind") == "measurement":
                block(not cap.get("quantity") or not cap.get("unit"),
                      f"Measurement {name}: UnitsNet quantity/unit unresolved")
        for op in operations:
            name = op["name"]
            block(op.get("reviewStatus") not in ("manualReviewed", "hardwareValidated"),
                  f"Method {name}: manual review not recorded")
            block(op.get("semantics") == "manualReviewRequired",
                  f"Method {name}: semantics unresolved")
            block(not specified(op.get("commands")) and not specified(op.get("notes")),
                  f"Method {name}: no command or algorithm documentation")
        coverage = definition.get("extraction", {})
        if provenance.get("upstreamProject") != "manual":
            block(not coverage and not specified(review.get("coverageEvidence")),
                  "Upstream extraction coverage or independent coverage evidence required")
        block(bool(coverage.get("inheritedCandidates")) and
              not specified(review.get("inheritanceEvidence")),
              "Inherited member reconciliation evidence required")
        block(bool(coverage.get("dynamicCandidates")) and
              not specified(review.get("dynamicEvidence")),
              "Dynamic member reconciliation evidence required")
        block(coverage.get("unresolvedDynamicMembers", 0) > 0,
              "Dynamic instrument members have not been reconciled")
        block(coverage.get("unresolvedInheritedMembers", 0) > 0,
              "Inherited instrument members have not been reconciled")

    if target == "hardwareValidated":
        block(review.get("hardwareTested") is not True,
              "Hardware-tested flag required")
        block(not specified(review.get("hardwareEvidence")),
              "Hardware-test evidence reference required")
        block(not specified(review.get("testFixture")),
              "Hardware fixture identification required")

    for cap in caps:
        if cap.get("mockDefault") is None:
            warnings.append(f"Capability {cap['name']}: no default mock value")
    if not modes:
        warnings.append("No operating modes declared (verify single-mode instrument)")
    metrics = {
        "capabilities": len(caps), "methods": len(operations), "modes": len(modes),
        "quantityMapped": sum(c.get("quantity") is not None and c.get("unit") is not None
                              for c in caps),
        "manualReviewedMembers": sum(c.get("reviewStatus") in
                                     ("manualReviewed", "hardwareValidated")
                                     for c in caps + operations),
        "totalMembers": len(caps) + len(operations)
    }
    return {"instrument": f"{identity['manufacturer']} {identity['model']}",
            "target": target, "ready": not blockers,
            "blockers": sorted(set(blockers)), "warnings": sorted(set(warnings)),
            "metrics": metrics,
            "disclaimer": "Metadata gate only: not compilation, physical safety, or hardware validation."}


def main():
    """Run the command-line validation and report results."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("definition", type=Path)
    parser.add_argument("--target", choices=list(REVIEW_LEVELS),
                        default="manualReviewed")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable audit")
    args = parser.parse_args()
    try:
        doc = json.loads(args.definition.read_text(encoding="utf-8"))
        result = audit(doc, args.target)
        if args.json:
            print(json.dumps(result, indent=2, sort_keys=True))
        else:
            print(f"{result.get('instrument', args.definition.name)} "
                  f"{args.target}: {'READY (metadata only)' if result['ready'] else 'BLOCKED'}")
            for issue in result["blockers"]:
                print("BLOCK: " + issue)
            for warning in result["warnings"]:
                print("WARN: " + warning)
        return 0 if result["ready"] else 1
    except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as exc:
        print("ERROR: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
