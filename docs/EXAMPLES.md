# BenchForge — runnable examples

**Status:** documentation for existing prototype demos. Commands below are intended to run after dependencies are installed; passing results are not claimed here. All examples are hardware-free unless explicitly noted. Run from the repository root.

## 1. Prepare the environment

On macOS/Linux:

```sh
bash scripts/bootstrap.sh
make check-generated
make test
```

On Windows (PowerShell 7):

```powershell
pwsh -File scripts/bootstrap.ps1
dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
```

**Migration note:** The GitHub repository and public-facing documentation are named BenchForge, but some C# project paths, namespaces, assembly names and command targets remain `BenchDouble` pending the tracked identity migration [#64](https://github.com/jeffadickson/BenchForge/issues/64). Do not rename those paths in commands until the corresponding project migration lands.

## 2. Stateful generated function-generator mock

```sh
make demo-generated-mock
make check-generated
```

**What to observe:** the checked-in Keysight 33522B generated driver and mock share channel-specific SCPI templates and UnitsNet quantities. Inspect [driver source](../src/BenchDouble/Generated/Keysight33522B.g.cs) and [mock source](../src/BenchDouble/Generated/Keysight33522BMock.g.cs). The demonstration is not proof of complete hardware fidelity. Instrument limits and commands must be reviewed against the manufacturer's manual.

**Failure case to test:** set an out-of-range frequency or use an unsupported channel and confirm validation rejects the operation. Never interpret a mock acceptance as permission to apply a voltage to physical equipment.

## 3. Unit-aware meter and measurement modes

```sh
make demo-typed-meter
dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
```

**What to observe:** typed voltage and resistance quantities, with mode checks that reject mismatched measurement types. Read [the typed mock tutorial](GENERIC-UNIT-AWARE-MOCKS.md). Source tests exist; this guide does not claim the full suite passed.

## 4. Inject a deterministic overcurrent fault

```sh
make demo-normal
make demo
```

The normal demo uses a modeled resistive load; the fault scenario injects a simulated 2.5 A current after five seconds of virtual time, and the example's 2 A monitor should disable its simulated output. See [the dependency-injection tutorial](DEPENDENCY-INJECTION-AND-MOCK-BEHAVIORS.md). The shutdown is software simulation, not a physical interlock.

## 5. Thermal chamber, DUT and sensor

```sh
make demo-thermal
```

Open [Thermal Cycle Monitor](../examples/ThermalCycleMonitor/index.html) in a browser. The demo models a 30°C → 70°C → −30°C → 30°C program, a temperature-dependent supply load, and a lagging sensor. Virtual time can be accelerated or stepped. This is an idealized test model, not a calibrated chamber controller.

## 6. Binary waveform and screenshot capture

```sh
make demo-capture
```

Read [local capture storage](LOCAL-CAPTURE-STORAGE.md). The SQLite artifact prototype stores immutable capture bytes with acquisition metadata and integrity checks. Inspect the demo's recorded and retrieved waveform/screenshot. Remote replication, power-loss durability qualification and retention controls remain open in [issue #62](https://github.com/jeffadickson/BenchForge/issues/62).

## 7. Local instrument host dashboard

```sh
make demo-dashboard
```

Open http://127.0.0.1:5278/dashboard/ on the same machine. The dashboard presents local simulated instrument state, reservation status and sanitized activity. **Do not expose this prototype host on an untrusted network:** production authentication, TLS, lease fencing and physical safe-state behavior are not yet established. See [dashboard guide](INSTRUMENT-DASHBOARD.md).

## 8. Define and review a new instrument

Review [Instrument Definer](INSTRUMENT-DEFINER.md) and [the schema](../instrument-definitions/schema/instrument-definition-v2.schema.json). Extract source metadata or import the CSV templates under [tools/InstrumentDefiner/templates](../tools/InstrumentDefiner/templates/), then run:

```sh
make definer-check
```

The current schema-v2 pipeline supports review drafts and mode-aware mock previews. It does **not** yet automatically generate a production asynchronous driver from every schema-v2 definition. This is tracked in [issue #63](https://github.com/jeffadickson/BenchForge/issues/63).

## 9. Build API documentation and inspect requirements

```sh
make docs
make scoreboard
```

Open `build/documentation/index.html` with a local static server, for example `python3 -m http.server 8000 --directory build/documentation`. Read [API documentation policy](API-DOCUMENTATION.md). The requirements dashboard distinguishes implemented source, tests present, and verified evidence.

## Example acceptance checklist

For every new example: state prerequisites, exact invocation, expected observations, a negative/fault test, unit and time semantics, whether physical hardware is involved, relevant requirement IDs, and the actual build/test evidence. Examples must use the same production interfaces as the library and must not silently bypass host authorization or hardware safety checks.
