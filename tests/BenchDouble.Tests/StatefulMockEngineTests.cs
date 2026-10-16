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
