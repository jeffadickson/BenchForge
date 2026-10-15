#!/usr/bin/env python3
"""Static requirement traceability gate. No external Python dependencies."""
import csv
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
DOC = ROOT / "requirements/benchdouble.sdoc"
TRACE = ROOT / "requirements/traceability.csv"
VALID = {"Planned", "Unverified", "SourceCommittedUnverified", "Verified", "Blocked"}
UID_PATTERN = re.compile(r"^UID:\s+([A-Z][A-Z0-9-]+)$", re.M)


def main() -> int:
    ids = UID_PATTERN.findall(DOC.read_text(encoding="utf-8"))
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
        if row["implementation_status"] == "Verified" and not row["verification_file"]:
            errors.append(f"{uid}: cannot mark Verified without verification evidence")
    for uid in sorted(set(ids) - set(by_id)):
        errors.append(f"Missing traceability row: {uid}")
    for uid in sorted(set(by_id) - set(ids)):
        errors.append(f"Unknown traceability requirement: {uid}")
    for uid, row in by_id.items():
        if row["implementation_status"] == "Planned" and not row["github_issue"]:
            errors.append(f"{uid}: planned work lacks GitHub issue")
    if errors:
        for error in errors:
            print("ERROR:", error, file=sys.stderr)
        return 1
    print(f"Traceability OK: {len(ids)} requirements, "
          f"{sum(r['implementation_status']=='Verified' for r in rows)} verified, "
          f"{sum(r['implementation_status']=='SourceCommittedUnverified' for r in rows)} source-committed unverified.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
