# Generated BenchForge code — disposable output

This directory contains generated-only projects. **Never edit generated .g.cs files by hand.**

- `BenchForge.Instruments/Code/` contains schema-v2 simulation-preview classes.
- `BenchForge.Instruments/BenchForge.Instruments.csproj` is **hand-maintained build configuration** and must not be deleted.
- Canonical imported data lives in `instrument-definitions/`, outside this directory.
- Generator source lives in `tools/BenchDouble.DriverGen/`.

From the repository root:

```sh
make generate-v2-preview
make check-v2-preview
make build-generated
make clean-generated
make generate-v2-preview
```

`clean-generated` deletes only the known generated code directory after a path safety check. It does not delete project files, source definitions, handwritten code, or tests.

**Maturity:** v2 output is simulation-only; unreviewed PyMeasure data must never produce physical-control drivers. The v1 generator currently writes into `src/BenchDouble/Generated` for backward compatibility. That legacy output is not part of this cleanup target until a separately verified migration is complete.
