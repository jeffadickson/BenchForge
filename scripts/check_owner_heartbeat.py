#!/usr/bin/env python3
"""Run real-time owner-heartbeat expiry in a disposable process and verify retained journal evidence.

Requires a Release build of tools/BenchDouble.RecoveryProbe. No vendor driver or
physical safe-state behavior is exercised. BENCH_SETUP_DOTNET selects the runtime.
"""
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    """Run the bounded process probe and inspect durable ownership after its exit."""
    with tempfile.TemporaryDirectory(prefix="bench-heartbeat-") as directory:
        probe = ROOT / "tools/BenchDouble.RecoveryProbe/bin/Release/net10.0/BenchDouble.RecoveryProbe.dll"
        result = subprocess.run([os.environ.get("BENCH_SETUP_DOTNET", "dotnet"), str(probe), "heartbeat-expire", directory],
                                text=True, capture_output=True, timeout=20, check=True)
        document = json.loads((Path(directory) / "ownership.json").read_text())
        if len(document["records"]) != 1:
            raise RuntimeError("Heartbeat expiry erased unresolved ownership")
        print(result.stdout.strip())


if __name__ == "__main__":
    main()
