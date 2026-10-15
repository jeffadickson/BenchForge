# API documentation policy

Requirements: BD-DOC-003..005. Work item: [#61](https://github.com/jeffadickson/BenchForge/issues/61).

Run `make setup`, then `make docs`. Open `build/documentation/index.html`
through a local static HTTP server (for example `python3 -m http.server 8000
--directory build/documentation`). The generated site combines DocFX C# API
pages and Sphinx Python tooling pages. `dotnet tool restore` installs the pinned
DocFX version; `tools/docs-requirements.txt` installs Sphinx in a separate `.tools/docs-venv`
environment because its Docutils version differs from StrictDoc.
On Windows run the corresponding commands from Makefile using the provisioned
`.tools/docs-venv/Scripts/python.exe` and local .NET executable.

All C# projects produce XML documentation alongside their assemblies, including
generated drivers. DocFX discovers source, tool and example projects. Sphinx
imports only repository-owned tooling; the Instrument Definer continues to parse
upstream instrument sources without importing them. New Python modules must be
added to the Python reference index in the same change.

## Comment requirements

- Public C# types/members: useful XML summary; parameters, units, ranges and
  return meaning; relevant errors; ownership, disposal and concurrency behavior.
- Async calls: distinguish transport completion, accepted command and physical
  execution. Explain cancellation and ambiguous failures. Never imply safe retries.
- Python: module/class/function docstrings describing purpose, arguments, results,
  side effects and raised errors where relevant. Include a runnable usage example
  for user-facing features. Private helpers need useful purpose descriptions too.
- Generated code: the generator emits the comments. No hand edits to `.g.cs`.
  Definitions provide units/ranges; unknown physical behavior must stay unknown.
  New generators and generated languages must join this pipeline and audit.
- Reviewers verify comment accuracy. Passing structural checks does not establish
  semantic quality, hardware safety, or completed requirement acceptance.

## Audit and migration

`make docs-check` runs a Roslyn C#12 syntax audit over src/tools/examples/tests,
then a Python AST audit over those directories plus scripts. It records public
and protected declarations, implicit interface members, enum values, parameters,
return tags and malformed XML; Python coverage includes nested definitions.
It writes `build/documentation-audit.json` with file, line, symbol and each gap.
It does not resolve inherited documentation, external references or conditional
compilation beyond the parser's default symbols; compiler/DocFX checks remain
necessary. Test helpers are inventoried even though not published as product APIs.

`docs/api-documentation-baseline.json` is the explicit legacy migration backlog,
not a waiver declaring those APIs documented. New gaps fail the gate. Generated
API gaps always fail, including any mistakenly put in the baseline. Remove
resolved entries as comments are reviewed. Adding baseline entries requires an
explicit requirement/issue review; never refresh it automatically to make a build
green. The compiler keeps CS1591 visible as a warning during this migration.

The first audit found substantial missing handwritten descriptions and parameter
contracts. Full documentation of every existing API remains open in #61; rendered
signatures alone do not satisfy that requirement. This increment establishes the
site, generator comments and auditable migration boundary.

## Sources

- [DocFX metadata](https://dotnet.github.io/docfx/reference/docfx-cli-reference/docfx-metadata.html)
- [DocFX configuration](https://dotnet.github.io/docfx/reference/docfx-json-reference.html)
- [Sphinx autodoc](https://www.sphinx-doc.org/en/master/usage/extensions/autodoc.html)

See the [dated execution audit](API-DOCUMENTATION-AUDIT-2026-10-15.md) for exact coverage counts and local build limitations.


## Example integration

The [examples guide](EXAMPLES.md) links runnable source to generated API documentation. All new examples must explain input, output, cancellation, error behavior and whether hardware is involved.
