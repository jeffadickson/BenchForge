"""Focused differential checks for C# ports; never runs application test suites."""
import csv
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOTNET = os.environ.get("BENCHFORGE_DOTNET", "dotnet")
DLL = Path(os.environ.get("BENCHFORGE_CHECKS_DLL", str(ROOT / "tools/BenchForge.Checks/bin/Release/net10.0/BenchForge.Checks.dll")))
NATIVE = Path(os.environ.get("BENCHFORGE_CHECKS_NATIVE", str(ROOT / ".tools/checks/native" / ("BenchForge.Checks.exe" if os.name == "nt" else "BenchForge.Checks"))))
REFERENCE = Path(__file__).with_name("reference.py")
ENV = {**os.environ, "PYTHONIOENCODING": "utf-8", "PYTHONDONTWRITEBYTECODE": "1"}
RESULTS = []


def run(command, root, args=(), jobs=1, reference=False, native=False):
    """Capture independent process output exactly, including exit status."""
    cmd = ([sys.executable, str(REFERENCE), command, str(root)] if reference else
           [*([str(NATIVE)] if native else [DOTNET, str(DLL)]), command, "--root", str(root), "--jobs", str(jobs), "--python", sys.executable])
    return subprocess.run([*cmd, *map(str, args)], cwd=root, env=ENV, capture_output=True, timeout=180)


def parity(name, command, root, args=(), outputs=(), expected=None):
    """Require identical stdout, stderr, exit code and report bytes in both worker modes."""
    old = run(command, root, args, reference=True)
    if expected is not None:
        assert old.returncode == expected, (name, old.returncode, old.stderr)
    original = {p: (root / p).read_bytes() for p in outputs}
    for jobs, native in [(j, n) for n in ([False, True] if NATIVE.exists() else [False]) for j in (1, 8)]:
        new = run(command, root, args, jobs, native=native)
        assert (new.returncode, new.stdout, new.stderr) == (old.returncode, old.stdout, old.stderr), (name, jobs, old.stdout, new.stdout, old.stderr, new.stderr)
        for p, content in original.items():
            actual = (root / p).read_bytes()
            if actual != content:
                (ROOT / "build/check-ports/expected-output").write_bytes(content)
                (ROOT / "build/check-ports/actual-output").write_bytes(actual)
                raise AssertionError((name, jobs, p, "report bytes differ; see build/check-ports/{expected,actual}-output"))
    RESULTS.append({"case": name, "jobs": [1, 8], "runtimes": ["managed", "native"] if NATIVE.exists() else ["managed"], "exact": True})
    print("PASS", name, flush=True)


def write(root, path, value):
    """Create a UTF-8 fixture, retaining caller-supplied newline and BOM cases."""
    p = root / path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(value, encoding="utf-8")


def mutate(root, name, action):
    """Apply a CSV mutation while preserving quoted fields and canonical headers."""
    p = root / "requirements" / name
    with p.open(newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    fields = list(rows[0])
    action(rows)
    with p.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        w.writerows(rows)


def fixtures(root):
    """Check error order, evidence validation, lexical cases, HTML and report escaping."""
    req = root / "requirements"
    req.mkdir()
    for name in ("benchdouble.sdoc", "traceability.csv", "full-audit.csv", "legacy-runner-audit.csv"):
        shutil.copy2(ROOT / "requirements" / name, req / name)
    originals = {p: p.read_bytes() for p in req.iterdir()}
    with (req / "traceability.csv").open(newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            for key in ("implementation_file", "verification_file"):
                if row[key]:
                    p = root / row[key]
                    p.parent.mkdir(parents=True, exist_ok=True)
                    p.touch()
    mutations = [
        ("valid baseline", None, None, 0),
        ("missing full audit row", "full-audit.csv", lambda r: r.pop(), 1),
        ("duplicate full audit row", "full-audit.csv", lambda r: r.append(r[-1].copy()), 1),
        ("missing acceptance", "full-audit.csv", lambda r: r[-1].update(acceptance_criteria=""), 1),
        ("issue missing", "traceability.csv", lambda r: r[-1].update(github_issue=""), 1),
        ("issue newline", "traceability.csv", lambda r: r[-1].update(github_issue=r[-1]["github_issue"] + "\n"), 1),
        ("unknown status", "traceability.csv", lambda r: r[-1].update(implementation_status="Oops"), 1),
        ("missing source", "traceability.csv", lambda r: r[-1].update(implementation_file="missing.cs"), 1),
        ("duplicate trace", "traceability.csv", lambda r: r.append(r[-1].copy()), 1),
        ("planned without issue", "traceability.csv", lambda r: r[-1].update(implementation_status="Planned", github_issue=""), 1),
        ("unknown legacy assessment", "legacy-runner-audit.csv", lambda r: r[-1].update(audit_assessment="Oops"), 1),
        ("duplicate legacy", "legacy-runner-audit.csv", lambda r: r.append(r[-1].copy()), 1),
        ("quoted multiline CSV", "full-audit.csv", lambda r: r[-1].update(finding='Unicode café, "quoted"\nnext line'), 0),
    ]
    for name, file, action, code in mutations:
        for p, content in originals.items():
            p.write_bytes(content)
        if file:
            mutate(root, file, action)
        parity("requirements: " + name, "requirements", root, expected=code)
    for p, content in originals.items():
        p.write_bytes(content)
    sdoc = req / "benchdouble.sdoc"
    sdoc.write_text(sdoc.read_text(encoding="utf-8").replace("STATEMENT: ", "STATEMENT: changed ", 1), encoding="utf-8")
    parity("requirements: changed hash", "requirements", root, expected=1)
    with (req / "traceability.csv").open(newline="", encoding="utf-8") as f:
        uid = list(csv.DictReader(f))[-1]["requirement_id"]
    valid = {"result": "passed", "tested_commit": "a" * 40, "platform": "windows", "command": "focused", "requirements": [uid]}
    evidence_cases = [None, "{bad", "[]", '{"result":"failed"}', json.dumps(valid)]
    evidence_cases += [json.dumps({**valid, **patch}) for patch in ({"result": 2}, {"tested_commit": None}, {"tested_commit": 2}, {"platform": 0.0}, {"requirements": None}, {"requirements": {uid: True}}, {"requirements": uid})]
    for evidence in evidence_cases:
        for p, content in originals.items():
            p.write_bytes(content)
        for file in ("traceability.csv", "full-audit.csv"):
            mutate(root, file, lambda r: r[-1].update(implementation_status="Verified"))
        if evidence is not None:
            write(root, "build/evidence.json", evidence)
            mutate(root, "traceability.csv", lambda r: r[-1].update(verification_evidence="build/evidence.json"))
        parity("requirements: verified evidence " + repr(evidence)[:35], "requirements", root)
    for p, content in originals.items():
        p.write_bytes(content)
    sdoc.write_text(sdoc.read_text(encoding="utf-8") + "\nUID: TR-001\n", encoding="utf-8")
    (req / "full-audit.csv").unlink()
    parity("requirements: duplicate UID and missing full audit", "requirements", root, expected=1)
    # Reset fixture to a small documentation/source audit tree.
    for area in ("src", "tools", "scripts", "examples", "tests", "generated"):
        shutil.rmtree(root / area, ignore_errors=True)
        (root / area).mkdir()
    write(root, "src/Meter.cs", '\ufeff/// <summary>Meter.</summary>\npublic class Meter {\n'
          '/// <summary>Read.</summary>\n/// <param name="channel">Channel.</param>\n/// <returns>Value.</returns>\npublic double Read(int channel) => 1;\n'
          'public Meter(int channel) {}\npublic string Bad(System.Collections.Generic.Dictionary<string, int> map, int n = 2) => "public void Fake() {}";\n}\n'
          '/// <summary>Capture.</summary>\npublic record Capture(int Channel, double Voltage);\n'
          '/// <inheritdoc/>\npublic class Inherited {}\n')
    write(root, "tools/sample.py", '"""Module café."""\nraise RuntimeError("must never execute")\nclass Example:\n    def method(self):\n        pass\nif True:\n    async def conditional():\n        """Documented."""\n        pass\n')
    write(root, "generated/preview.g.cs", 'public record Preview(int Channel);\n')
    for file in ("src/Area.cs", "src/Area/Z.cs", "src/Area.More/A.cs", "src/z.cs"):
        write(root, file, "public class Undocumented {}\n")
    write(root, "build/api-audit.json", '[{"path":"src/Generated/Test.g.cs","symbol":"Test","line":1,"issues":["summary"]}]')
    write(root, "docs/api-documentation-baseline.json", '["src/Generated/Test.g.cs::Test::summary"]')
    parity("docs: generated baseline never waived; source never executed", "docs", root, outputs=["build/documentation-audit.json"], expected=1)
    write(root, "build/api-audit.json", "[]")
    parity("docs: nested Python definitions and resolution", "docs", root, outputs=["build/documentation-audit.json"], expected=1)
    parity("preflight: BOM generic constructor record comments and Python AST", "preflight", root, ["--output", root / "build/preflight.json", "--strict"], ["build/preflight.json"], 1)
    write(root, "build/roslyn.json", '[{"path":"src/Meter.cs","line":10,"issues":["summary","param:channel"]}]')
    write(root, "build/portable.json", '{"findings":[{"path":"src/Meter.cs","line":11,"issues":["summary","returns"]}]}')
    parity("compare: ties missed extra and ratios", "compare", root, ["--roslyn", root / "build/roslyn.json", "--portable", root / "build/portable.json", "--output", root / "build/parity.json"], ["build/parity.json"], 0)
    write(root, "build/roslyn.json", "[]")
    parity("compare: no expected issues", "compare", root, ["--roslyn", root / "build/roslyn.json", "--portable", root / "build/portable.json", "--output", root / "build/parity.json"], ["build/parity.json"], 0)
    write(root, "requirements/traceability.csv", 'requirement_id,priority,implementation_status,github_issue\n"ID,<>&",P0,"café & <tag>","https://example.test/?x=\'&y="""\n')
    parity("scoreboard: quoted HTML and unicode", "scoreboard", root, outputs=["build/requirements-scoreboard.html"], expected=0)
    write(root, "requirements/traceability.csv", 'requirement_id,priority,implementation_status,github_issue\nA,"line1\r\nline2",Planned,issue\n')
    parity("scoreboard: embedded CRLF", "scoreboard", root, outputs=["build/requirements-scoreboard.html"], expected=0)
    write(root, "requirements/traceability.csv", "requirement_id,priority,implementation_status,github_issue\n")
    parity("scoreboard: empty", "scoreboard", root, outputs=["build/requirements-scoreboard.html"], expected=0)
    term = "Py" + "Measure"
    write(root, "THIRD_PARTY_REVIEW.md", term)
    write(root, term + "-note.md", "Legacy " + term.lower() + "\n" + term.upper())
    write(root, "skip.md", "literal \\0 " + term)
    write(root, "unicode.md", term.replace("s", "ſ") + "\u2028" + term)
    (root / "binary.dat").write_bytes(b"\xff" + term.encode())
    subprocess.run(["git", "init", "-q"], cwd=root, check=True, capture_output=True)
    subprocess.run(["git", "add", "."], cwd=root, check=True, capture_output=True)
    parity("neutrality: legal binary unicode filename and content", "neutrality", root, ["--strict", "--output", root / "build/neutral.json"], ["build/neutral.json"], 1)


def main():
    """Run only port checks and persist the exact-comparison inventory."""
    for key, selected in (("BENCHFORGE_CHECKS_DLL", DLL), ("BENCHFORGE_CHECKS_NATIVE", NATIVE)):
        if key in os.environ and not selected.is_file():
            raise FileNotFoundError("Explicit checker selection does not exist: " + key)
    if "BENCHFORGE_ROSLYN_DLL" in os.environ and not Path(os.environ["BENCHFORGE_ROSLYN_DLL"]).is_file():
        raise FileNotFoundError("Explicit Roslyn selection does not exist")

    folder = ROOT / "build/check-ports"
    folder.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="parity-", dir=folder) as temp:
        fixtures(Path(temp))
    output = ROOT / "build/api-audit.json"
    original = folder / "roslyn-baseline/bin/Release/net10.0/BenchDouble.DocumentationAudit.dll"
    updated = Path(os.environ.get("BENCHFORGE_ROSLYN_DLL", str(ROOT / "tools/BenchDouble.DocumentationAudit/bin/Release/net10.0/BenchDouble.DocumentationAudit.dll")))
    baseline = subprocess.run([DOTNET, str(original), str(ROOT), str(output)], cwd=ROOT, capture_output=True, check=True)
    content = output.read_bytes()
    for jobs in (1, 8):
        current = subprocess.run([DOTNET, str(updated), str(ROOT), str(output), "--jobs", str(jobs)], cwd=ROOT, capture_output=True, check=True)
        assert (current.stdout, current.stderr, output.read_bytes()) == (baseline.stdout, baseline.stderr, content)
    RESULTS.append({"case": "Roslyn repository report", "jobs": [1, 8], "exact": True})
    with tempfile.TemporaryDirectory(prefix="syntax-", dir=folder) as temp:
        fixture = Path(temp)
        for area in ("src", "tools", "examples", "tests"):
            (fixture / area).mkdir()
        for name in ("A.cs", "Z.cs"):
            write(fixture, "src/" + name, "public class Broken { public void (\n")
        for jobs in (1, 8):
            failed = subprocess.run([DOTNET, str(updated), str(fixture), str(fixture / "audit.json"), "--jobs", str(jobs)], cwd=ROOT, capture_output=True)
            reference = subprocess.run([DOTNET, str(original), str(fixture), str(fixture / "audit.json")], cwd=ROOT, capture_output=True)
            assert failed.returncode != 0 and reference.returncode != 0
            assert failed.stderr.splitlines()[0] == reference.stderr.splitlines()[0]
            assert b"Cannot audit src/A.cs" in failed.stderr and not (fixture / "audit.json").exists()
        RESULTS.append({"case": "Roslyn first malformed path and diagnostic", "jobs": [1, 8], "exact_diagnostic": True, "runtime_stack_excluded": True})
    parity("repository requirements", "requirements", ROOT, expected=0)
    parity("repository scoreboard", "scoreboard", ROOT, outputs=["build/requirements-scoreboard.html"], expected=0)
    parity("repository neutrality", "neutrality", ROOT, ["--output", ROOT / "build/source-neutrality-audit.json"], ["build/source-neutrality-audit.json"], 0)
    parity("repository preflight", "preflight", ROOT, ["--output", ROOT / "build/docs-preflight.json"], ["build/docs-preflight.json"], 0)
    parity("repository documentation gate", "docs", ROOT, outputs=["build/documentation-audit.json"], expected=0)
    parity("repository audit comparison", "compare", ROOT, ["--roslyn", ROOT / "build/api-audit.json", "--portable", ROOT / "build/docs-preflight.json", "--output", ROOT / "build/docs-parity.json"], ["build/docs-parity.json"], 0)
    (folder / "parity.json").write_text(json.dumps({"result": "passed", "cases": RESULTS}, indent=2) + "\n", encoding="utf-8")
    print(f"{len(RESULTS)} differential cases passed at jobs=1 and jobs=8", flush=True)


if __name__ == "__main__":
    main()
