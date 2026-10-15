using BenchDouble.Experiments;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;
using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>Proves named DI roles, run isolation, injected behavior and disposal.</summary>
public sealed class ExperimentCompositionTests
{
    public interface IProbe { Temperature Read(); }
    public sealed class Probe(ISimulationClock clock) : IProbe
    {
        public Temperature Read() => Temperature.FromDegreesCelsius(30 + clock.Elapsed.TotalHours);
    }

    public interface ILoad { ElectricCurrent Read(); }
    public sealed class Load([FromKeyedServices("Chamber1")] IProbe probe) : ILoad
    {
        public ElectricCurrent Read() => ElectricCurrent.FromAmperes(
            0.5 * (1 + (probe.Read().DegreesCelsius - 30) / 10));
    }

    private static ExperimentDefinition Definition() => new ExperimentDefinitionBuilder()
        .Simulated<IProbe, Probe>("Chamber1")
        .Simulated<ILoad, Load>("Supply1")
        .Build("Thermal");

    [Fact]
    public async Task InjectedInstrumentReadsOtherNamedInstrument()
    {
        await using var run = Definition().StartSimulated();
        Assert.Equal(0.5, run.Instrument<ILoad>("Supply1").Read().Amperes, 10);
        run.Clock.AdvanceBy(TimeSpan.FromHours(1));
        Assert.Equal(0.55, run.Instrument<ILoad>("Supply1").Read().Amperes, 10);
    }

    [Fact]
    public async Task TwoRunsHaveIndependentClockState()
    {
        var definition = Definition();
        await using var first = definition.StartSimulated();
        await using var second = definition.StartSimulated();
        first.Clock.AdvanceBy(TimeSpan.FromHours(1));
        Assert.Equal(0.55, first.Instrument<ILoad>("Supply1").Read().Amperes, 10);
        Assert.Equal(0.5, second.Instrument<ILoad>("Supply1").Read().Amperes, 10);
        Assert.NotEqual(first.RunId, second.RunId);
    }

    [Fact]
    public void RejectsDuplicateRolesAndMutationAfterBuild()
    {
        var builder = new ExperimentDefinitionBuilder();
        builder.Simulated<IProbe, Probe>("Chamber1");
        Assert.Throws<InvalidOperationException>(() =>
            builder.Simulated<IProbe, Probe>("Chamber1"));
        builder.Build("Thermal");
        Assert.Throws<InvalidOperationException>(() =>
            builder.Simulated<IProbe, Probe>("Chamber2"));
    }

    [Fact]
    public async Task RejectsUnknownOrWrongType()
    {
        await using var run = Definition().StartSimulated();
        Assert.Throws<KeyNotFoundException>(() => run.Instrument<IProbe>("Supply1"));
        Assert.Throws<KeyNotFoundException>(() => run.Instrument<IProbe>("missing"));
    }
}
