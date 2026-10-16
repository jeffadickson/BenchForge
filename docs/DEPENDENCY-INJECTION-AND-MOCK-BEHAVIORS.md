# Dependency Injection and Programmable Instrument Mocks

**Status (2026-10-15):** Initial implementation and runnable console demonstration committed. Build/test execution has not been verified in this environment.

## 1. Why DI and inversion of control?

**Inversion of Control (IoC)** is the design principle: instrument objects should not decide which physical backend, remote proxy, virtual clock, or simulation behavior they receive. **Dependency Injection (DI)** is the concrete technique: supply dependencies through constructors and configure them at a composition root.

The modern .NET standard DI container is `Microsoft.Extensions.DependencyInjection`. It is used in the demo application; the core instrument library has **no DI-container dependency**. This preserves easy construction in xUnit, NSubstitute, and embedded applications.

### Ownership and scope
- Host resource registry: singleton within the **host** (not globally shared across computers).
- Experiment simulation clock: scoped **per run**.
- Stateful mock instrument: scoped **per run**.
- Mock behavior model: scoped per run unless proven immutable/stateless.
- Lease/session: explicit acquisition/disposal; DI is **not** the reservation manager.
- External real/remote instrument: registered via factory/proxy, but still requires host-authorized leases.
- Application service provider: created at process startup; scope disposed at run completion.

## 2. Implemented extension point

```csharp
public interface ILoadBehavior
{
    ElectricCurrent CurrentAt(
        ElectricPotential voltage,
        TimeSpan elapsed);
}
```

`MockPowerSupply` receives `ILoadBehavior` and `ISimulationClock` in its constructor. It does not know whether the behavior is a resistor, a diode, a programmed IV curve or a timed fault. The behavior receives only the simulated applied voltage and virtual elapsed time, not VISA/GPIB handles.

Current built-in implementations:
- `ResistiveLoadBehavior`: Ohm's law.
- `TimedOvercurrentBehavior`: decorator that uses a baseline behavior before a virtual-time threshold, then a specified current.
- `ManualSimulationClock`: deterministic, explicit time advancement.
- `MockPowerSupply`: configure voltage/current limit, enable/disable output, measure voltage/current and record whether the requested current exceeds the configured limit.

The mock is intentionally an **idealized measurement model**. It reports `CurrentLimitExceeded` but does not pretend to model current regulation, voltage droop, real compliance behavior or an electrical safety interlock.

## 3. Runnable demo

```bash
dotnet run --project examples/BenchDouble.DiDemo
dotnet run --project examples/BenchDouble.DiDemo -- --normal
```

Default behavior: configure 12 V, a 24 Ω resistive load, 3 A configured limit, and a virtual fault at 5 seconds. The baseline current is 0.5 A. At 5 simulated seconds the injected behavior returns 2.5 A. The unchanged controller loop trips its 2 A threshold and disables the simulated output. The `--normal` variant injects only the resistor behavior and never trips. Both run **without real waiting**, because the test advances the virtual clock in 250 ms steps.

The demo's **composition root** is the only place that chooses the behavior:

```csharp
var services = new ServiceCollection();
services.AddScoped<ManualSimulationClock>();
services.AddScoped<ISimulationClock>(sp =>
    sp.GetRequiredService<ManualSimulationClock>());

services.AddScoped<ILoadBehavior>(sp =>
{
    ILoadBehavior baseline = new ResistiveLoadBehavior(
        ElectricResistance.FromOhms(24));

    return injectFault
        ? new TimedOvercurrentBehavior(
            baseline,
            TimeSpan.FromSeconds(5),
            ElectricCurrent.FromAmperes(2.5))
        : baseline;
});
services.AddScoped<MockPowerSupply>();

using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions
    {
        ValidateScopes = true,
        ValidateOnBuild = true
    });
using var run = provider.CreateScope();
var supply = run.ServiceProvider.GetRequiredService<MockPowerSupply>();
```

This uses the standard DI container to compose the mock, behavior and run clock. The procedure itself does not use the service locator; the composition root resolves its dependencies once and supplies them to the runner.

## 4. Adding a custom IV-curve behavior

No generator or Roslyn compilation is necessary for a normally compiled C# plugin:

```csharp
public sealed class DiodeLoad : ILoadBehavior
{
    public ElectricCurrent CurrentAt(
        ElectricPotential voltage, TimeSpan elapsed)
    {
        // Simplified example: NOT a validated semiconductor model.
        double amps = 1e-9 *
            (Math.Exp(Math.Min(voltage.Volts / 0.05, 20)) - 1);
        return ElectricCurrent.FromAmperes(amps);
    }
}
```

Register `services.AddScoped<ILoadBehavior, DiodeLoad>();` instead of the baseline. The mock power supply and test procedure remain unchanged. In a real application, use a named behavior registry or keyed DI registrations to choose among multiple simultaneously connected devices; do not register a single `ILoadBehavior` globally for every instrument.

## 5. Roslyn and remote behavior packages

The same `ILoadBehavior` interface can be implemented in a separately compiled DLL. Roslyn may compile user-authored C# in an **isolated development/test worker** and emit a DLL + PDB. Before deploying to a remote simulation host:

1. Compile with an approved set of references and report diagnostics.
2. Run unit tests with a deterministic simulation clock and predefined scenarios.
3. Perform integration tests through the same instrument API and fault-injection expectations.
4. Package immutable assembly/dependency manifest with version, SHA-256 digest and publisher signature.
5. Host policy verifies identity, compatibility and approval before loading into a **restricted out-of-process simulation worker**.
6. A run pins one behavior package/hash and clock seed for reproducibility; no implicit hot-swap during active execution.

**Security:** `AssemblyLoadContext` and Roslyn are **not** security sandboxes. A behavior DLL is arbitrary .NET code; never load untrusted client-supplied assemblies in the privileged instrument host process. This first increment does not implement runtime compilation, package uploading, signatures, sandboxing or remote behavior deployment.

## 6. DI and remote/physical/mock backend selection

The factory should select two independent dimensions: access = local/remote and backend = physical/mock. A remote host can itself run physical or simulated equipment. The host's lease/fencing and authorization are separate from DI registration.

Use the same typed interface for all four combinations. For a simulated power supply, inject `ILoadBehavior`; for a physical supply, inject a driver provider/transport; for a remote supply, inject an authenticated typed proxy. The procedure never copies vendor-specific SCPI strings.

## 7. Test strategy and next increments

Committed unit tests cover a fault at virtual t=5s, changing behavior without changing the mock class, current-limit metadata and negative clock advancement. Next add:
- DI scope isolation across two simultaneous experiment runs and named instruments.
- A pure IV-curve model and seeded-noise decorator with explicit state/reset semantics.
- A test verifying the controller's **trip latency**, not just current change.
- Real-time/accelerated clock adapters with cancellable delay.
- Behavior lifecycle `InitializeAsync` / `ResetAsync` for stateful overrides.
- A behavior compilation/test/approval workflow with DLL/PDB and source mapping.
- Versioned remote host loading and structured event logs.
- Hardware-accurate supply compliance behavior only with validated device data.

## 8. References

- [Microsoft DI guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines)
- [Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/)
- [Simulation clock specification](MOCK-TIME-AND-LIFECYCLE.md)
- [Distributed host architecture](DISTRIBUTED-INSTRUMENT-HOST.md)
- [Unified typed operations](INSTRUMENT-OPERATIONS-25-external instrument source-EXAMPLES.md)
