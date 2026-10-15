using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class PowerSupplyBehaviorTests
{
    [Fact]
    public async Task TimedFaultChangesReadingAtVirtualFiveSeconds()
    {
        var clock = new ManualSimulationClock();
        ILoadBehavior behavior = new TimedOvercurrentBehavior(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)),
            TimeSpan.FromSeconds(5), ElectricCurrent.FromAmperes(2.5));
        var supply = new MockPowerSupply(behavior, clock);
        await supply.ConfigureAsync(ElectricPotential.FromVolts(12), ElectricCurrent.FromAmperes(3));
        await supply.SetOutputAsync(true);
        Assert.Equal(0.5, (await supply.MeasureAsync()).Current.Amperes, 10);
        clock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(2.5, (await supply.MeasureAsync()).Current.Amperes, 10);
        await supply.SetOutputAsync(false);
        Assert.Equal(0, (await supply.MeasureAsync()).Current.Amperes);
    }

    [Fact]
    public async Task DifferentInjectedBehaviorDoesNotChangeInstrumentCode()
    {
        var clock = new ManualSimulationClock();
        var supply = new MockPowerSupply(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(6)), clock);
        await supply.ConfigureAsync(ElectricPotential.FromVolts(12), ElectricCurrent.FromAmperes(3));
        await supply.SetOutputAsync(true);
        Assert.Equal(2, (await supply.MeasureAsync()).Current.Amperes, 10);
    }

    [Fact]
    public async Task CurrentLimitFlagIsMetadataNotAnInventedComplianceModel()
    {
        var supply = new MockPowerSupply(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(2)),
            new ManualSimulationClock());
        await supply.ConfigureAsync(ElectricPotential.FromVolts(12), ElectricCurrent.FromAmperes(3));
        await supply.SetOutputAsync(true);
        var sample = await supply.MeasureAsync();
        Assert.Equal(6, sample.Current.Amperes);
        Assert.True(sample.CurrentLimitExceeded);
    }

    [Fact]
    public void NegativeVirtualTimeAdvanceIsRejected()
    {
        var clock = new ManualSimulationClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceBy(TimeSpan.FromSeconds(-1)));
    }
}
