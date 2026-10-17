#!/usr/bin/env python3
"""Kill a managed owner process and verify journal quarantine in a fresh process.

Build tools/BenchDouble.RecoveryProbe in Release first. BENCH_SETUP_DOTNET selects
its dotnet runtime. Tests process termination, not power loss or physical recovery.
"""
import os
from pathlib import Path
import selectors
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
PROBE = ROOT / "tools/BenchDouble.RecoveryProbe/bin/Release/net10.0/BenchDouble.RecoveryProbe.dll"


def main():
    """Run bounded child processes against an isolated temporary journal; raise on failure."""
    runtime = os.environ.get("BENCH_SETUP_DOTNET", "dotnet")
    with tempfile.TemporaryDirectory(prefix="bench-ownership-crash-") as directory:
        child = subprocess.Popen([runtime, str(PROBE), "hold", directory], stdout=subprocess.PIPE, text=True)
        try:
            with selectors.DefaultSelector() as selector:
                selector.register(child.stdout, selectors.EVENT_READ)
                if not selector.select(20) or child.stdout.readline().strip() != "READY":
                    raise RuntimeError("Owner did not persist its grant before the deadline")
            child.kill()
            child.wait(timeout=10)
            result = subprocess.run([runtime, str(PROBE), "verify", directory], capture_output=True, text=True, timeout=20, check=True)
            print(result.stdout.strip())
        finally:
            if child.poll() is None:
                child.kill()
                child.wait(timeout=10)
            child.stdout.close()


if __name__ == "__main__":
    main()
