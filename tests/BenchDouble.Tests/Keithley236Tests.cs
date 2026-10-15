using BenchDouble.Instruments.Keithley;
using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class Keithley236Tests
{
    [Fact]
    public async Task SourceVoltageMeasureCurrent_ResistiveDut()
    {
        var bus = new Keithley236MockBus { ResistanceOhms = 1000 };
        var meter = new Keithley236(bus);
        await meter.ConfigureAsync(SourceMeasureMode.SourceVoltageMeasureCurrent);
        await meter.SetBiasAsync(ElectricPotential.FromVolts(5), TimeSpan.Zero);
        await meter.SetComplianceAsync(ElectricCurrent.FromMilliamperes(10));
        await meter.SetIntegrationAsync(IntegrationSpeed.Medium);
        await meter.SetOperateAsync(true);
        Assert.Equal(0.005, (await meter.MeasureCurrentAsync()).Amperes, 10);
        await meter.StandbyAsync();
        Assert.False(bus.Operate);
        Assert.Equal(new[] { "W:N0X", "W:F0,0X", "W:B5,0,0X", "W:L0.01,0X", "W:S1X", "W:N1X", "W:G4,2,0X", "R", "W:N0X" }, bus.Transcript);
    }

    [Fact]
    public async Task SourceCurrentMeasureVoltage_ResistiveDut()
    {
        var bus = new Keithley236MockBus { ResistanceOhms = 2000 };
        var meter = new Keithley236(bus);
        await meter.ConfigureAsync(SourceMeasureMode.SourceCurrentMeasureVoltage);
        await meter.SetBiasAsync(ElectricCurrent.FromMilliamperes(1), TimeSpan.Zero);
        await meter.SetComplianceAsync(ElectricPotential.FromVolts(10));
        await meter.SetOperateAsync(true);
        Assert.Equal(2, (await meter.MeasureVoltageAsync()).Volts, 10);
        await meter.StandbyAsync();
    }

    [Fact]
    public async Task RejectInvalidModesAndUnsafeValues()
    {
        var bus = new Keithley236MockBus();
        var meter = new Keithley236(bus);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await meter.SetOperateAsync(true));
        await meter.ConfigureAsync(SourceMeasureMode.SourceVoltageMeasureCurrent);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await meter.SetBiasAsync(ElectricPotential.FromVolts(200), TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await meter.SetBiasAsync(ElectricPotential.FromVolts(1), TimeSpan.FromSeconds(66)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await meter.MeasureVoltageAsync());
        Assert.False(bus.Operate);
    }

    [Fact]
    public async Task ComplianceIsEnforcedByMock()
    {
        var bus = new Keithley236MockBus { ResistanceOhms = 100 };
        var meter = new Keithley236(bus);
        await meter.ConfigureAsync(SourceMeasureMode.SourceVoltageMeasureCurrent);
        await meter.SetBiasAsync(ElectricPotential.FromVolts(10), TimeSpan.Zero);
        await meter.SetComplianceAsync(ElectricCurrent.FromMilliamperes(5));
        await meter.SetOperateAsync(true);
        Assert.Equal(0.005, (await meter.MeasureCurrentAsync()).Amperes, 10);
        await meter.StandbyAsync();
    }
    [Fact]
    public async Task OperateRequiresBiasAsWellAsModeAndCompliance()
    {
        var bus = new Keithley236MockBus();
        var instrument = new Keithley236(bus);
        await instrument.ConfigureAsync(SourceMeasureMode.SourceVoltageMeasureCurrent);
        await instrument.SetComplianceAsync(ElectricCurrent.FromMilliamperes(5));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await instrument.SetOperateAsync(true));
        Assert.False(bus.Operate);
        await instrument.SetBiasAsync(ElectricPotential.FromVolts(1), TimeSpan.Zero);
        await instrument.SetOperateAsync(true);
        Assert.True(bus.Operate);
        await instrument.StandbyAsync();
    }

    [Fact]
    public async Task InvalidSourceModeDoesNotWriteToBus()
    {
        var bus = new Keithley236MockBus();
        var instrument = new Keithley236(bus);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await instrument.ConfigureAsync((SourceMeasureMode)999));
        Assert.Empty(bus.Transcript);
    }

    [Fact]
    public async Task ChangingSourceModeInvalidatesOldCompliance()
    {
        var bus = new Keithley236MockBus();
        var instrument = new Keithley236(bus);
        await instrument.ConfigureAsync(SourceMeasureMode.SourceVoltageMeasureCurrent);
        await instrument.SetComplianceAsync(ElectricCurrent.FromMilliamperes(5));
        await instrument.ConfigureAsync(SourceMeasureMode.SourceCurrentMeasureVoltage);

        Assert.False(instrument.OperateRequested);
        Assert.Equal(0, bus.Compliance);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await instrument.SetOperateAsync(true));
        await instrument.SetComplianceAsync(ElectricPotential.FromVolts(10));
        await instrument.SetBiasAsync(ElectricCurrent.FromMilliamperes(1), TimeSpan.Zero);
        await instrument.SetOperateAsync(true);
        Assert.True(bus.Operate);
        await instrument.StandbyAsync();
    }

    [Fact]
    public async Task FailedModeWriteLeavesDriverUnconfiguredAndInStandby()
    {
        var bus = new FaultingBus();
        var instrument = new Keithley236(bus);
        await instrument.ConfigureAsync(SourceMeasureMode.SourceVoltageMeasureCurrent);
        await instrument.SetComplianceAsync(ElectricCurrent.FromMilliamperes(5));
        bus.FailModeWrite = true;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await instrument.ConfigureAsync(SourceMeasureMode.SourceCurrentMeasureVoltage));
        Assert.Null(instrument.ConfiguredMode);
        Assert.False(instrument.OperateRequested);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await instrument.SetOperateAsync(true));
    }

    private sealed class FaultingBus : IKeithley236Bus
    {
        public bool FailModeWrite { get; set; }
        public ValueTask WriteAsync(string command, CancellationToken ct = default)
        {
            if (FailModeWrite && command.StartsWith("F", StringComparison.Ordinal))
                throw new InvalidOperationException("Simulated IEEE-488 write failure");
            return ValueTask.CompletedTask;
        }
        public ValueTask<string> ReadAsync(CancellationToken ct = default) =>
            ValueTask.FromResult("0");
    }

}
