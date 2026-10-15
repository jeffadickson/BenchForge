using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Mode-aware UnitsNet mock acceptance tests; no physical instruments.</summary>
public sealed class ModeAwareMockInstrumentTests
{
    [Fact]
    public async Task DifferentModesReturnCorrectQuantitiesWithoutReconfiguration()
    {
        var clock = new ManualSimulationClock();
        var meter = new ModeAwareMockBuilder()
            .Mode("voltage")
            .Mode("resistance")
            .Measurement("voltage", _ => ElectricPotential.FromMillivolts(250))
            .Measurement("resistance", t =>
                ElectricResistance.FromOhms(1000 + 10 * t.TotalSeconds))
            .Build(clock);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await meter.MeasureAsync<ElectricPotential>());
        await meter.ConfigureModeAsync("voltage");
        Assert.Equal(0.25, (await meter.MeasureAsync<ElectricPotential>()).Volts, 10);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await meter.MeasureAsync<ElectricResistance>());

        await meter.ConfigureModeAsync("resistance");
        Assert.Equal(1000, (await meter.MeasureAsync<ElectricResistance>()).Ohms);
        clock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(1050, (await meter.MeasureAsync<ElectricResistance>()).Ohms);
    }

    [Fact]
    public async Task UnknownModeAndUnsupportedQuantityFail()
    {
        var meter = new ModeAwareMockBuilder()
            .Mode("voltage")
            .Measurement("voltage", _ => ElectricPotential.FromVolts(1))
            .Build(new ManualSimulationClock());
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await meter.ConfigureModeAsync("current"));
        await meter.ConfigureModeAsync("voltage");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await meter.MeasureAsync<ElectricCurrent>());
    }

    [Fact]
    public void DuplicateModesAndMeasurementsAreRejected()
    {
        var builder = new ModeAwareMockBuilder().Mode("voltage");
        Assert.Throws<InvalidOperationException>(() => builder.Mode("voltage"));
        builder.Measurement("voltage", _ => ElectricPotential.FromVolts(1));
        Assert.Throws<InvalidOperationException>(() =>
            builder.Measurement("voltage", _ => ElectricPotential.FromVolts(2)));
        Assert.Throws<InvalidOperationException>(() =>
            builder.Measurement("resistance", _ => ElectricResistance.FromOhms(100)));
    }

    [Fact]
    public void ModesAreExplicitlyRequired()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ModeAwareMockBuilder().Build(new ManualSimulationClock()));
    }
}
