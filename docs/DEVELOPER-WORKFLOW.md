# Make-free developer workflow

The primary build automation is the existing compiled C# workflow host. No new
build framework or GNU Make installation is required. PowerShell only bootstraps
the host, validates the requested architecture and maintains its content cache.
Use Windows PowerShell on Windows or PowerShell 7 on Linux/macOS.

```powershell
./build.ps1                         # seven checks and focused checker regressions
./build.ps1 -Command build          # generation, then application/example/tool builds
./build.ps1 -Command test           # generation, then non-browser .NET test projects
./build.ps1 -Command generate       # catalog validation, legacy and preview generation
./build.ps1 -Command docs           # fresh Roslyn audit and documentation gate
./build.ps1 -Command plan -Task test # JSON task order; no task subprocesses
./build.ps1 -PlanOnly               # architecture plan; no SDK lookup or host build
./build.ps1 -Command checks -Jobs 8 -Configuration Release -Architecture x64
```

Use `powershell -NoProfile -File ./build.ps1 ...` if script association or execution
policy prevents direct invocation. Unix uses `pwsh -NoProfile -File ./build.ps1`.
`-Dotnet` and `-Python` select executables; paths containing spaces remain single
arguments. `-Configuration Debug` applies to the host and application commands.
The host uses the existing RID/configuration cache and rejects foreign execution.
Application projects retain their own target frameworks/platform settings; these
commands do not claim cross-compilation of the application. Browser tests remain
an explicit separate workflow requiring browser setup.

Generation is a prerequisite of build/test. Tasks stop on the first failure.
Projects are discovered in deterministic path order, excluding bin/obj. Application
builds/tests run sequentially to avoid concurrent writes to shared project outputs;
the existing independent check stages still run concurrently. `-Jobs` controls
checker parallelism, not application test project scheduling.

`plan` can build the cached host on its first invocation, then emits JSON without
starting any planned tasks. Use `-PlanOnly` for a completely build-free architecture
inspection. Existing Make build/test/generate/check targets are compatibility
wrappers around the same host; specialized packaging, demos and native benchmarks
remain available through their existing scripts or optional Make targets.

Validation: `python tests/check_ports/developer_tasks.py` tests the actual compiled
host with disposable recording subprocesses, including exact exit propagation and
no task launch in plan mode. Related checker and architecture checks are recorded
in the implementation report. No full application suite is required for this
orchestration change, and no benchmark speedup is claimed.

Local validation on Windows x64 (.NET 10.0.13): the host compiled without warnings
or errors; disposable developer-task execution tests passed; the primary entry
point passed seven compiled checks and 19 focused regressions; runtime selection
passed 27 contracts and architecture/containment passed 19 contracts. The direct
tracker unittest invocation initially hit the sandbox's external temporary-folder
restriction; the same nine tests passed through the normal host's repository-local
temporary directory. Linux/macOS execution and full application suites were not run.
`python tests/check_ports/workflow_contract.py` also passed: all six report files
matched the independent references byte for byte at jobs=1 and jobs=8; stale
documentation inputs were regenerated and failed producers prevented consumers.
GNU Make execution itself was unavailable locally; its routing was checked as a
source contract. `git diff --check` passed. Validation was performed locally before committing.

## Bounded execution and focused tests

Use `./build.ps1 -Command test -TestFilter 'FullyQualifiedName~Mock'` to select
application test cases. The filter remains one argument; it is never interpreted
by a shell or applied to generation/build commands. `-TimeoutSeconds 900` sets a
per-child deadline (default 300 seconds). Timeout stops dependent work, terminates
the child process tree and returns 124 from the compiled host. Console interruption
requests child cancellation; its handler also prevents starting another task.
The bootstrap treats any nonzero host exit as failure.

Real execution writes `build/developer-workflow.json`, containing the command,
executables, argument arrays, stage durations and exit codes, including launch
failures. Plan mode leaves this report untouched. Project discovery prunes bin/obj
folders case-insensitively and skips symbolic links; absent optional trees are
allowed. Browser projects stay outside ordinary test runs regardless of filename
casing. Application projects remain sequential; independent check stages still
run concurrently.
