using BenchDouble.Experiments;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>Local behavior and composition regressions.</summary>
public sealed class LocalHardeningTests
{
    /// <summary>Public metadata cannot mutate frozen role registrations or later run preflight.</summary>
    /// <returns>Completion of immutable metadata checks.</returns>
    [Fact]
    public async Task DefinitionRolesCannotBeMutated()
    {
        var definition = new ExperimentDefinitionBuilder().Simulated<object>("meter", _ => new object()).Build("immutable");
        var roles = Assert.IsAssignableFrom<IList<ExperimentInstrument>>(definition.Instruments);
        Assert.Throws<NotSupportedException>(() => roles[0] = new("other", typeof(object), "other", "Simulated"));
        await using var run = await definition.StartSimulatedAsync();
        Assert.NotNull(run.Instrument<object>("meter"));
        Assert.Throws<NotSupportedException>(() => ((IList<ExperimentInstrument>)run.RegisteredInstruments).Clear());
    }

    /// <summary>Null factories fail at the composition boundary instead of creating an unusable role.</summary>
    /// <returns>Completion after eager startup refuses the invalid instrument.</returns>
    [Fact]
    public async Task NullInstrumentFactoriesFailClearly()
    {
        var definition = new ExperimentDefinitionBuilder().Simulated<object>("meter", _ => null!).Build("invalid");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await definition.StartSimulatedAsync());
        Assert.Contains("meter", error.Message);
        Assert.Contains("returned null", error.Message);
    }

// CASES
}
