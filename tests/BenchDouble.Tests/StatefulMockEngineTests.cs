using BenchDouble.Generated;
using BenchDouble.Simulation;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Hardware-free stateful mock regression tests.</summary>
public sealed class StatefulMockEngineTests
{
    private const string Definition = """
    {"schemaVersion":2,"identity":{"driverName":"StatefulMeter"},
     "modes":[{"id":"voltage","selectionCommand":"CONF:VOLT"},
              {"id":"resistance","selectionCommand":"CONF:RES"}],
     "capabilities":[
       {"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read",
        "query":"MEAS:VOLT?","modes":["voltage"],"mockDefault":0.25},
       {"name":"resistance","quantity":"ElectricResistance","unit":"Ohm","access":"read",
        "query":"MEAS:RES?","modes":["resistance"],"mockDefault":4700}]}
    """;

    /// <summary>Faults are consumed only in a compatible mode.</summary>
    /// <returns>Completion of mode and fault verification.</returns>
    [Fact]
    public async Task FaultIsConsumedOnlyInCompatibleMode()
    {
        var engine = new StatefulMockEngine(ImportedInstrumentDefinition.Parse(Definition));
        engine.QueueFault("voltage", new TimeoutException("synthetic"));
        await engine.ConfigureModeAsync("resistance");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.MeasureAsync("voltage"));
        await engine.ConfigureModeAsync("voltage");
        await Assert.ThrowsAsync<TimeoutException>(async () => await engine.MeasureAsync("voltage"));
        Assert.Equal(0.25, await engine.MeasureAsync("voltage"));
    }

    /// <summary>Step and ramp models use virtual time without wall-clock delays.</summary>
    /// <returns>Completion of deterministic behavior verification.</returns>
    [Fact]
    public async Task VirtualTimeModelsAreDeterministic()
    {
        var clock = new ManualSimulationClock();
        var mock = new ImportedInstrumentMock(ImportedInstrumentDefinition.Parse(Definition), clock);
        mock.SetBehavior("voltage", StatefulMockBehaviors.Step(TimeSpan.FromSeconds(5), 1, 2));
        await mock.ConfigureModeAsync("voltage");
        Assert.Equal(1, await mock.MeasureAsync("voltage"));
        clock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(2, await mock.MeasureAsync("voltage"));
        mock.SetBehavior("voltage", StatefulMockBehaviors.Ramp(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), 2, 4));
        clock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(3, await mock.MeasureAsync("voltage"));
    }

    /// <summary>Concurrent readings remain serialized and mode compatible.</summary>
    /// <returns>Completion of concurrent state verification.</returns>
    [Fact]
    public async Task ConcurrentReadsAreSerialized()
    {
        var mock = new ImportedInstrumentMock(ImportedInstrumentDefinition.Parse(Definition));
        await mock.ConfigureModeAsync("voltage");
        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => mock.MeasureAsync("voltage").AsTask()));
        Assert.All(results, value => Assert.Equal(0.25, value));
    }

    /// <summary>Async behaviors honor cancellation and preserve the exclusive transaction.</summary>
    /// <returns>Completion of cancellation verification.</returns>
    [Fact]
    public async Task AsyncModelHonorsCancellation()
    {
        var mock = new ImportedInstrumentMock(ImportedInstrumentDefinition.Parse(Definition));
        await mock.ConfigureModeAsync("voltage");
        mock.SetAsyncBehavior("voltage", async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), token);
            return 4.25;
        });
        Assert.Equal(4.25, await mock.MeasureAsync("voltage"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await mock.MeasureAsync("voltage", cancelled.Token));
        mock.SetAsyncBehavior("voltage", null);
        Assert.Equal(0.25, await mock.MeasureAsync("voltage"));
    }

    /// <summary>Unsupported captured quantities and invalid modes preserve queued faults.</summary>
    /// <returns>Completion of captured measurement validation.</returns>
    [Fact]
    public async Task CapturedReadValidationPrecedesFaultConsumption()
    {
        var smu = new VirtualSourceMeasureUnit(new VirtualSignalBus(), new ManualSimulationClock());
        var acquired = smu.AcquireCoherently(1, 8, UnitsNet.Duration.FromMilliseconds(1));
        var mock = new ImportedInstrumentMock(ImportedInstrumentDefinition.Parse(Definition));
        mock.QueueFault("resistance", new TimeoutException("resistance fault"));
        await mock.ConfigureModeAsync("resistance");
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await mock.MeasureCapturedAsync("resistance", acquired));
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await mock.MeasureAsync("resistance"));
        mock.QueueFault("voltage", new TimeoutException("voltage fault"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mock.MeasureCapturedAsync("voltage", acquired));
        await mock.ConfigureModeAsync("voltage");
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await mock.MeasureCapturedAsync("voltage", acquired));
        Assert.Equal(0, await mock.MeasureCapturedAsync("voltage", acquired));
    }

    /// <summary>Two imported meter mocks consume one immutable SMU and scope acquisition.</summary>
    /// <returns>Completion of correlated provenance verification.</returns>
    [Fact]
    public async Task CapturedMetersShareSourceAndWaveformProvenance()
    {
        const string definition = """
            {"schemaVersion":2,"identity":{"driverName":"CorrelatedMeters"},
             "modes":[{"id":"measure","selectionCommand":"CONF:VOLT"}],
             "capabilities":[
               {"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read",
                "query":"MEAS:VOLT?","modes":["measure"],"mockDefault":0},
               {"name":"current","quantity":"ElectricCurrent","unit":"Ampere","access":"read",
                "query":"MEAS:CURR?","modes":["measure"],"mockDefault":0}]}
            """;
        var metadata = ImportedInstrumentDefinition.Parse(definition);
        var clock = new ManualSimulationClock();
        var smu = new VirtualSourceMeasureUnit(new VirtualSignalBus(), clock);
        smu.SetLoad(UnitsNet.ElectricResistance.FromOhms(100));
        smu.SourceVoltage(UnitsNet.ElectricPotential.FromVolts(10),
            UnitsNet.ElectricCurrent.FromAmperes(0.02));
        smu.SetOutput(true);
        clock.AdvanceBy(TimeSpan.FromSeconds(3));
        var acquired = smu.AcquireCoherently(1, 8, UnitsNet.Duration.FromMilliseconds(1));
        var voltmeter = new ImportedInstrumentMock(metadata, clock);
        var ammeter = new ImportedInstrumentMock(metadata, clock);
        await voltmeter.ConfigureModeAsync("measure");
        await ammeter.ConfigureModeAsync("measure");
        var voltage = await voltmeter.ReadCapturedAsync("voltage", acquired);
        var current = await ammeter.ReadCapturedAsync("current", acquired);
        Assert.Equal(acquired.AcquisitionId, voltage.AcquisitionId);
        Assert.Equal(acquired.Waveform.AcquisitionId, current.AcquisitionId);
        Assert.Equal(voltage.SourceRevision, current.SourceRevision);
        Assert.Equal(voltage.Elapsed, current.Elapsed);
        Assert.Equal("Volt", voltage.Unit);
        Assert.Equal("Ampere", current.Unit);
        Assert.Equal(2, voltage.Value, 8);
        Assert.Equal(0.02, current.Value, 8);
        smu.SetOutput(false);
        Assert.Equal(2, (await voltmeter.ReadCapturedAsync("voltage", acquired)).Value, 8);
        Assert.Equal(0, smu.Measure().Voltage.Volts, 8);
    }

    /// <summary>Incompatible mock mode cannot consume a queued captured-measurement fault.</summary>
    /// <returns>Completion of mode and fault verification.</returns>
    [Fact]
    public async Task CapturedModeValidationPreservesFault()
    {
        var metadata = ImportedInstrumentDefinition.Parse(Definition);
        var smu = new VirtualSourceMeasureUnit(new VirtualSignalBus(), new ManualSimulationClock());
        var acquired = smu.AcquireCoherently(1, 4, UnitsNet.Duration.FromMilliseconds(1));
        var mock = new ImportedInstrumentMock(metadata);
        mock.QueueFault("voltage", new TimeoutException("synthetic"));
        await mock.ConfigureModeAsync("resistance");
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await mock.ReadCapturedAsync("voltage", acquired));
        await mock.ConfigureModeAsync("voltage");
        await Assert.ThrowsAsync<TimeoutException>(
            async () => await mock.ReadCapturedAsync("voltage", acquired));
        Assert.Equal(0, (await mock.ReadCapturedAsync("voltage", acquired)).Value);
    }

    /// <summary>Reset restores definition defaults without mutating old snapshots.</summary>
    /// <returns>Completion of snapshot verification.</returns>
    [Fact]
    public async Task ResetPreservesSnapshots()
    {
        var engine = new StatefulMockEngine(ImportedInstrumentDefinition.Parse(Definition));
        await engine.ConfigureModeAsync("voltage");
        engine.SetReading("voltage", 1.5);
        var old = engine.Snapshot();
        engine.Reset();
        await engine.ConfigureModeAsync("voltage");
        Assert.Equal(0.25, await engine.MeasureAsync("voltage"));
        Assert.Equal(1.5, old.Overrides["voltage"]);
    }
}
