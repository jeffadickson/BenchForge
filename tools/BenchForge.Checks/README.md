# Check and report ports

Release-build `BenchForge.Checks.csproj`, then invoke its DLL with one of:

```
dotnet tools/BenchForge.Checks/bin/Release/net10.0/BenchForge.Checks.dll requirements
dotnet tools/BenchForge.Checks/bin/Release/net10.0/BenchForge.Checks.dll scoreboard
dotnet tools/BenchForge.Checks/bin/Release/net10.0/BenchForge.Checks.dll neutrality --output build/source-neutrality-audit.json
dotnet tools/BenchForge.Checks/bin/Release/net10.0/BenchForge.Checks.dll preflight --output build/docs-preflight.json
dotnet tools/BenchForge.Checks/bin/Release/net10.0/BenchForge.Checks.dll docs
dotnet tools/BenchForge.Checks/bin/Release/net10.0/BenchForge.Checks.dll compare --roslyn build/api-audit.json --portable build/docs-preflight.json --output build/docs-parity.json
```

`--root` selects the checkout, `--jobs` bounds independent file work (1–256;
default at most eight), and `--python` selects the Python grammar interpreter.
The documentation gate consumes the existing Roslyn `build/api-audit.json`.
`preflight` and `neutrality` accept the original `--strict` failure switch.
Source neutrality still audits **Git-tracked** files, including missing tracked
paths; it does not silently extend the original policy to untracked files.

C# implements requirement validation, lexical C# scanning, HTML generation,
documentation gating, source-neutrality scanning and audit comparison. The small
`scripts/python_ast_inventory.py` helper supplies CPython parser metadata in one
batch. Inspected files are never imported or executed. This preserves the real
Python grammar and the original tools' distinct nested-definition traversal
rules. It is not a Python-free documentation pipeline.

Parallel scans retain source indices and merge results in reference order.
Output retains the Python JSON indentation, ASCII escaping, object ordering,
floating-point ratios, platform text newlines and HTML escaping. The portable
scanner remains advisory; Roslyn remains authoritative. Neither implementation
promotes requirements to Verified.

The existing Python scripts remain unchanged as independent oracles, portable
fallbacks and benchmark candidates. Language choice alone does not select the
default: the measured fastest equivalent implementation wins each command.
See `docs/CHECK-PORT-BENCHMARKS.md` for measured selections and commands.

## Native executable and selected defaults

`dotnet publish tools/BenchForge.Checks -c Release -p:PublishAot=true -p:IlcOptimizationPreference=Speed -p:ILLinkTreatWarningsAsErrors=true -o .tools/checks/native`

uses the host runtime and requires its native compiler/linker. This removes the
managed startup cost from repeat runs. The selected Make/CI commands use the
cached native executable for five ports and keep Python for the documentation
gate because it measured faster. The original scripts remain usable directly.
`make checks-native` rebuilds the native image after source changes. On Windows,
use the direct publish command and `.tools/checks/native/BenchForge.Checks.exe`.

## Focused verification and timing

```
python tests/check_ports/verify.py
python tests/check_ports/benchmark.py --samples 9 --warmups 2
```

Build the original Roslyn source with `python tests/check_ports/benchmark.py
--prepare-baseline 68113cc72de785e5329a3df2bcf3087d0059c294 --prepare-only`.
Publish a native benchmark candidate to `build/check-ports/native` with the same
AOT options. Use the same SDK and Release settings for both Roslyn builds. `verify.py` compares the reference scripts against C# at
one and eight workers on requirement fault cases, HTML escaping, lexical and
AST edge cases, source-neutrality fixtures and the checkout. It compares raw
stdout/stderr, exit status and report bytes. This is a tested corpus, not proof
of parser equivalence for arbitrary malformed input or identical runtime stack
traces across languages. Unexpected input errors are reported by C# with exit 2.

The benchmark measures fresh-process wall time, including startup, Python AST
helper cost and file writes. Compilation, restore and digest comparison are
outside the interval. Candidates run sequentially in seeded interleaved order,
with warmups, medians and all raw samples retained. Rebenchmark on other systems
or substantially larger inventories. The application test suites are not needed
for these ports.
