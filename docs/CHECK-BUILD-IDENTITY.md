# Compiled check build identity

Normal Make targets use `scripts/checks.ps1`, forwarding `DOTNET`, `PYTHON`, `CHECKS_ARCH` and `CHECKS_CONFIGURATION`. Direct callers can select `-Dotnet`, `-Architecture` and `-Configuration`. The resolved .NET host path, SDK version, source contents, runtime identifier and configuration participate in cache identity. Artifact hashes are verified on cache hits.

`-PlanOnly` requires no SDK and emits the supported build plan. A foreign architecture can be prepared with `-PrepareOnly`, but cannot execute in the caller's process. These plans are contract-tested; only Windows x64 is executed in this batch.

Contenders wait for the same cache lock up to `-LockTimeoutSeconds` (default 300, maximum 3600). Only recognized sharing/lock contention is retried; unrelated I/O failures propagate. The lock remains exclusive through build and execution and is released on failure. Different runtime/configuration partitions have separate locks. This serializes simultaneous requests for the same cache rather than starting duplicate builds.

The optional standalone Roslyn output is `.tools/roslyn/<RID>/<Configuration>`. Optional native builds require an explicit `CHECKS_RID`. The port parity Make target passes the exact selected native executable and Roslyn assembly to the verifier. Native compilation is excluded from the default workflow. Optional Make publish prerequisites still use timestamps; SDK/content validation belongs to the normal managed workflow.

Standalone `docs` regenerates Roslyn before applying the documentation gate. Standalone `compare` regenerates Roslyn and portable producer reports before comparing; producer failure stops the consumer. The full workflow retains parallel independent producers and deterministic report bytes.

GNU Make is unavailable on the local machine. Final verification covers Make source contracts and the underlying build commands, not an actual Make invocation or foreign runtime execution.

The managed workflow disables its unused native apphost and puts intermediates in cache-local `obj/`, reducing Windows copy-path length. Old nested `obj`/`bin` directories are excluded from compilation when an existing cache migrates. Checkpoint 350 exercised a custom cache whose former intermediate DLL path was 266 characters, then validated both a new cold build and the existing default-cache migration.

## Explicit runtime requests

`make checks CHECKS_RID=win-x64 CHECKS_ARCH=x64` and direct `scripts/checks.ps1 -RuntimeIdentifier win-x64` use the same requested-runtime validator. An explicit RID supplies the architecture only when architecture is `auto`; conflicting values fail. Unsupported RID spellings and cross-OS requests fail before touching the cache. `checks-plan` can describe a supported same-OS foreign architecture; execution still requires a matching process and `checks-prepare` remains the explicit build-only route.

Optional native and standalone Roslyn targets have order-only runtime-plan guards, so cached target files do not bypass validation. `check-ports` checks execution eligibility and actual .NET host identity before recursive native/Roslyn builds. Its managed DLL output is also partitioned by RID/configuration. The verifier receives exact tool paths and rejects missing explicit selections rather than silently skipping native coverage. The benchmark harness and verifier both honor the selected .NET executable, including paths containing spaces.

Examples use the current host OS. Foreign-OS builds and arbitrary native AOT cross-compilation toolchains are not promised. These changes are checked with pure plans, entry-point execution and Make source contracts; GNU Make and foreign-architecture binaries remain unexecuted locally unless later evidence explicitly records otherwise.
