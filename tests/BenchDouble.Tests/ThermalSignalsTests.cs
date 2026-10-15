using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class ThermalSignalsTests
{
    [Fact]
    public void SensorLagsBehindOneHourHeatingRamp()
    {
        var chamber = new ThermalChamberModel(ThermalCycleSettings.Example());
        var sensor = new FirstOrderThermalSensor(
            chamber, TimeSpan.FromMinutes(20),
            Temperature.FromDegreesCelsius(30));

        var atOneHour = sensor.ReadAt(TimeSpan.FromHours(1)).DegreesCelsius;
        // Exact first-order response to a 40 C/hour linear ramp.
        double expected = 70 - (40.0 / 3) * (1 - Math.Exp(-3));
        Assert.Equal(expected, atOneHour, 8);
        Assert.InRange(atOneHour, 30, 70);
        Assert.True(sensor.ReadAt(TimeSpan.FromHours(2)).DegreesCelsius > atOneHour);
    }

    [Fact]
    public void ReadFrequencyDoesNotChangePhysicalResult()
    {
        var chamber = new ThermalChamberModel(ThermalCycleSettings.Example());
        var sparse = new FirstOrderThermalSensor(chamber, TimeSpan.FromMinutes(20),
            Temperature.FromDegreesCelsius(30));
        var dense = new FirstOrderThermalSensor(chamber, TimeSpan.FromMinutes(20),
            Temperature.FromDegreesCelsius(30));

        for (int minute = 1; minute <= 420; minute++)
            dense.ReadAt(TimeSpan.FromMinutes(minute));

        Assert.Equal(
            sparse.ReadAt(TimeSpan.FromHours(7)).Kelvins,
            dense.ReadAt(TimeSpan.FromHours(7)).Kelvins, 7);
    }

    [Fact]
    public void RepeatedSameTimestampIsStableAndBackwardTimeIsRejected()
    {
        var sensor = new FirstOrderThermalSensor(
            new ThermalChamberModel(ThermalCycleSettings.Example()),
            TimeSpan.FromMinutes(20), Temperature.FromDegreesCelsius(30));
        var t = TimeSpan.FromMinutes(30);
        Assert.Equal(sensor.ReadAt(t), sensor.ReadAt(t));
        Assert.Throws<InvalidOperationException>(() => sensor.ReadAt(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void SensorDoesNotJumpImmediatelyAtCoolingBoundary()
    {
        var chamber = new ThermalChamberModel(ThermalCycleSettings.Example());
        var sensor = new FirstOrderThermalSensor(chamber, TimeSpan.FromMinutes(20),
            Temperature.FromDegreesCelsius(30));
        var atBoundary = sensor.ReadAt(TimeSpan.FromHours(3)).DegreesCelsius;
        var justAfter = sensor.ReadAt(TimeSpan.FromHours(3) + TimeSpan.FromSeconds(1)).DegreesCelsius;
        Assert.InRange(Math.Abs(atBoundary - justAfter), 0, 0.01);
    }
}
