#!/usr/bin/env python3
"""Static requirement traceability gate. No external Python dependencies."""
import csv
import pathlib
import re
import sys
import hashlib
import json

ROOT = pathlib.Path(__file__).resolve().parents[1]
DOC = ROOT / "requirements/benchdouble.sdoc"
TRACE = ROOT / "requirements/traceability.csv"
VALID = {
    "Planned", "Unverified", "SourceCommittedUnverified",
    "PartialSourceUnverified", "DesignOnly", "NoImplementationEvidence",
    "Verified", "Blocked"
}
AUDIT = ROOT / "requirements/legacy-runner-audit.csv"
AUDIT_TO_STATUS = {
    "PartialSource": "PartialSourceUnverified",
    "DesignOnly": "DesignOnly",
    "NoImplementationEvidence": "NoImplementationEvidence",
}
UID_PATTERN = re.compile(r"^UID:\s+([A-Z][A-Z0-9-]+)$", re.M)


def main() -> int:
    source = DOC.read_text(encoding="utf-8")
    ids = UID_PATTERN.findall(source)
    ids = [uid for uid in ids if uid != "BD-BASELINE"]
    errors = []
    if len(ids) != len(set(ids)):
        errors.append("Duplicate requirement UID in StrictDoc source")
    with TRACE.open(encoding="utf-8", newline="") as file:
        rows = list(csv.DictReader(file))
    by_id = {}
    for row in rows:
        uid = row["requirement_id"]
        if uid in by_id:
            errors.append(f"Duplicate traceability row: {uid}")
        by_id[uid] = row
        if row["implementation_status"] not in VALID:
            errors.append(f"{uid}: invalid status {row['implementation_status']}")
        for column in ("implementation_file", "verification_file"):
            path = row[column].strip()
            if path and not (ROOT / path).is_file():
                errors.append(f"{uid}: missing {column}: {path}")
        issue = row["github_issue"]
        if issue and not re.fullmatch(
            r"https://github\.com/jeffadickson/BenchDouble/issues/[0-9]+", issue
        ):
            errors.append(f"{uid}: malformed GitHub issue URL")
        if not issue:
            errors.append(f"{uid}: requirement lacks GitHub issue")
        if row["implementation_status"] == "Verified":
            evidence = row.get("verification_evidence", "").strip()
            if not row["verification_file"] or not evidence:
                errors.append(f"{uid}: cannot mark Verified without test and execution evidence")
            else:
                try:
                    report = json.loads((ROOT / evidence).read_text(encoding="utf-8"))
                    if (report.get("result") != "passed" or
                        not re.fullmatch(r"[0-9a-f]{40}", report.get("tested_commit", "")) or
                        not report.get("platform") or not report.get("command") or
                        uid not in report.get("requirements", [])):
                        errors.append(f"{uid}: incomplete passing execution evidence")
                except (OSError, ValueError, TypeError, AttributeError):
                    errors.append(f"{uid}: invalid execution evidence report")
    for uid in sorted(set(ids) - set(by_id)):
        errors.append(f"Missing traceability row: {uid}")
    for uid in sorted(set(by_id) - set(ids)):
        errors.append(f"Unknown traceability requirement: {uid}")
    for uid, row in by_id.items():
        if row["implementation_status"] == "Planned" and not row["github_issue"]:
            errors.append(f"{uid}: planned work lacks GitHub issue")
    # Audit the entire original 90-requirement runner baseline against the
    # canonical traceability matrix. Do not let the two reports drift.
    with AUDIT.open(encoding="utf-8", newline="") as file:
        audited = list(csv.DictReader(file))
    expected_legacy = {f"TR-{n:03d}" for n in range(1, 91)}
    audited_ids = [row["requirement_id"] for row in audited]
    if len(audited_ids) != len(set(audited_ids)):
        errors.append("Duplicate requirement in legacy runner audit")
    if set(audited_ids) != expected_legacy:
        errors.append("Legacy runner audit must contain exactly TR-001 through TR-090")
    for audit in audited:
        uid = audit["requirement_id"]
        expected_status = AUDIT_TO_STATUS.get(audit["audit_assessment"])
        if expected_status is None:
            errors.append(f"{uid}: unknown audit assessment")
            continue
        row = by_id.get(uid)
        if row is None:
            errors.append(f"{uid}: audited requirement missing from traceability")
            continue
        if row["implementation_status"] != expected_status:
            errors.append(f"{uid}: audit/traceability status mismatch")
        if row["implementation_file"] != audit["source_evidence"]:
            errors.append(f"{uid}: audit/traceability source mismatch")
        if row["verification_file"] != audit["test_evidence"]:
            errors.append(f"{uid}: audit/traceability test mismatch")
        if row["github_issue"] != audit["tracking_issue"]:
            errors.append(f"{uid}: audit/traceability issue mismatch")
        if not audit["remaining_gap"].strip():
            errors.append(f"{uid}: missing specific audit gap")
        for field in ("source_evidence", "test_evidence"):
            path = audit[field].strip()
            if path and not (ROOT / path).is_file():
                errors.append(f"{uid}: missing audited evidence path {path}")
        if expected_status == "PartialSourceUnverified" and not audit["source_evidence"]:
            errors.append(f"{uid}: partial source claim lacks source evidence")
    if any(row["implementation_status"] == "Verified" for row in rows):
        # CI's file-existence check is not a substitute for test-run evidence.
        print("NOTICE: Verified statuses require separately reviewed passing CI evidence.")
    # Audit every requirement, not only the historical TR subset. A statement
    # fingerprint forces an explicit review when requirement wording changes.
    statements = {}
    for block in source.split("[REQUIREMENT]")[1:]:
        uid = re.search(r"^UID: (.+)$", block, re.M)
        statement = re.search(r"^STATEMENT: (.+)$", block, re.M)
        if uid and statement:
            statements[uid[1]] = statement[1]
    full_path = ROOT / "requirements/full-audit.csv"
    if not full_path.is_file():
        errors.append("Missing complete requirements audit")
    else:
        with full_path.open(encoding="utf-8", newline="") as file:
            full = list(csv.DictReader(file))
        full_ids = [r["requirement_id"] for r in full]
        if len(full_ids) != len(set(full_ids)) or set(full_ids) != set(ids):
            errors.append("Full audit must contain every requirement exactly once")
        for audit in full:
            uid = audit["requirement_id"]
            row = by_id.get(uid, {})
            for field in ("priority", "implementation_status", "implementation_file", "verification_file", "github_issue"):
                if audit.get(field) != row.get(field):
                    errors.append(f"{uid}: full audit/traceability {field} mismatch")
            fingerprint = hashlib.sha256(statements.get(uid, "").encode()).hexdigest()
            if audit.get("requirement_sha256") != fingerprint:
                errors.append(f"{uid}: requirement changed since full audit")
            if not audit.get("finding", "").strip() or not audit.get("acceptance_criteria", "").strip():
                errors.append(f"{uid}: full audit lacks finding or acceptance criteria")
    if errors:
        for error in errors:
            print("ERROR:", error, file=sys.stderr)
        return 1
    print(f"Traceability OK: {len(ids)} requirements, "
          f"{sum(r['implementation_status']=='Verified' for r in rows)} verified, "
          f"{sum(r['implementation_status'] in ('SourceCommittedUnverified','PartialSourceUnverified') for r in rows)} source-linked unverified, "
          f"{len(audited)} original runner requirements audited.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

