# Check/report port benchmarks

Windows-11-10.0.26300-SP0

Fresh-process wall time including startup, parsing, checking and report writes; output hashing excluded. Sequential interleaved candidates; no concurrent benchmarks. Warm file cache; not a cold-boot test.

Local Windows measurement on this checkout; rebenchmark on other hardware/platforms. Python AST helper is included in C# documentation timings. Build/restore excluded from timings; Release DLL invocation used for C#.

9 measured fresh processes and 2 warmups per candidate. Medians in milliseconds.

| Task | Reference | C# 1 worker | C# 8 workers | Native 1 worker | Native 8 workers | Selected | Speedup |
|---|---:|---:|---:|---:|---:|---|---:|
| requirements | 323.65 | 299.64 | 304.28 | 73.43 | 72.07 | native-8 | 4.49x |
| scoreboard | 242.93 | 237.31 | 239.73 | 54.23 | 62.32 | native-1 | 4.48x |
| neutrality | 692.59 | 994.66 | 749.95 | 417.61 | 348.20 | native-8 | 1.99x |
| preflight | 1412.07 | 960.92 | 1012.80 | 721.53 | 679.47 | native-8 | 2.08x |
| docs | 671.27 | 1069.85 | 1081.70 | 840.53 | 679.64 | python | 1.00x |
| compare | 355.66 | 362.65 | 392.05 | 103.19 | 101.95 | native-8 | 3.49x |
| roslyn | 2292.51 | 2248.62 | 1347.40 | — | — | csharp-8 | 1.70x |

Every measured run matched stdout, stderr, exit status and report bytes. Raw samples, hashes, commands and runtime versions are in benchmarks.json.

## Selection and scope

Native C# AOT is selected for requirements (eight workers), scoreboard (one),
source neutrality (eight), portable preflight (eight) and audit comparison
(eight). Python remains the documentation gate: its median was 671.27 ms versus
679.64 ms for the closest C# candidate, a narrow result that should be remeasured
on other systems. Eight-worker managed C# remains the authoritative Roslyn audit.
`--jobs` bounds independent scans; scoreboard and comparison retain serial
ordered report assembly. Worker count is not a claim that every stage is parallel.

Make targets and CI use the selected commands. Native tools are cached in
`.tools/checks/native`; Make rebuilds on source changes, and CI caches by platform
and source. A first native compilation has a separate up-front cost and requires
the host native compiler/linker (MSVC on Windows, a compatible clang toolchain
on Unix). It is excluded from the cached-run numbers above. Python references
remain available where the native build toolchain is unavailable. Cross-platform
performance and CI execution have not been measured in this local Windows run.
GNU Make is unavailable on this workstation; the equivalent publishing and
check commands were exercised directly, including the Git Bash invocation used
by CI. No application suites, hardware tests or hosted CI runs were executed.

## Output checks

44 focused cases passed. The six script ports were compared against unchanged
Python oracles at one and eight workers, using both managed and native C#.
Successful Roslyn JSON and stdout were compared byte for byte against the
original Release executable. The malformed-source case compares its first
error diagnostic and failing status, excluding runtime stack traces; ordered
error selection prevents worker completion order from changing the first file.

The corpus includes CSV quoting and embedded CRLF, empty inventories, HTML and
Unicode escaping, duplicate/missing traceability and audit rows, statement
fingerprints, malformed and passing execution evidence, generated documentation
gaps, records/constructors/generic C# signatures, path-component ordering,
Python nested definitions and import-side-effect traps, source-neutrality
exemptions and binary data, report matching, and the complete checkout.
This is executable corpus evidence, not a claim of equivalence for all malformed
inputs. C# CLI usage/unexpected-input errors use exit 2 rather than reproducing
Python runtime tracebacks.

The existing nine requirements and ten documentation regression tests also
passed. No requirement was promoted to Verified; the legacy documentation
baseline was not changed.

C# owns checking, JSON/HTML reporting, comparison and ordered scan coordination.
`scripts/python_ast_inventory.py` remains a parser-only helper to preserve the
real Python grammar. It never imports or executes inspected source. The lexical
C# preflight remains advisory, and Roslyn remains authoritative. StrictDoc,
Sphinx and DocFX remain third-party dependencies and were not reimplemented.

## Reproduction

Build the candidates and pinned original with the same .NET SDK:

```
dotnet build tools/BenchForge.Checks -c Release
dotnet build tools/BenchDouble.DocumentationAudit -c Release
dotnet publish tools/BenchForge.Checks -c Release -p:PublishAot=true -p:IlcOptimizationPreference=Speed -p:ILLinkTreatWarningsAsErrors=true -o .tools/checks/native
python tests/check_ports/benchmark.py --prepare-baseline 68113cc72de785e5329a3df2bcf3087d0059c294 --prepare-only
python tests/check_ports/verify.py
```

For the benchmark's native candidate, publish to `build/check-ports/native` with
the same options, then run:

```
python tests/check_ports/benchmark.py --samples 9 --warmups 2
```

`--tasks roslyn` refreshes only that task and merges its samples into the existing
report; the final Roslyn timings above were refreshed after its ordered-error
repair. No other port source changed after its measured run. Raw measurements,
commands, source hashes and versions are in `check-port-benchmarks.json`, and
case details are in `check-port-parity.json`.

## Selected direct commands

On Windows the native executable has the `.exe` extension. Run from the repository
root; `--root` can select another checkout. The same native source can be built
for other supported hosts, but those timings still need measurement.

```
.tools/checks/native/BenchForge.Checks.exe requirements --jobs 8
.tools/checks/native/BenchForge.Checks.exe scoreboard --jobs 1
.tools/checks/native/BenchForge.Checks.exe neutrality --jobs 8 --output build/source-neutrality-audit.json
.tools/checks/native/BenchForge.Checks.exe preflight --jobs 8 --output build/docs-preflight.json
.tools/checks/native/BenchForge.Checks.exe compare --jobs 8 --roslyn build/api-audit.json --portable build/docs-preflight.json --output build/docs-parity.json
dotnet tools/BenchDouble.DocumentationAudit/bin/Release/net10.0/BenchDouble.DocumentationAudit.dll . build/api-audit.json --jobs 8
python scripts/check_api_docs.py
```
