# Version Control for Laboratory Automation — Git, Packages and Experiment Revisions

**Audience:** Instrument-control engineers and test developers familiar with writing procedures but new to Git or modern .NET package management.

## 1. Why version control matters in a test lab

A laboratory test result is only meaningful if you can identify the exact **procedure**, **instrument driver**, **mock behavior**, **test limits**, **calibration/configuration**, and **software dependencies** used to produce it. Merely knowing the executable filename is insufficient.

Three different kinds of versioning matter:
1. **Source version:** Git commits, branches, tags and pull requests.
2. **Binary/API version:** NuGet package versions, assembly compatibility and protocol versions.
3. **Experiment version:** an immutable definition plus configuration, driver IDs, behavior assembly hashes, simulation seed and run ID.

They must be related but are not interchangeable. A Git commit identifies source, not the calibration of a physical instrument or an operator's approval.

## 2. Git from the beginning

Clone once:
```bash
git clone https://github.com/jeffadickson/BenchDouble.git
cd BenchDouble
git status
```

Create a focused branch:
```bash
git switch -c feature/thermal-sensor-alarm
```

Change the C# behavior, requirements and tests together. Then review:
```bash
git diff
git status
python3 scripts/check_requirements.py
dotnet test tests/BenchDouble.Experiments.Tests/BenchDouble.Experiments.Tests.csproj
```

Commit a coherent change:
```bash
git add src tests docs requirements
git commit -m "Add sensor-dependent alarm behavior with acceptance tests"
git push -u origin feature/thermal-sensor-alarm
```

Open a pull request on GitHub. Request review, inspect CI results and merge only when required checks pass. A merge conflict is a competing edit to the same part of a file; resolve it carefully and rerun tests rather than discarding either change.

To inspect history:
```bash
git log --oneline --decorate -10
git show <commit-sha>
```

A release tag marks a reviewed baseline:
```bash
git tag -a v0.1.0 -m "BenchDouble prototype baseline"
git push origin v0.1.0
```

These tag commands are **illustrations**; do not create a real release until acceptance gates are met.

## 3. Semantic versioning and compatibility

A conventional package version has MAJOR.MINOR.PATCH:
- MAJOR: incompatible public API changes.
- MINOR: backward-compatible new capabilities.
- PATCH: backward-compatible corrections.

Before version 1.0.0, document your compatibility policy explicitly; SemVer alone cannot ensure two remote instrument hosts support identical capabilities.

Do not serialize arbitrary CLR objects across host boundaries. Use versioned DTO/protobuf contracts, stable operation IDs, optional fields and capability negotiation. A missing optional field should not crash the receiver; an incompatible required capability must be rejected with an explicit error.

## 4. A concrete mock behavior change

Suppose version A of `TemperatureDependentLoad` increases current 10% at 70°C. Version B adds hysteresis. Those behaviors can produce different test results from the same procedure.

The experiment manifest should record:
```json
{
  "experiment": "ThermalQualification",
  "definitionRevision": "rev-12",
  "sourceCommit": "example-commit-sha",
  "runId": "example-run-id",
  "simulationSeed": 42,
  "behaviors": [
    {
      "role": "Supply1",
      "type": "TemperatureDependentLoad",
      "packageVersion": "0.2.0",
      "sha256": "example-artifact-digest"
    }
  ]
}
```

The values above are placeholders, not actual committed artifact hashes. Never infer a behavior's identity from its C# class name alone.

## 5. Roslyn plugin workflow

A future approved mock package workflow should:
1. Compile ordinary C# with Roslyn and generate a PDB for debugging.
2. Run deterministic xUnit/Reqnroll tests in an isolated worker.
3. Record dependencies, compiler/runtime version, source commit and SHA-256.
4. Sign/approve the immutable behavior artifact.
5. Load only in an isolated, unprivileged simulation process.
6. Pin that exact artifact to the experiment run; never silently replace it mid-run.

`AssemblyLoadContext` is a loading mechanism, **not** a security sandbox.

## 6. What should be committed?

Commit C# source, test fixtures, requirements, design documents, examples, dependency lockfiles and reproducible build configuration. Avoid committing `bin/`, `obj/`, downloaded toolchains, secrets, personal contact lists, large captured waveform data or local SQLite run databases. Use artifact storage for signed releases and large data.

## 7. Traceability and review

Every requirement should have a stable ID in `requirements/benchdouble.sdoc` and a traceability row linking source, tests and GitHub issue. Reqnroll feature tags such as `@BD-UOM-001` make acceptance intent visible.

A passing unit test validates behavior under its fixture. A production release still needs integration tests, platform evidence, physical-instrument acceptance and independent safety review.

## 8. Common pitfalls

- **Detached HEAD:** you checked out a commit rather than a branch. Create a branch before committing new work.
- **Merge conflict:** resolve both intended changes and rerun verification.
- **Stale NuGet dependency:** restore against committed package lockfiles once implemented.
- **Same class name, different DLL:** use package hashes and version manifests.
- **Simulation replay differs:** verify virtual clock, seed, state reset, dependency versions and run inputs.
- **Physical hardware differs:** record calibration, firmware, serial identity, wiring and environment; Git cannot guarantee physical equivalence.

## 9. Recommended release checklist

Review Git commit and tag, requirements coverage, CI evidence, dependency lockfiles/SBOM, signed behavior artifacts, host protocol compatibility, user manual, known limitations, and instrument safety constraints before release.

[Software Development Plan](SOFTWARE-DEVELOPMENT-PLAN.md) · [Requirements](../requirements/README.md) · [Experiment Architecture](EXPERIMENT-COMPOSITION.md)
