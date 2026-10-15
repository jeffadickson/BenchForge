# BenchForge schema-v2 emitter: typed simulation preview

The standalone `BenchDouble.DriverGen` now has an explicit `--preview-v2` mode alongside the unchanged schema-v1 production-driver emitter. This **does not** authorize physical instrument I/O.

```sh
make generate-v2-preview
make check-v2-preview
```

Or on Windows:

```powershell
dotnet run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- --preview-v2 instrument-definitions/drafts build/v2-preview
dotnet run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- --preview-v2 instrument-definitions/drafts build/v2-preview --check
```

The v2 emitter uses bounded `TransformBlock` and `ActionBlock` stages to validate and render source in stable order. It validates all inputs before writing, rejects duplicate driver names, and atomically replaces changed files. Generated preview classes expose asynchronous mode selection and typed UnitsNet measurements for scalar capabilities with explicit mock defaults. The generated XML comments include concise descriptions, physical quantity, wire unit, measurement basis (or *unspecified*), and the source query string.

The v2 schema now permits `description`, `originalDescription`, and `unitReviewStatus` for each capability. Preserve original PyMeasure text and evidence. Editorial unit suggestions are **not** accepted as reviewed units merely because they exist in a CSV. The v2 preview is intentionally read-only and simulation-only; it does not issue SCPI, enable outputs, or silently translate unknown quantities.

## Known limitations

- This is a source-emitting **preview**, not the completed reviewed schema-v2 physical-driver and mock generator.
- Only scalar frequency, potential, current and resistance pairs are supported; unsupported quantity/unit combinations fail instead of being guessed.
- Mode transitions, measurement basis and response framing require manufacturer review.
- No waveform or multi-argument operation generation is implemented.
- Generated output is not yet integrated into the main compiled library or package. Build/CI execution must be confirmed before promoting requirements.

See issues #63 and #66. Use `make docs-check` to audit XML documentation and `make check-generated` to verify the existing schema-v1 output.
