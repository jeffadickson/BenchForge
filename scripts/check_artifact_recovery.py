#!/usr/bin/env python3
"""Exercise process-exit recovery against a fresh database, preserving the prior committed capture."""
import argparse
import hashlib
from pathlib import Path
import sqlite3
import subprocess
import tempfile


def main():
    """Run the compiled probe and assert SQLite recovery, bytes, digest and atomic outbox state."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--probe", default="tools/BenchDouble.ArtifactCrashProbe/bin/Debug/net10.0/BenchDouble.ArtifactCrashProbe.dll")
    args = parser.parse_args()
    probe = Path(args.probe).resolve()
    if not probe.is_file():
        parser.error("Build tools/BenchDouble.ArtifactCrashProbe before running recovery checks.")
    with tempfile.TemporaryDirectory(prefix="benchdouble-crash-") as directory:
        database = Path(directory) / "captures.sqlite"
        result = subprocess.run([args.dotnet, "exec", str(probe), str(database)], capture_output=True, text=True, timeout=30)
        if result.returncode != 97:
            raise RuntimeError(f"Probe failed to reach injected exit (code {result.returncode}): {result.stdout}\n{result.stderr}")
        with sqlite3.connect(database) as connection:
            if connection.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise AssertionError("SQLite integrity check failed after process exit")
            records = connection.execute("SELECT artifact_id,payload,sha256 FROM artifacts").fetchall()
            if len(records) != 1 or records[0][1] != bytes([1, 2, 3]):
                raise AssertionError("Interrupted capture leaked or committed capture was lost")
            artifact_id, payload, digest = records[0]
            if digest != hashlib.sha256(payload).hexdigest().upper():
                raise AssertionError("Committed payload digest mismatch")
            if connection.execute("SELECT artifact_id,state FROM artifact_outbox").fetchall() != [(artifact_id, "pending")]:
                raise AssertionError("Outbox is inconsistent with committed captures")
    print("Process-exit recovery passed: prior capture intact, interrupted transaction absent, outbox consistent.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
