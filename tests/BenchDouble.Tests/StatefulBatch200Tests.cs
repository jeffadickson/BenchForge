using BenchDouble.Generated;
using BenchDouble.Simulation;
using Xunit;
namespace BenchDouble.Tests;
/// <summary>Mock diagnostics and bulk-state regression tests for batch 200.</summary>
public sealed class StatefulBatch200Tests
{
    private const string Definition = """
    {"schemaVersion":2,"identity":{"driverName":"Batch200"},"modes":[{"id":"voltage","selectionCommand":"CONF:VOLT"},{"id":"resistance","selectionCommand":"CONF:RES"}],"capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"V?","modes":["voltage"],"mockDefault":1},{"name":"resistance","quantity":"ElectricResistance","unit":"Ohm","access":"read","query":"R?","modes":["resistance"],"mockDefault":2}]}
    """;
    private static StatefulMockEngine Engine(ISimulationClock? clock=null)=>new(ImportedInstrumentDefinition.Parse(Definition),clock);
    private sealed class CallbackClock(Func<TimeSpan> read):ISimulationClock
    {
        /// <inheritdoc/>
        public TimeSpan Elapsed=>read();
    }

    /// <summary>New engines report zero pending faults.</summary>
    [Fact] public void InitialFaultCount()
    {
        var e=Engine(); Assert.Equal(0,e.FaultCount("voltage")); Assert.Empty(e.Snapshot().PendingFaultCounts);
    }

    /// <summary>Queued faults appear in cached diagnostics.</summary>
    [Fact] public void QueuedFaultCount()
    {
        var e=Engine(); e.QueueFault("voltage",new TimeoutException()); Assert.Equal(1,e.FaultCount("voltage")); Assert.Equal(1,e.Snapshot().PendingFaultCounts["voltage"]);
    }

    /// <summary>Fault counts remain capability scoped.</summary>
    [Fact] public void IndependentFaultCounts()
    {
        var e=Engine(); e.QueueFault("voltage",new TimeoutException()); e.QueueFault("resistance",new TimeoutException()); e.QueueFault("resistance",new TimeoutException()); Assert.Equal(1,e.FaultCount("voltage")); Assert.Equal(2,e.FaultCount("resistance"));
    }

    /// <summary>Fault-count inspection never samples virtual time.</summary>
    [Fact] public void FaultCountNoClock()
    {
        var e=Engine(new CallbackClock(()=>throw new InvalidOperationException())); e.QueueFault("voltage",new TimeoutException()); Assert.Equal(1,e.FaultCount("voltage"));
    }

    /// <summary>Unknown capabilities cannot acquire diagnostic entries.</summary>
    [Fact] public void UnknownFaultCount()
    {
        var e=Engine(); Assert.Throws<NotSupportedException>(()=>e.FaultCount("missing")); Assert.Empty(e.Snapshot().PendingFaultCounts);
    }

    /// <summary>Captured fault counts survive later queue mutation.</summary>
    [Fact] public void DetachedFaultCounts()
    {
        var e=Engine(); e.QueueFault("voltage",new TimeoutException()); var before=e.Snapshot(); e.ClearFaults("voltage"); Assert.Equal(1,before.PendingFaultCounts["voltage"]); Assert.Empty(e.Snapshot().PendingFaultCounts);
    }

    /// <summary>Snapshot fault maps reject external mutation.</summary>
    [Fact] public void ReadOnlyFaultCounts()
    {
        var e=Engine(); e.QueueFault("voltage",new TimeoutException()); var map=Assert.IsAssignableFrom<IDictionary<string,int>>(e.Snapshot().PendingFaultCounts); Assert.Throws<NotSupportedException>(()=>map.Clear());
    }

    /// <summary>Consumed fault queues disappear from nonempty diagnostics.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task ConsumedFaultExcluded()
    {
        var e=Engine(); await e.ConfigureModeAsync("voltage"); e.QueueFault("voltage",new TimeoutException()); await Assert.ThrowsAsync<TimeoutException>(async()=>await e.MeasureAsync("voltage")); Assert.Empty(e.Snapshot().PendingFaultCounts);
    }

    /// <summary>Default fallback is not reported as a configured model.</summary>
    [Fact] public void InitialModelKinds()
    {
        var e=Engine(); Assert.Empty(e.Snapshot().BehaviorKinds);
    }

    /// <summary>Synchronous callbacks are identified without execution.</summary>
    [Fact] public void SynchronousKind()
    {
        var e=Engine(); e.SetBehavior("voltage",_=>throw new InvalidOperationException()); Assert.Equal("Synchronous",e.Snapshot().BehaviorKinds["voltage"]);
    }

    /// <summary>Async callbacks are identified without execution.</summary>
    [Fact] public void AsynchronousKind()
    {
        var e=Engine(); e.SetAsyncBehavior("voltage",(_,_)=>throw new InvalidOperationException()); Assert.Equal("Asynchronous",e.Snapshot().BehaviorKinds["voltage"]);
    }

    /// <summary>Sequence diagnostics reveal kind without consuming values.</summary>
    [Fact] public void SequenceKind()
    {
        var e=Engine(); e.SetSequence("voltage",[3,4]); Assert.Equal("Sequence",e.Snapshot().BehaviorKinds["voltage"]); Assert.Equal(0,e.SequenceStatus("voltage")!.Position);
    }

    /// <summary>Replacing a model updates exactly its diagnostic kind.</summary>
    [Fact] public void ModelReplacementKind()
    {
        var e=Engine(); e.SetBehavior("voltage",_=>3); e.SetSequence("voltage",[4]); var s=e.Snapshot(); Assert.Single(s.BehaviorKinds); Assert.Equal("Sequence",s.BehaviorKinds["voltage"]);
    }

    /// <summary>Removing a behavior removes its diagnostic entry.</summary>
    [Fact] public void ModelRemovalKind()
    {
        var e=Engine(); e.SetBehavior("voltage",_=>3); e.SetBehavior("voltage",null); Assert.Empty(e.Snapshot().BehaviorKinds);
    }

    /// <summary>Captured model-kind maps survive replacement.</summary>
    [Fact] public void DetachedModelKinds()
    {
        var e=Engine(); e.SetBehavior("voltage",_=>3); var s=e.Snapshot(); e.SetSequence("voltage",[4]); Assert.Equal("Synchronous",s.BehaviorKinds["voltage"]);
    }

    /// <summary>Model-kind maps reject caller writes.</summary>
    [Fact] public void ReadOnlyModelKinds()
    {
        var e=Engine(); e.SetSequence("voltage",[3]); var map=Assert.IsAssignableFrom<IDictionary<string,string>>(e.Snapshot().BehaviorKinds); Assert.Throws<NotSupportedException>(()=>map["voltage"]="bad");
    }

    /// <summary>One snapshot samples the virtual clock once.</summary>
    [Fact] public void SnapshotClockOnce()
    {
        var count=0; var e=Engine(new CallbackClock(()=>{++count;return TimeSpan.Zero;})); _=e.Snapshot(); Assert.Equal(1,count);
    }

    /// <summary>Bulk fault clear returns the exact discarded total.</summary>
    [Fact] public void ClearAllFaultCount()
    {
        var e=Engine(); e.QueueFault("voltage",new TimeoutException()); e.QueueFault("resistance",new TimeoutException()); Assert.Equal(2,e.ClearAllFaults()); Assert.Empty(e.Snapshot().PendingFaultCounts);
    }

    /// <summary>Bulk fault clear changes revision once.</summary>
    [Fact] public void ClearAllFaultRevision()
    {
        var e=Engine(); e.QueueFault("voltage",new TimeoutException()); e.QueueFault("resistance",new TimeoutException()); var revision=e.Snapshot().Revision; e.ClearAllFaults(); Assert.Equal(revision+1,e.Snapshot().Revision);
    }

    /// <summary>Empty bulk fault clear leaves revision unchanged.</summary>
    [Fact] public void EmptyFaultClearRevision()
    {
        var e=Engine(); var revision=e.Snapshot().Revision; Assert.Equal(0,e.ClearAllFaults()); Assert.Equal(revision,e.Snapshot().Revision);
    }

    /// <summary>Bulk fault clear preserves mode and scalar overrides.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task FaultClearPreservesReadings()
    {
        var e=Engine(); await e.ConfigureModeAsync("voltage"); e.SetReading("voltage",7); e.QueueFault("voltage",new TimeoutException()); e.ClearAllFaults(); Assert.Equal("voltage",e.Snapshot().Mode); Assert.Equal(7,await e.MeasureAsync("voltage"));
    }

    /// <summary>Bulk fault clear preserves the sequence cursor.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task FaultClearPreservesSequence()
    {
        var e=Engine(); await e.ConfigureModeAsync("voltage"); e.SetSequence("voltage",[7,8]); await e.MeasureAsync("voltage"); e.QueueFault("voltage",new TimeoutException()); e.ClearAllFaults(); Assert.Equal(1,e.SequenceStatus("voltage")!.Position); Assert.Equal(8,await e.MeasureAsync("voltage"));
    }

    /// <summary>Bulk sequence clear reports both removed capabilities.</summary>
    [Fact] public void ClearAllSequenceCount()
    {
        var e=Engine(); e.SetSequence("voltage",[3]); e.SetSequence("resistance",[4]); Assert.Equal(2,e.ClearAllSequences()); Assert.Empty(e.Snapshot().SequencePositions);
    }

    /// <summary>Empty bulk sequence clear leaves revision unchanged.</summary>
    [Fact] public void EmptySequenceClearRevision()
    {
        var e=Engine(); var r=e.Snapshot().Revision; Assert.Equal(0,e.ClearAllSequences()); Assert.Equal(r,e.Snapshot().Revision);
    }
}
