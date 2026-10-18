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
}
