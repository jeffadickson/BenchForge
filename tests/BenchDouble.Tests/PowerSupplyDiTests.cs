using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Exercises the actual .NET DI container and per-run IoC scopes.</summary>
public sealed class PowerSupplyDiTests
{
    [Fact]
    public async Task BehaviorCanBeSwappedAtCompositionRoot()
    {
        async Task<double> RunAsync(ILoadBehavior behavior)
        {
            var services = new ServiceCollection();
            services.AddScoped<ISimulationClock, ManualSimulationClock>();
            services.AddScoped<ILoadBehavior>(_ => behavior);
            services.AddScoped<MockPowerSupply>();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true, ValidateScopes = true
            });
            using var scope = provider.CreateScope();
            var supply = scope.ServiceProvider.GetRequiredService<MockPowerSupply>();
            await supply.ConfigureAsync(ElectricPotential.FromVolts(12),
                ElectricCurrent.FromAmperes(3));
            await supply.SetOutputAsync(true);
            return (await supply.MeasureAsync()).Current.Amperes;
        }

        Assert.Equal(0.5, await RunAsync(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(24))), 10);
        Assert.Equal(2.0, await RunAsync(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(6))), 10);
    }

    [Fact]
    public async Task EachExperimentScopeOwnsItsClockAndInstrumentState()
    {
        var services = new ServiceCollection();
        services.AddScoped<ManualSimulationClock>();
        services.AddScoped<ISimulationClock>(sp => sp.GetRequiredService<ManualSimulationClock>());
        services.AddScoped<ILoadBehavior>(_ =>
            new TimedOvercurrentBehavior(
                new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)),
                TimeSpan.FromSeconds(5),
                ElectricCurrent.FromAmperes(2.5)));
        services.AddScoped<MockPowerSupply>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        });
        using var firstRun = provider.CreateScope();
        using var secondRun = provider.CreateScope();
        var first = firstRun.ServiceProvider.GetRequiredService<MockPowerSupply>();
        var second = secondRun.ServiceProvider.GetRequiredService<MockPowerSupply>();
        var firstClock = firstRun.ServiceProvider.GetRequiredService<ManualSimulationClock>();
        var secondClock = secondRun.ServiceProvider.GetRequiredService<ManualSimulationClock>();
        Assert.NotSame(first, second);
        Assert.NotSame(firstClock, secondClock);

        foreach (var supply in new[] { first, second })
        {
            await supply.ConfigureAsync(ElectricPotential.FromVolts(12),
                ElectricCurrent.FromAmperes(3));
            await supply.SetOutputAsync(true);
        }
        firstClock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(2.5, (await first.MeasureAsync()).Current.Amperes, 10);
        Assert.Equal(0.5, (await second.MeasureAsync()).Current.Amperes, 10);
    }
}
