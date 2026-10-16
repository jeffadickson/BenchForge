using BenchDouble.Experiments;
using BenchDouble.Generated;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;
using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>End-to-end, run-scoped virtual source, meter and oscilloscope integration tests.</summary>
public sealed class VirtualInstrumentIntegrationTests
{
    private const string VoltageDefinition = """
        {"schemaVersion":2,"identity":{"driverName":"VirtualVoltmeter"},
         "modes":[{"id":"voltage","selectionCommand":"CONF:VOLT"}],
         "capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt",
         "access":"read","query":"MEAS:VOLT?","modes":["voltage"],"mockDefault":0.0}]}
        """;
    private const string CurrentDefinition = """
        {"schemaVersion":2,"identity":{"driverName":"VirtualAmmeter"},
         "modes":[{"id":"current","selectionCommand":"CONF:CURR"}],
         "capabilities":[{"name":"current","quantity":"ElectricCurrent","unit":"Ampere",
         "access":"read","query":"MEAS:CURR?","modes":["current"],"mockDefault":0.0}]}
        """;

    private static ExperimentDefinition CreateDefinition()
    {
        var volts = ImportedInstrumentDefinition.Parse(VoltageDefinition);
        var amps = ImportedInstrumentDefinition.Parse(CurrentDefinition);
        return new ExperimentDefinitionBuilder()
            .Model<VirtualSignalBus>(_ => new VirtualSignalBus())
            .Model<VirtualSourceMeasureUnit>(sp => new VirtualSourceMeasureUnit(
                sp.GetRequiredService<VirtualSignalBus>(),
                sp.GetRequiredService<ISimulationClock>()))
            .Simulated<VirtualSourceMeasureUnit>("SMU",
                sp => sp.GetRequiredService<VirtualSourceMeasureUnit>())
            .Simulated<VirtualProgrammablePowerSupply>("Supply",
                sp => new VirtualProgrammablePowerSupply(sp.GetRequiredService<VirtualSourceMeasureUnit>()))
            .Simulated<VirtualCoherentAcquisitionService>("Scope",
                sp => new VirtualCoherentAcquisitionService(sp.GetRequiredService<VirtualSourceMeasureUnit>()))
            .StatefulMock<ImportedInstrumentMock, VirtualSignalBus>("Voltmeter",
                (clock, logger, bus) =>
                {
                    var meter = new ImportedInstrumentMock(volts, clock, logger);
                    meter.SetBehavior("voltage", StatefulMockBehaviors.SignalVoltage(bus));
                    return meter;
                })
            .StatefulMock<ImportedInstrumentMock, VirtualSignalBus>("Ammeter",
                (clock, logger, bus) =>
                {
                    var meter = new ImportedInstrumentMock(amps, clock, logger);
                    meter.SetBehavior("current", StatefulMockBehaviors.SignalCurrent(bus));
                    return meter;
                })
            .Build("Virtual source-measure experiment");
    }

    /// <summary>Two injected meters observe the same compliant source and acquisition.</summary>
    /// <returns>Completion of the virtual instrument verification.</returns>
    [Fact]
    public async Task SharedCircuitMeasurementsAndWaveformAgree()
    {
        await using var run = CreateDefinition().StartSimulated();
        var smu = run.Instrument<VirtualSourceMeasureUnit>("SMU");
        var voltmeter = run.Instrument<ImportedInstrumentMock>("Voltmeter");
        var ammeter = run.Instrument<ImportedInstrumentMock>("Ammeter");
        await voltmeter.ConfigureModeAsync("voltage");
        await ammeter.ConfigureModeAsync("current");

        smu.SetLoad(ElectricResistance.FromOhms(100));
        smu.SourceVoltage(ElectricPotential.FromVolts(10), ElectricCurrent.FromMilliamperes(20));
        smu.SetOutput(true);
        run.Clock.AdvanceBy(TimeSpan.FromSeconds(7));
        var acquired = run.Instrument<VirtualCoherentAcquisitionService>("Scope")
            .Acquire(1, 8, Duration.FromMilliseconds(1), 0.001);
        Assert.True(acquired.Measurement.ComplianceActive);
        Assert.Equal(2, acquired.Voltmeter.Volts, 8);
        Assert.Equal(0.02, acquired.Ammeter.Amperes, 8);
        Assert.Equal(acquired.Voltmeter.Volts, await voltmeter.MeasureAsync("voltage"), 8);
        Assert.Equal(acquired.Ammeter.Amperes, await ammeter.MeasureAsync("current"), 8);
        Assert.Equal(acquired.AcquisitionId, acquired.Waveform.AcquisitionId);
        Assert.Equal(7, acquired.Waveform.TimeAt(0).Seconds, 8);

        smu.SetOutput(false);
        Assert.Equal(2, acquired.Waveform.VoltageAt(7).Volts, 8);
        Assert.Equal(0, await voltmeter.MeasureAsync("voltage"));
    }

    /// <summary>Generated-style meters can read a fixed acquisition after source settings change.</summary>
    /// <returns>Completion of acquisition correlation and fault assertions.</returns>
    [Fact]
    public async Task MetersReadOneCapturedSnapshotDespiteSubsequentSourceChanges()
    {
        await using var run = CreateDefinition().StartSimulated();
        var smu = run.Instrument<VirtualSourceMeasureUnit>("SMU");
        var voltmeter = run.Instrument<ImportedInstrumentMock>("Voltmeter");
        var ammeter = run.Instrument<ImportedInstrumentMock>("Ammeter");
        await voltmeter.ConfigureModeAsync("voltage");
        await ammeter.ConfigureModeAsync("current");
        smu.SetLoad(ElectricResistance.FromOhms(100));
        smu.SourceVoltage(ElectricPotential.FromVolts(10), ElectricCurrent.FromMilliamperes(20));
        smu.SetOutput(true);
        var acquired = run.Instrument<VirtualCoherentAcquisitionService>("Scope")
            .Acquire(1, 16, Duration.FromMilliseconds(1));
        smu.SetOutput(false);
        Assert.Equal(2, await voltmeter.MeasureCapturedAsync("voltage", acquired), 8);
        Assert.Equal(0.02, await ammeter.MeasureCapturedAsync("current", acquired), 8);
        Assert.Equal(0, await voltmeter.MeasureAsync("voltage"));
        Assert.Equal(0, await ammeter.MeasureAsync("current"));

        voltmeter.QueueFault("voltage", new TimeoutException("simulated"));
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await voltmeter.MeasureCapturedAsync("voltage", acquired));
        Assert.Equal(2, await voltmeter.MeasureCapturedAsync("voltage", acquired), 8);
        Assert.Equal(acquired.AcquisitionId, acquired.Waveform.AcquisitionId);
    }

    /// <summary>Current sourcing clips at the voltage compliance threshold.</summary>
    /// <returns>Completion of current-source verification.</returns>
    [Fact]
    public async Task CurrentModeObeysVoltageCompliance()
    {
        await using var run = CreateDefinition().StartSimulated();
        var source = run.Instrument<VirtualSourceMeasureUnit>("SMU");
        source.SetLoad(ElectricResistance.FromOhms(1000));
        source.SourceCurrent(ElectricCurrent.FromMilliamperes(10), ElectricPotential.FromVolts(5));
        source.SetOutput(true);
        var clipped = source.Measure();
        Assert.Equal(VirtualSourceMode.Current, clipped.Mode);
        Assert.True(clipped.ComplianceActive);
        Assert.Equal(5, clipped.Voltage.Volts, 8);
        Assert.Equal(0.005, clipped.Current.Amperes, 8);
        source.SetLoad(ElectricResistance.FromOhms(100));
        var normal = source.Measure();
        Assert.False(normal.ComplianceActive);
        Assert.Equal(1, normal.Voltage.Volts, 8);
        Assert.Equal(0.01, normal.Current.Amperes, 8);
    }

    /// <summary>Experiment scopes isolate source settings, virtual clocks and measurement overrides.</summary>
    /// <returns>Completion of run isolation verification.</returns>
    [Fact]
    public async Task SeparateRunsHaveIndependentPhysicalState()
    {
        var definition = CreateDefinition();
        await using var first = definition.StartSimulated();
        await using var second = definition.StartSimulated();
        var a = first.Instrument<VirtualSourceMeasureUnit>("SMU");
        var b = second.Instrument<VirtualSourceMeasureUnit>("SMU");
        a.SourceVoltage(ElectricPotential.FromVolts(4), ElectricCurrent.FromAmperes(1));
        a.SetOutput(true);
        first.Clock.AdvanceBy(TimeSpan.FromSeconds(3));
        Assert.Equal(4, a.Measure().Voltage.Volts, 8);
        Assert.Equal(0, b.Measure().Voltage.Volts, 8);
        Assert.Equal(TimeSpan.FromSeconds(3), a.Measure().Elapsed);
        Assert.Equal(TimeSpan.Zero, b.Measure().Elapsed);
        var meterA = first.Instrument<ImportedInstrumentMock>("Voltmeter");
        var meterB = second.Instrument<ImportedInstrumentMock>("Voltmeter");
        Assert.NotSame(meterA, meterB);
        await meterA.ConfigureModeAsync("voltage");
        await meterB.ConfigureModeAsync("voltage");
        Assert.Equal(4, await meterA.MeasureAsync("voltage"));
        Assert.Equal(0, await meterB.MeasureAsync("voltage"));
    }

    /// <summary>Supply and SMU facades control one physical output in the same run.</summary>
    /// <returns>Completion of facade identity verification.</returns>
    [Fact]
    public async Task SupplyFacadeSharesSourceState()
    {
        await using var run = CreateDefinition().StartSimulated();
        var supply = run.Instrument<VirtualProgrammablePowerSupply>("Supply");
        var smu = run.Instrument<VirtualSourceMeasureUnit>("SMU");
        supply.Configure(ElectricPotential.FromVolts(3), ElectricCurrent.FromAmperes(1));
        supply.SetOutput(true);
        Assert.Equal(3, smu.Measure().Voltage.Volts, 8);
        smu.SetOutput(false);
        Assert.Equal(0, supply.Measure().Voltage.Volts, 8);
    }
}
