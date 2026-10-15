# AutoMock — Declarative Instrument Behavior Injection

**Status:** First working source increment committed; build and test execution remain unverified.

## Architectural decision

AutoMock is a **Strategy decorator** over a normal, typed instrument simulation behavior. The experiment is the **composition root**: it registers instrument roles and chooses normal behavior, a declarative AutoMock, or a custom C# behavior. Instrument implementations never need to parse rule files or know about the DI container.

This is a lightweight, deterministic rule evaluator, **not** a general inference engine, scripting environment, SCPI interpreter, or physical power-supply compliance model.

## Simple fluent example

```csharp
var builder = new ExperimentDefinitionBuilder();
builder.AutoMockLoad(
    _ => new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)),
    rules => rules
        .FixedCurrent("overcurrent", ElectricCurrent.FromAmperes(2.5),
            new AutoMockLoadCondition(From: TimeSpan.FromSeconds(5)),
            priority: 100)
        .ScaleBaseline("high-voltage-gain", 1.10,
            new AutoMockLoadCondition(
                MinimumVoltage: ElectricPotential.FromVolts(10),
                MaximumVoltage: ElectricPotential.FromVolts(15)),
            priority: 10));
builder.Simulated<IPowerSupply, SimulatedPowerSupply>("Supply1");
var definition = builder.Build("Overcurrent qualification");
```

At 12 V with a 24 Ω baseline, the normal current is 0.5 A. The gain rule makes it 0.55 A until the high-priority fault rule becomes active at five simulated seconds, when the mock reports 2.5 A. The existing power supply's output-enabled state still governs whether current is zero.

## JSON without recompiling

```json
{
  "version": 1,
  "rules": [
    {
      "name": "overcurrent",
      "priority": 100,
      "action": "FixedCurrent",
      "value": 2.5,
      "fromSeconds": 5
    },
    {
      "name": "gain",
      "priority": 10,
      "action": "MultiplyBaseline",
      "value": 1.1,
      "minimumVolts": 10,
      "maximumVolts": 15
    }
  ]
}
```

```csharp
builder.AutoMockLoadFromJson(
    _ => new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)),
    File.ReadAllText("automock-overcurrent.json"));
```

The parser accepts only schema version 1 and recognized actions/fields. It limits the input size and rule count, validates time windows and numeric values, and rejects unknown properties. No user-supplied C# expressions are executed. The rule set is immutable for an experiment definition.

## Semantics

- A rule may match a half-open logical-time interval `[From, Until)` and inclusive supply-voltage bounds.
- The highest priority matching rule wins. Equal-priority rules retain declaration order.
- `FixedCurrent` returns a nonnegative fixed current in amperes.
- `MultiplyBaseline` evaluates the injected baseline once and multiplies its current by a finite, nonnegative factor.
- If no rule matches, the baseline behavior is used.
- Duplicate rule names and invalid ranges are rejected before a run.
- Each experiment run has its own clock and instrument state. No rule mutates hardware or global state.
- The mock's output-off state overrides the load behavior and returns zero current.

This version does **not** model current limiting or voltage droop. A real power supply and DUT need a validated compliance model before hardware-equivalent claims.

## Rule-match provenance

`RuleBasedLoadBehavior.EvaluateAt(voltage, elapsed)` returns `AutoMockEvaluation` containing the simulated current, matched rule name (or null for baseline fallback), and logical timestamp. This is a pure evaluation result; it is **not yet persisted to SQLite or streamed to the instrument dashboard**. The regular `ILoadBehavior.CurrentAt` method returns only the typed current, keeping existing instrument APIs unchanged.

## Testing

`AutoMockLoadRulesTests` checks the five-second boundary, voltage-range scaling, priority, invalid configurations and output-off behavior. `AutoMockRuleFileTests` checks JSON parsing and rejection. `ExperimentCompositionTests` checks AutoMock injection and two-run isolation. These tests are in the normal xUnit CI projects.

Run from the repository root:

```bash
dotnet run --project examples/BenchDouble.ExperimentDemo -- --automock
dotnet run --project examples/BenchDouble.ExperimentDemo -- --automock-json
dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj
dotnet test tests/BenchDouble.Experiments.Tests/BenchDouble.Experiments.Tests.csproj
```

The JSON example file is `examples/BenchDouble.ExperimentDemo/automock-overcurrent.json`. The demo uses a two-hour fault threshold to fit the thermal cycle example.

## Existing .NET rules engines

- **Microsoft RulesEngine**: JSON workflows, dynamic expression conditions, configurable policies. Best future optional adapter when basic predicates become insufficient. https://github.com/microsoft/RulesEngine
- **NRules**: Rete-based forward chaining, rule dependencies and inference. Valuable for complex stateful event correlation, unnecessary for simple instrument measurement overrides. https://github.com/NRules/NRules
- **Dynamic Expresso**: C# expression interpreter. Useful for trusted local development, but its expressions are not a sandbox. https://github.com/dynamicexpresso/DynamicExpresso

Do not introduce any of these into the core by default. If adopted, hide them behind a pluggable `IAutoMockRuleEvaluator` boundary and run untrusted expressions or code only in an isolated worker.

## Next increments

1. Extend the same declarative model to **instrument commands and state**: DMM voltage/resistance mode, generator frequency, SMU source/sense configuration, chamber temperature profile.
2. Add time-based ramps, sine/noise with explicit seeds, delay, timeouts, disconnections, errors and reset/initialize hooks.
3. Add rule provenance and deterministic traces (matched rule, baseline value, output, virtual timestamp), retaining measurement metadata.
4. Add explicit model revision, rule-set SHA-256, package signature, experiment manifest and replay.
5. Evaluate an optional Microsoft RulesEngine adapter for complex conditions, with strict security limits and benchmarked startup/evaluation cost.
6. Add a browser rule editor and preview only after the JSON schema and command semantics stabilize.
7. Verify on Windows, macOS and Linux and demonstrate the same experiment with normal, AutoMock and custom C# behavior.

**Safety:** AutoMock is test simulation, not a safety interlock. Physical instrument control, leases and safe-state verification are separate.

## Generated mock adapters (planned)

**BD-AM-011** will use the existing instrument-definition/code-generation pipeline to create default typed mock adapters for DMM, SMU, generator, chamber and other instrument classes. The goal is to avoid writing command boilerplate twice: generated adapters preserve compile-time method/property signatures, while AutoMock rules override selected measurements, command responses and fault behavior. This generator integration has **not** yet been implemented. See [issue #40](https://github.com/jeffadickson/BenchDouble/issues/40).

## Multiple instruments in one experiment

The composition root supports independently keyed rules using `AutoMockLoadFor("SupplyA", baselineFactory, configure)` and `AutoMockLoadFor("SupplyB", ...)`. Register each instrument with the `Simulated<TInterface>(role, factory)` overload, resolving its keyed `ILoadBehavior` inside the composition factory. The instrument implementation itself receives an ordinary `ILoadBehavior` constructor argument and never references the DI container. Each named rule set has a separate SHA-256 fingerprint in `ExperimentDefinition.AutoMockRuleHashes` and `ExperimentRun.AutoMockRuleHashes`. See `TwoNamedSuppliesCanHaveDifferentAutoMockRules` in the experiment xUnit tests. Full binary/package provenance remains planned.
