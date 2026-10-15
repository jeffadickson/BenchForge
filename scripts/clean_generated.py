#!/usr/bin/env python3
"""Safely remove only the dedicated v2 generated C# source directory."""
from pathlib import Path
import shutil

repo = Path(__file__).resolve().parents[1]
parent = (repo / "generated" / "BenchForge.Instruments").resolve()
target = (parent / "Code").resolve()
if parent != (repo / "generated" / "BenchForge.Instruments").resolve() or target.parent != parent:
    raise RuntimeError("Unsafe generated directory")
if target.is_symlink():
    raise RuntimeError("Refusing to follow generated source symlink")
if target.exists():
    if not target.is_dir():
        raise RuntimeError("Generated source path is not a directory")
    shutil.rmtree(target)
print("Removed only generated/BenchForge.Instruments/Code")
