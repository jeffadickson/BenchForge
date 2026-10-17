#!/usr/bin/env python3
"""Exercise actual SIGTERM/Generic Host shutdown, without vendor I/O or hardware claims.

Build tools/BenchDouble.RecoveryProbe in Release first. BENCH_SETUP_DOTNET selects
its runtime. Each process uses an isolated journal and a finite exit deadline.
"""
import json
import os
from pathlib import Path
import selectors
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
PROBE = ROOT / "tools/BenchDouble.RecoveryProbe/bin/Release/net10.0/BenchDouble.RecoveryProbe.dll"


def main():
    """Send SIGTERM after readiness and check both clean release and retained startup quarantine."""
    runtime = os.environ.get("BENCH_SETUP_DOTNET", "dotnet")
    for mode, expected_records in [("graceful", 0), ("graceful-retained", 1)]:
        with tempfile.TemporaryDirectory(prefix="bench-shutdown-") as directory:
            child = subprocess.Popen([runtime, str(PROBE), mode, directory], stdout=subprocess.PIPE, text=True)
            try:
                with selectors.DefaultSelector() as selector:
                    selector.register(child.stdout, selectors.EVENT_READ)
                    if not selector.select(20) or child.stdout.readline().strip() != "READY":
                        raise RuntimeError("Shutdown probe did not become ready")
                child.terminate()
                if child.wait(timeout=10) != 0:
                    raise RuntimeError("Host shutdown failed")
                document = json.loads((Path(directory) / "ownership.json").read_text())
                if len(document["records"]) != expected_records:
                    raise RuntimeError("Shutdown erased quarantine or stranded a clean reservation")
            finally:
                if child.poll() is None:
                    child.kill()
                    child.wait(timeout=10)
                child.stdout.close()
    print("PASS: SIGTERM closes a plain canonical reservation and retains startup quarantine; no vendor I/O.")


if __name__ == "__main__":
    main()
