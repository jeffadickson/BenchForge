using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class TypedMockMeasurementsTests
{
    [Fact]
    public async Task GenericCallerGetsResistanceOrVoltageWithCorrectUnits()
    {
        var clock = new ManualSimulationClock();
        var registry = new TypedMockMeasurementsBuilder()
            .Constant(ElectricResistance.FromKiloohms(4.7))
            .Constant(ElectricPotential.FromMillivolts(250))
            .Build();
        var meter = new TypedMockMeter(registry, clock);

        await meter.ConfigureAsync<ElectricResistance>();
        ElectricResistance resistance = await meter.MeasureAsync<ElectricResistance>();
        Assert.Equal(4700, resistance.Ohms, 8);

        await meter.ConfigureAsync<ElectricPotential>();
        ElectricPotential voltage = await meter.MeasureAsync<ElectricPotential>();
        Assert.Equal(0.25, voltage.Volts, 8);
    }

    [Fact]
    public async Task MeasurementRequestCannotSilentlyChangeInstrumentMode()
    {
        var meter = new TypedMockMeter(new TypedMockMeasurementsBuilder()
            .Constant(ElectricResistance.FromOhms(100))
            .Constant(ElectricPotential.FromVolts(1)).Build(), new ManualSimulationClock());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await meter.MeasureAsync<ElectricResistance>());
        await meter.ConfigureAsync<ElectricResistance>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await meter.MeasureAsync<ElectricPotential>());
    }

    [Fact]
    public async Task UnsupportedQuantityIsRejectedEvenThoughItImplementsIQuantity()
    {
        var meter = new TypedMockMeter(new TypedMockMeasurementsBuilder()
            .Constant(ElectricResistance.FromOhms(100)).Build(), new ManualSimulationClock());
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await meter.ConfigureAsync<Temperature>());
    }

    [Fact]
    public async Task TimeIndexedMockUsesExperimentClock()
    {
        var clock = new ManualSimulationClock();
        var meter = new TypedMockMeter(new TypedMockMeasurementsBuilder()
            .Register<ElectricResistance>(t =>
                ElectricResistance.FromOhms(100 + t.TotalSeconds * 10)).Build(), clock);
        await meter.ConfigureAsync<ElectricResistance>();
        Assert.Equal(100, (await meter.MeasureAsync<ElectricResistance>()).Ohms);
        clock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(150, (await meter.MeasureAsync<ElectricResistance>()).Ohms);
    }

    [Fact]
    public void DuplicateQuantityRegistrationIsRejected()
    {
        var builder = new TypedMockMeasurementsBuilder()
            .Constant(ElectricResistance.FromOhms(10));
        Assert.Throws<InvalidOperationException>(() =>
            builder.Constant(ElectricResistance.FromOhms(20)));
    }

    [Fact]
    public void GenericTypeDoesNotInferUnsupportedCapability()
    {
        var registry = new TypedMockMeasurementsBuilder()
            .Constant(ElectricResistance.FromOhms(10)).Build();
        Assert.True(registry.Supports<ElectricResistance>());
        Assert.False(registry.Supports<ElectricPotential>());
        Assert.Throws<NotSupportedException>(() =>
            registry.Evaluate<ElectricPotential>(TimeSpan.Zero));
    }
}
