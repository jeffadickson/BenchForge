using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class ThermalCycleTests
{
    private static readonly ThermalChamberModel Chamber =
        new(ThermalCycleSettings.Example());

    [Theory]
    [InlineData(0, 30, "Heat ramp")]
    [InlineData(0.5, 50, "Heat ramp")]
    [InlineData(1, 70, "Hot dwell")]
    [InlineData(3, 70, "Cool ramp")]
    [InlineData(3.5, 20, "Cool ramp")]
    [InlineData(4, -30, "Cold dwell")]
    [InlineData(6, -30, "Return ramp")]
    [InlineData(6.5, 0, "Return ramp")]
    [InlineData(7, 30, "Complete")]
    public void ProfileMatchesAllRampAndDwellBoundaries(
        double hour, double expectedCelsius, string expectedPhase)
    {
        var sample = Chamber.At(TimeSpan.FromHours(hour));
        Assert.Equal(expectedCelsius, sample.Temperature.DegreesCelsius, 8);
        Assert.Equal(expectedPhase, sample.Phase);
    }

    [Fact]
    public async Task InjectedLoadRisesExactlyTenPercentAtHotDwell()
    {
        var clock = new ManualSimulationClock();
        ILoadBehavior behavior = new TemperatureDependentLoadBehavior(
            Chamber, clock, ElectricCurrent.FromAmperes(0.5),
            Temperature.FromDegreesCelsius(30),
            Temperature.FromDegreesCelsius(70), 0.10);
        var supply = new MockPowerSupply(behavior, clock);
        await supply.ConfigureAsync(ElectricPotential.FromVolts(12),
            ElectricCurrent.FromAmperes(1));
        await supply.SetOutputAsync(true);
        Assert.Equal(0.5, (await supply.MeasureAsync()).Current.Amperes, 10);
        clock.AdvanceBy(TimeSpan.FromMinutes(30));
        Assert.Equal(0.525, (await supply.MeasureAsync()).Current.Amperes, 10);
        clock.AdvanceBy(TimeSpan.FromMinutes(30));
        Assert.Equal(0.55, (await supply.MeasureAsync()).Current.Amperes, 10);
        clock.AdvanceBy(TimeSpan.FromHours(3));
        Assert.Equal(0.5, (await supply.MeasureAsync()).Current.Amperes, 10);
        await supply.SetOutputAsync(false);
        Assert.Equal(0, (await supply.MeasureAsync()).Current.Amperes, 10);
    }

    [Fact]
    public void RejectsInvalidSchedule()
    {
        var settings = ThermalCycleSettings.Example() with { HeatRamp = TimeSpan.Zero };
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalChamberModel(settings));
    }

    [Fact]
    public void ManualClockCanCompleteSevenHoursWithoutSleeping()
    {
        var clock = new ManualSimulationClock();
        clock.AdvanceBy(Chamber.Settings.TotalDuration);
        Assert.Equal(TimeSpan.FromHours(7), clock.Elapsed);
        Assert.Equal("Complete", Chamber.At(clock.Elapsed).Phase);
    }
}
