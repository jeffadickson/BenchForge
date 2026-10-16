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
