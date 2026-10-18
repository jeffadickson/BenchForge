"""Benchmark only equivalent checker ports, including process startup and report I/O.

Build Release binaries first. Never run application tests. Reproducible interleaved
candidate order, warmups, raw samples and output hashes are retained for review.
"""
import argparse
import hashlib
import json
import os
import platform
import random
import statistics
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BUILD = ROOT / "build/check-ports"
DOTNET = os.environ.get("BENCHFORGE_DOTNET", "dotnet")
DLL = Path(os.environ.get("BENCHFORGE_CHECKS_DLL", str(ROOT / "tools/BenchForge.Checks/bin/Release/net10.0/BenchForge.Checks.dll")))
AUDIT = Path(os.environ.get("BENCHFORGE_ROSLYN_DLL", str(ROOT / "tools/BenchDouble.DocumentationAudit/bin/Release/net10.0/BenchDouble.DocumentationAudit.dll")))
BASE = BUILD / "roslyn-baseline/bin/Release/net10.0/BenchDouble.DocumentationAudit.dll"
NATIVE = Path(os.environ.get("BENCHFORGE_CHECKS_NATIVE", str(BUILD / "native" / ("BenchForge.Checks.exe" if os.name == "nt" else "BenchForge.Checks"))))
ENV = {**os.environ, "PYTHONIOENCODING": "utf-8", "PYTHONDONTWRITEBYTECODE": "1"}


def execute(command, output):
    """Measure one fresh process; hash output artifacts outside the timed interval."""
    started = time.perf_counter_ns()
    result = subprocess.run(command, cwd=ROOT, env=ENV, capture_output=True, timeout=180)
    elapsed = (time.perf_counter_ns() - started) / 1e6
    assert result.returncode == 0, (command, result.returncode, result.stdout, result.stderr)
    digest = hashlib.sha256(result.stdout + b"\0" + result.stderr + b"\0" + (output.read_bytes() if output else b"")).hexdigest()
    return elapsed, digest


def main():
    """Select the lowest-median equivalent executable for each current-repository task."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--samples", type=int, default=9)
    parser.add_argument("--warmups", type=int, default=2)
    parser.add_argument("--tasks", help="Comma-separated subset; merge refreshed measurements into the existing report")
    parser.add_argument("--prepare-baseline", metavar="REF", help="Build original Roslyn source from an explicit Git revision")
    parser.add_argument("--prepare-only", action="store_true")
    args = parser.parse_args()
    if args.prepare_baseline:
        folder = BUILD / "roslyn-baseline"
        folder.mkdir(parents=True, exist_ok=True)
        for name in ("Program.cs", "BenchDouble.DocumentationAudit.csproj"):
            content = subprocess.check_output(["git", "show", args.prepare_baseline + ":tools/BenchDouble.DocumentationAudit/" + name], cwd=ROOT)
            (folder / name).write_bytes(content)
        subprocess.run([DOTNET, "build", str(folder), "-c", "Release"], cwd=ROOT, check=True)
    if args.prepare_only:
        return
    assert args.samples >= 5 and args.warmups >= 1
    tasks = [
        ("requirements", "check_requirements.py", [], None),
        ("scoreboard", "requirements_scoreboard.py", [], ROOT / "build/requirements-scoreboard.html"),
        ("neutrality", "check_source_neutrality.py", ["--output", "build/source-neutrality-audit.json"], ROOT / "build/source-neutrality-audit.json"),
        ("preflight", "check_docs_portable.py", ["--output", "build/docs-preflight.json"], ROOT / "build/docs-preflight.json"),
        ("docs", "check_api_docs.py", [], ROOT / "build/documentation-audit.json"),
        ("compare", "compare_doc_audits.py", ["--roslyn", "build/api-audit.json", "--portable", "build/docs-preflight.json", "--output", "build/docs-parity.json"], ROOT / "build/docs-parity.json"),
        ("roslyn", None, [], ROOT / "build/api-audit.json"),
    ]
    rng = random.Random(7301)
    reports = []
    for task, script, options, output in tasks:
        if args.tasks and task not in args.tasks.split(","):
            continue
        candidates = {"python": [sys.executable, str(ROOT / "scripts" / script), *options]} if script else {
            "csharp-original": [DOTNET, str(BASE), str(ROOT), str(output)]}
        for jobs in (1, 8):
            candidates[f"csharp-{jobs}"] = ([DOTNET, str(DLL), task, "--root", str(ROOT), "--jobs", str(jobs), "--python", sys.executable, *options] if script else
                                            [DOTNET, str(AUDIT), str(ROOT), str(output), "--jobs", str(jobs)])
            if script and NATIVE.exists():
                candidates[f"native-{jobs}"] = [str(NATIVE), task, "--root", str(ROOT), "--jobs", str(jobs), "--python", sys.executable, *options]
        # Reference script has a positional root; default '.' is already the same checkout.
        samples = {name: [] for name in candidates}
        expected = None
        for iteration in range(args.warmups + args.samples):
            order = list(candidates)
            rng.shuffle(order)
            for name in order:
                elapsed, digest = execute(candidates[name], output)
                if expected is None:
                    expected = digest
                assert digest == expected, (task, name, "output mismatch")
                if iteration >= args.warmups:
                    samples[name].append(elapsed)
        medians = {name: statistics.median(values) for name, values in samples.items()}
        winner = min(medians, key=medians.get)
        reference = "python" if script else "csharp-original"
        reports.append({"task": task, "winner": winner, "speedup": medians[reference] / medians[winner],
                        "median_ms": medians, "min_ms": {k: min(v) for k, v in samples.items()},
                        "max_ms": {k: max(v) for k, v in samples.items()}, "samples_ms": samples,
                        "identical_output_sha256": expected, "commands": candidates})
        print(task, "winner:", winner, "medians_ms:", medians, flush=True)
    if args.tasks:
        previous = json.loads((BUILD / "benchmarks.json").read_text(encoding="utf-8"))
        assert previous["samples_per_candidate"] == args.samples and previous["warmups_per_candidate"] == args.warmups
        replacements = {row["task"]: row for row in reports}
        reports = [replacements.get(row["task"], row) for row in previous["results"]]
    source_paths = [*sorted((ROOT / "tools/BenchForge.Checks").glob("*.cs")), *sorted((ROOT / "tools/BenchForge.Checks").glob("*.csproj")), ROOT / "tools/BenchForge.Checks/scoreboard.html", ROOT / "scripts/python_ast_inventory.py", ROOT / "tools/BenchDouble.DocumentationAudit/Program.cs", *[ROOT / "scripts" / script for _, script, _, _ in tasks if script]]
    report = {"platform": platform.platform(), "cpu": platform.processor(), "logical_processors": os.cpu_count(),
              "source_sha256": {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in source_paths},
              "python": sys.version, "dotnet": subprocess.check_output([DOTNET, "--version"], text=True).strip(),
              "commit_before_changes": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
              "samples_per_candidate": args.samples, "warmups_per_candidate": args.warmups, "seed": 7301,
              "timing": "Fresh-process wall time including startup, parsing, checking and report writes; output hashing excluded. Sequential interleaved candidates; no concurrent benchmarks. Warm file cache; not a cold-boot test.",
              "limitations": "Local Windows measurement on this checkout; rebenchmark on other hardware/platforms. Python AST helper is included in C# documentation timings. Build/restore excluded from timings; Release DLL invocation used for C#.",
              "results": reports}
    BUILD.mkdir(parents=True, exist_ok=True)
    (BUILD / "benchmarks.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    lines = ["# Check/report port benchmarks", "", report["platform"], "", report["timing"], "", report["limitations"], "",
             f"{args.samples} measured fresh processes and {args.warmups} warmups per candidate. Medians in milliseconds.", "",
             "| Task | Reference | C# 1 worker | C# 8 workers | Native 1 worker | Native 8 workers | Selected | Speedup |", "|---|---:|---:|---:|---:|---:|---|---:|"]
    for row in reports:
        m = row["median_ms"]
        native = [f'{m[k]:.2f}' if k in m else '—' for k in ('native-1', 'native-8')]
        lines.append(f'| {row["task"]} | {m.get("python", m.get("csharp-original")):.2f} | {m["csharp-1"]:.2f} | {m["csharp-8"]:.2f} | {native[0]} | {native[1]} | {row["winner"]} | {row["speedup"]:.2f}x |')
    lines += ["", "Every measured run matched stdout, stderr, exit status and report bytes. Raw samples, hashes, commands and runtime versions are in benchmarks.json."]
    (BUILD / "benchmarks.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
