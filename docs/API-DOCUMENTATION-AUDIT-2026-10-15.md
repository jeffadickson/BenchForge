# API documentation audit — 2026-10-15

Work items: #61 documentation, #62 binary artifacts. Baseline before this increment:
`5d960e02fe43fcc7fa02f3b7b9a4e178b2285618`.

## Source coverage

The Roslyn syntax pass inspected all 93 owned C# source files under src, tools,
examples and tests (excluding bin/obj), producing 842 visible declaration records.
The combined Python AST pass produced a total of 903 declaration/module records.
There are 1,538 outstanding structural gaps across 716 records: 465 in src, 236
in tests, 12 in examples, two in tools and one in scripts. A record can have several
gaps (summary, parameters, return description). The baseline stores every gap by
file and symbol; the reproducible report adds source line numbers. Top-level C#
programs can have no public declarations and therefore no declaration row.

Both checked-in generated driver/mock files have zero documentation gaps. The
base instrument contracts were documented, including scope waveform metadata
limitations. Python Definer entry points and validation tools gained docstrings.
Remaining handwritten APIs are explicitly incomplete; this audit is not a claim
that all code has useful documentation. Existing inherited docs and overloads
still require semantic review. See the policy for parser limitations.

## Local execution

- Roslyn C#12 compilation of the changed DriverGen and new DocumentationAudit
  succeeded using the pinned SDK compiler directly. The SDK's bundled Roslyn
  assemblies were used for this local audit; normal NuGet project restore/build
  of the audit tool remains to be exercised on a supported host.
- DriverGen generated the Keysight33522B driver/mock pair, and `--check` passed. An empty definition directory was also rejected instead of reporting a vacuous successful check.
- Core library compiled with XML output. No XML-comment syntax errors were
  reported; legacy CS1591 warnings remain visible.
- Documentation gap gate passed, with zero new gaps and zero generated gaps.
  Three Python gate regression tests passed, covering new gaps, generated
  exemptions and non-executing AST inspection.
- Nine requirements gate tests and 12 Instrument Definer tests passed.
- StrictDoc 0.30.2 parsed/exported all 220 requirements after correcting an extra
  separator blank line in the appended baseline. Static traceability also passed.
- A real dependency conflict was found: Sphinx 8 requires Docutils below 0.22,
  while StrictDoc 0.30.2 requires at least 0.22.2. Setup now provisions separate
  Python environments with explicit compatible Docutils pins.
- Sphinx 8.1.3 built the Python reference with warnings treated as errors.
- DocFX 2.78.3 rendered the compiled core DLL plus its XML, authored guides and
  Python reference: zero errors, 38 warnings (primarily existing guide links to
  repository files outside the site). This checks core API rendering/site layout,
  not all project metadata. The local assembly-check configuration is a temporary
  validation aid, not the committed build entry point.
- Standard `dotnet tool install` and DocFX project loading fail in this environment
  in .NET process metadata retrieval / MSBuild BuildHost initialization. Therefore
  `make docs` is not reported as passing end to end. Windows/macOS provisioning
  and the newly added CI steps have not been executed. Routine push skips CI.

## Remaining work

Resolve the explicit legacy comment backlog through source review; repair authored
guide links for publication; run the normal project-based site build and dependency
setup on supported hosts; review rendered API pages and examples. Binary capture
requirements and architecture are design-only: no new artifact backend, crash
recovery implementation or storage benchmark is claimed. All new requirements
remain below Verified status.
