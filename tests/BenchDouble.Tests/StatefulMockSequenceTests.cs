using BenchDouble.Generated;
using BenchDouble.Simulation;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Sequence output acceptance, cancellation and concurrency regressions for TR-043.</summary>
public sealed class StatefulMockSequenceTests
{
    private const string Definition = """
        {"schemaVersion":2,"identity":{"driverName":"SequenceMeter"},
         "modes":[{"id":"v","selectionCommand":"CONF:V"},{"id":"r","selectionCommand":"CONF:R"}],
         "capabilities":[
          {"name":"a","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"A?","modes":["v"],"mockDefault":0.5},
          {"name":"b","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"B?","modes":["v"],"mockDefault":0.25}]}
        """;
    private static StatefulMockEngine Engine(ISimulationClock? clock = null) => new(ImportedInstrumentDefinition.Parse(Definition),clock);

    /// <summary>A strict sequence returns each value once and never silently falls back after exhaustion.</summary>
    /// <returns>Completion after ordered output and explicit exhaustion.</returns>
    [Fact] public async Task StrictSequenceHasExplicitExhaustion()
    {
        var engine = Engine(); engine.SetSequence("a",[1,2,3]); await engine.ConfigureModeAsync("v");
        Assert.Equal(new[] { 1d,2d,3d },new[] { await engine.MeasureAsync("a"),await engine.MeasureAsync("a"),await engine.MeasureAsync("a") });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.MeasureAsync("a"));
        Assert.True(engine.SequenceStatus("a")!.Exhausted); Assert.Equal(0,engine.SequenceStatus("a")!.Remaining);
    }

    /// <summary>Hold-last mode stops changing cursor and revision after its initial values are consumed.</summary>
    /// <returns>Completion after repeated held readings.</returns>
    [Fact] public async Task RepeatLastDoesNotInventFurtherStateChanges()
    {
        var engine = Engine(); engine.SetSequence("a",[1,2],true); await engine.ConfigureModeAsync("v");
        Assert.Equal(1,await engine.MeasureAsync("a")); Assert.Equal(2,await engine.MeasureAsync("a")); var revision = engine.Snapshot().Revision;
        for (int i=0;i<8;i++) Assert.Equal(2,await engine.MeasureAsync("a"));
        Assert.Equal(revision,engine.Snapshot().Revision); Assert.Equal(2,engine.SequenceStatus("a")!.Position);
    }

    /// <summary>Sequence input arrays and old cursor snapshots remain independent of later mutation and reads.</summary>
    /// <returns>Completion after snapshot and input isolation checks.</returns>
    [Fact] public async Task SequenceInputsAndSnapshotsAreDetached()
    {
        var values = new[] { 1d,2d }; var engine = Engine(); engine.SetSequence("a",values); values[0]=99;
        var snapshot = engine.Snapshot(); await engine.ConfigureModeAsync("v"); Assert.Equal(1,await engine.MeasureAsync("a"));
        Assert.Equal(0,snapshot.SequencePositions["a"]); Assert.Equal(1,engine.Snapshot().SequencePositions["a"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string,int>)snapshot.SequencePositions).Add("other",5));
    }

    /// <summary>One-shot faults take precedence without discarding a sequence value.</summary>
    /// <returns>Completion after fault delivery and the first valid sequence reading.</returns>
    [Fact] public async Task FaultDeliveryDoesNotAdvanceSequence()
    {
        var engine = Engine(); engine.SetSequence("a",[7]); await engine.ConfigureModeAsync("v"); var fault = new IOException("one-shot"); engine.QueueFault("a",fault);
        Assert.Same(fault,await Assert.ThrowsAsync<IOException>(async () => await engine.MeasureAsync("a")));
        Assert.Equal(0,engine.SequenceStatus("a")!.Position); Assert.Equal(7,await engine.MeasureAsync("a"));
    }

    /// <summary>Concurrent callers consume each sequence element exactly once through the canonical gate.</summary>
    /// <returns>Completion after parallel reading and cursor checks.</returns>
    [Fact] public async Task ParallelReadsConsumeExactlyOnce()
    {
        var engine = Engine(); engine.SetSequence("a",Enumerable.Range(0,64).Select(i=>(double)i)); await engine.ConfigureModeAsync("v");
        var before = engine.Snapshot().Revision;
        var values = await Task.WhenAll(Enumerable.Range(0,64).Select(_ => Task.Run(async () => await engine.MeasureAsync("a"))));
        Assert.Equal(Enumerable.Range(0,64).Select(i=>(double)i),values.Order());
        Assert.Equal(64,engine.SequenceStatus("a")!.Position); Assert.Equal(before+64,engine.Snapshot().Revision);
    }
}
