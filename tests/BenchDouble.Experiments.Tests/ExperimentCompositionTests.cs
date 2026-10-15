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

    public interface IAutoSupply { ElectricCurrent Read(); }
    public sealed class AutoSupply(ILoadBehavior behavior, ISimulationClock clock) : IAutoSupply
    {
        public ElectricCurrent Read() =>
            behavior.CurrentAt(ElectricPotential.FromVolts(12), clock.Elapsed);
    }

    [Fact]
    public async Task AutoMockJsonInjectsFaultWithoutChangingInstrument()
    {
        const string json = """
            {"version":1,"rules":[
                {"name":"fault","priority":100,"action":"FixedCurrent",
                 "value":2.5,"fromSeconds":5}
            ]}
            """;
        var definition = new ExperimentDefinitionBuilder()
            .AutoMockLoadFromJson(_ => new ResistiveLoadBehavior(
                ElectricResistance.FromOhms(24)), json)
            .Simulated<IAutoSupply, AutoSupply>("Supply1")
            .Build("AutoMock");
        await using var run = definition.StartSimulated();
        Assert.NotNull(run.AutoMockRuleSha256);
        Assert.Equal(64, run.AutoMockRuleSha256!.Length);
        Assert.Equal(definition.AutoMockRuleSha256, run.AutoMockRuleSha256);
        var supply = run.Instrument<IAutoSupply>("Supply1");
        Assert.Equal(0.5, supply.Read().Amperes, 10);
        run.Clock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(2.5, supply.Read().Amperes, 10);
    }

    [Fact]
    public async Task AutoMockRulesAndClockAreIsolatedAcrossRuns()
    {
        var definition = new ExperimentDefinitionBuilder()
            .AutoMockLoad(_ => new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)),
                rules => rules.FixedCurrent("fault", ElectricCurrent.FromAmperes(2.5),
                    new AutoMockLoadCondition(From: TimeSpan.FromSeconds(5))))
            .Simulated<IAutoSupply, AutoSupply>("Supply1")
            .Build("AutoMock isolation");
        await using var first = definition.StartSimulated();
        await using var second = definition.StartSimulated();
        first.Clock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(2.5, first.Instrument<IAutoSupply>("Supply1").Read().Amperes, 10);
        Assert.Equal(0.5, second.Instrument<IAutoSupply>("Supply1").Read().Amperes, 10);
    }

    [Fact]
    public void DifferentRuleSetsHaveDifferentHashes()
    {
        var first = new AutoMockLoadRulesBuilder()
            .FixedCurrent("fault", ElectricCurrent.FromAmperes(2.5)).Build();
        var second = new AutoMockLoadRulesBuilder()
            .FixedCurrent("fault", ElectricCurrent.FromAmperes(3.0)).Build();
        Assert.NotEqual(AutoMockRuleFiles.Sha256(first), AutoMockRuleFiles.Sha256(second));
        Assert.Equal(AutoMockRuleFiles.Sha256(first), AutoMockRuleFiles.Sha256(first));
    }

}
