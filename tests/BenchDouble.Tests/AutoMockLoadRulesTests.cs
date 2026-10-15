using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class AutoMockLoadRulesTests
{
    [Fact]
    public void TimedOvercurrentOverridesOnlyAfterFiveSeconds()
    {
        var rules = new AutoMockLoadRulesBuilder()
            .FixedCurrent("overcurrent", ElectricCurrent.FromAmperes(2.5),
                new AutoMockLoadCondition(From: TimeSpan.FromSeconds(5))).Build();
        var mock = new RuleBasedLoadBehavior(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)), rules);
        Assert.Equal(0.5, mock.CurrentAt(ElectricPotential.FromVolts(12),
            TimeSpan.FromSeconds(4.999)).Amperes, 10);
        Assert.Equal(2.5, mock.CurrentAt(ElectricPotential.FromVolts(12),
            TimeSpan.FromSeconds(5)).Amperes, 10);
    }

    [Fact]
    public void EvaluationReportsMatchedRuleForObservability()
    {
        var rules = new AutoMockLoadRulesBuilder()
            .FixedCurrent("fault", ElectricCurrent.FromAmperes(2.5),
                new AutoMockLoadCondition(From: TimeSpan.FromSeconds(5))).Build();
        var mock = new RuleBasedLoadBehavior(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)), rules);
        var normal = mock.EvaluateAt(ElectricPotential.FromVolts(12), TimeSpan.Zero);
        var fault = mock.EvaluateAt(ElectricPotential.FromVolts(12), TimeSpan.FromSeconds(5));
        Assert.Null(normal.MatchedRule);
        Assert.Equal("fault", fault.MatchedRule);
        Assert.Equal(TimeSpan.FromSeconds(5), fault.SimulatedElapsed);
    }

    [Fact]
    public void VoltageRangeCanScaleBaseline()
    {
        var rules = new AutoMockLoadRulesBuilder()
            .ScaleBaseline("high-voltage-gain", 1.10,
                new AutoMockLoadCondition(MinimumVoltage: ElectricPotential.FromVolts(10),
                    MaximumVoltage: ElectricPotential.FromVolts(15))).Build();
        var mock = new RuleBasedLoadBehavior(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)), rules);
        Assert.Equal(0.55, mock.CurrentAt(ElectricPotential.FromVolts(12), TimeSpan.Zero).Amperes, 10);
        Assert.Equal(0.25, mock.CurrentAt(ElectricPotential.FromVolts(6), TimeSpan.Zero).Amperes, 10);
    }

    [Fact]
    public void PriorityOverridesDeclarationOrder()
    {
        var rules = new AutoMockLoadRulesBuilder()
            .FixedCurrent("low", ElectricCurrent.FromAmperes(1), priority: 1)
            .FixedCurrent("high", ElectricCurrent.FromAmperes(3), priority: 10).Build();
        var mock = new RuleBasedLoadBehavior(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)), rules);
        Assert.Equal(3, mock.CurrentAt(ElectricPotential.FromVolts(12), TimeSpan.Zero).Amperes);
    }

    [Fact]
    public void InvalidRulesAreRejectedBeforeRun()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoMockLoadRulesBuilder().FixedCurrent("bad",
                ElectricCurrent.FromAmperes(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoMockLoadRulesBuilder().ScaleBaseline("bad", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoMockLoadRulesBuilder().FixedCurrent("range",
                ElectricCurrent.FromAmperes(1), new AutoMockLoadCondition(
                    From: TimeSpan.FromSeconds(10), Until: TimeSpan.FromSeconds(5))));
        var builder = new AutoMockLoadRulesBuilder().ScaleBaseline("one", 1);
        Assert.Throws<InvalidOperationException>(() => builder.ScaleBaseline("one", 2));
    }

    [Fact]
    public async Task InstrumentOutputOffStillReturnsZeroDuringInjectedFault()
    {
        var clock = new ManualSimulationClock();
        var rules = new AutoMockLoadRulesBuilder()
            .FixedCurrent("fault", ElectricCurrent.FromAmperes(2.5),
                new AutoMockLoadCondition(From: TimeSpan.FromSeconds(5))).Build();
        var supply = new MockPowerSupply(
            new RuleBasedLoadBehavior(new ResistiveLoadBehavior(
                ElectricResistance.FromOhms(24)), rules), clock);
        await supply.ConfigureAsync(ElectricPotential.FromVolts(12), ElectricCurrent.FromAmperes(3));
        await supply.SetOutputAsync(true);
        clock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(2.5, (await supply.MeasureAsync()).Current.Amperes);
        await supply.SetOutputAsync(false);
        Assert.Equal(0, (await supply.MeasureAsync()).Current.Amperes);
    }
}
