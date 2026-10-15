using BenchDouble.Simulation;
using BenchDouble.DiDemo;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;

// Usage:
//   dotnet run --project examples/BenchDouble.DiDemo
//   dotnet run --project examples/BenchDouble.DiDemo -- --normal
//
// Composition root: only this file decides which behavior implementation to use.
// Neither the instrument nor the test procedure knows which behavior was selected.
bool injectFault = !args.Contains("--normal", StringComparer.OrdinalIgnoreCase);
var services = new ServiceCollection();
services.AddScoped<ManualSimulationClock>();
services.AddScoped<ISimulationClock>(sp => sp.GetRequiredService<ManualSimulationClock>());
services.AddScoped<ILoadBehavior>(sp =>
{
    ILoadBehavior baseline = new ResistiveLoadBehavior(ElectricResistance.FromOhms(24));
    return injectFault
        ? new TimedOvercurrentBehavior(baseline, TimeSpan.FromSeconds(5),
            ElectricCurrent.FromAmperes(2.5))
        : baseline;
});
services.AddScoped<MockPowerSupply>();
services.AddScoped<IOperatorNotificationSink, SimulatedOperatorNotificationSink>();

using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var run = provider.CreateScope();
var supply = run.ServiceProvider.GetRequiredService<MockPowerSupply>();
var clock = run.ServiceProvider.GetRequiredService<ManualSimulationClock>();
var notifications = run.ServiceProvider.GetRequiredService<IOperatorNotificationSink>();

await supply.ConfigureAsync(ElectricPotential.FromVolts(12),
    ElectricCurrent.FromAmperes(3));
await supply.SetOutputAsync(true);
Console.WriteLine(injectFault
    ? "DI scenario: 12 V, 24-ohm load; fault raises current to 2.5 A at virtual t=5 s."
    : "DI scenario: 12 V, 24-ohm load; no fault.");
Console.WriteLine("Trip threshold: 2 A; same runner code for both scenarios.");
Console.WriteLine("Time (s) | Voltage (V) | Current (A) | Output");

try
{
    for (int i = 0; i <= 28; i++)
    {
        PowerSupplySample sample = await supply.MeasureAsync();
        Console.WriteLine($"{sample.SimulatedElapsed.TotalSeconds,8:F2} | {sample.Voltage.Volts,11:F2} | {sample.Current.Amperes,11:F2} | {(sample.Voltage.Volts > 0 ? "ON" : "OFF")}");
        if (sample.Current.Amperes > 2)
        {
            Console.WriteLine($"OVERCURRENT detected at virtual t={clock.Elapsed.TotalSeconds:F2}s; requesting output off.");
            await notifications.NotifyAsync("DI-DEMO-001", $"Current {sample.Current.Amperes:F2} A exceeded 2 A threshold at virtual t={clock.Elapsed.TotalSeconds:F2}s.");
            await supply.SetOutputAsync(false);
            break;
        }
        clock.AdvanceBy(TimeSpan.FromMilliseconds(250));
    }
}
finally
{
    // This is only simulated cleanup. Real hardware must confirm safe state.
    await supply.SetOutputAsync(false);
}
Console.WriteLine("Demo complete. No physical instrument was accessed.");
