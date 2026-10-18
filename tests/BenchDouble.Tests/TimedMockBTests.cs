using BenchDouble.Simulation;
using BenchDouble.Generated;
using Xunit;
namespace BenchDouble.Tests;
/// <summary>Mode-aware opt-in scalar latency, cancellation and state preservation tests.</summary>
public sealed class TimedMockBTests
{
    private const string Definition="""
    {"schemaVersion":2,"identity":{"driverName":"TimedB"},"modes":[{"id":"v","selectionCommand":"CONF:V"},{"id":"r","selectionCommand":"CONF:R"}],"capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"V?","modes":["v"],"mockDefault":1},{"name":"current","quantity":"ElectricCurrent","unit":"Ampere","access":"read","query":"I?","modes":["v"],"mockDefault":2},{"name":"resistance","quantity":"ElectricResistance","unit":"Ohm","access":"read","query":"R?","modes":["r"],"mockDefault":3}]}
    """;
    private sealed class Clock:ISimulationClock
    {
        internal TimeSpan Now;internal Action? OnRead;internal bool Fail;
        /// <inheritdoc/>
        public TimeSpan Elapsed{get{OnRead?.Invoke();if(Fail)throw new IOException();return Now;}}
    }
    private static StatefulMockEngine Engine(ISimulationClock? clock=null)=>new(ImportedInstrumentDefinition.Parse(Definition),clock);
    private static SimulationLatencyProfile Profile(long ticks=10)=>new(7,[new(SimulationLatencyPhase.Acquisition,TimeSpan.FromTicks(ticks),TimeSpan.Zero)]);

    /// <summary>Unprepared scalar reads preserve existing behavior.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task UnpreparedCompatibility()
    {
        var e=Engine();await e.ConfigureModeAsync("v");Assert.Equal(1,await e.MeasureAsync("voltage"));
    }

    /// <summary>Preparation requires an explicitly compatible operating mode.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task NoMode()
    {
        var e=Engine();await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(),0));Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Incompatible preparation leaves mock revision unchanged.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task WrongMode()
    {
        var e=Engine();await e.ConfigureModeAsync("r");var r=e.Snapshot().Revision;await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(),0));Assert.Equal(r,e.Snapshot().Revision);
    }

    /// <summary>Unknown preparation capabilities are rejected.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task UnknownCapability()
    {
        var e=Engine();await Assert.ThrowsAsync<NotSupportedException>(async()=>await e.PrepareMeasurementAsync("missing",Profile(),0));
    }

    /// <summary>Null latency profiles are rejected before mutation.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task NullProfile()
    {
        var e=Engine();await Assert.ThrowsAsync<ArgumentNullException>(async()=>await e.PrepareMeasurementAsync("voltage",null!,0));
    }

    /// <summary>Negative preparation indices preserve prior state.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task NegativeIndex()
    {
        var e=Engine();await e.ConfigureModeAsync("v");await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(),-1));Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Preparation captures mode start and sampled deadline.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task PinnedTiming()
    {
        var c=new Clock{Now=TimeSpan.FromTicks(5)};var e=Engine(c);await e.ConfigureModeAsync("v");var p=await e.PrepareMeasurementAsync("voltage",Profile(),2);Assert.Equal("v",p.Mode);Assert.Equal(5,p.StartedAt.Ticks);Assert.Equal(15,p.ReadyAt.Ticks);Assert.Equal(2,p.Latency.OperationIndex);
    }

    /// <summary>Early reads leave preparation and readings intact.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task BeforeReady()
    {
        var e=Engine();await e.ConfigureModeAsync("v");await e.PrepareMeasurementAsync("voltage",Profile(),0);await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.MeasureAsync("voltage"));Assert.NotNull(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>The exact logical ready boundary permits scalar reads.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task ExactReady()
    {
        var c=new Clock();var e=Engine(c);await e.ConfigureModeAsync("v");await e.PrepareMeasurementAsync("voltage",Profile(),0);c.Now=TimeSpan.FromTicks(10);Assert.Equal(1,await e.MeasureAsync("voltage"));Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>A late scalar read consumes its preparation once.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task LateRead()
    {
        var c=new Clock();var e=Engine(c);await e.ConfigureModeAsync("v");await e.PrepareMeasurementAsync("voltage",Profile(),0);c.Now=TimeSpan.FromTicks(99);Assert.Equal(1,await e.MeasureAsync("voltage"));Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Zero-duration preparations are immediately ready.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task Instant()
    {
        var e=Engine();await e.ConfigureModeAsync("v");await e.PrepareMeasurementAsync("voltage",Profile(0),0);Assert.Equal(1,await e.MeasureAsync("voltage"));
    }

    /// <summary>Duplicate triggers do not replace an accepted operation.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task Duplicate()
    {
        var e=Engine();await e.ConfigureModeAsync("v");var p=await e.PrepareMeasurementAsync("voltage",Profile(),0);var r=e.Snapshot().Revision;await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(20),1));Assert.Equal(p,e.PreparedMeasurementStatus("voltage"));Assert.Equal(r,e.Snapshot().Revision);
    }

    /// <summary>Early timed reads preserve the sequence cursor.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task SequenceBeforeReady()
    {
        var e=Engine();await e.ConfigureModeAsync("v");e.SetSequence("voltage",[7,8]);await e.PrepareMeasurementAsync("voltage",Profile(),0);await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.MeasureAsync("voltage"));Assert.Equal(0,e.SequenceStatus("voltage")!.Position);
    }

    /// <summary>Successful timed reads advance sequence and preparation atomically.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task SequenceSuccess()
    {
        var c=new Clock();var e=Engine(c);await e.ConfigureModeAsync("v");e.SetSequence("voltage",[7,8]);await e.PrepareMeasurementAsync("voltage",Profile(),0);var r=e.Snapshot().Revision;c.Now=TimeSpan.FromTicks(10);Assert.Equal(7,await e.MeasureAsync("voltage"));Assert.Equal(1,e.SequenceStatus("voltage")!.Position);Assert.Equal(r+1,e.Snapshot().Revision);
    }

    /// <summary>Readiness checks precede one-shot injected faults.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task EarlyFault()
    {
        var e=Engine();await e.ConfigureModeAsync("v");e.QueueFault("voltage",new TimeoutException());await e.PrepareMeasurementAsync("voltage",Profile(),0);await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.MeasureAsync("voltage"));Assert.Equal(1,e.FaultCount("voltage"));
    }

    /// <summary>Injected faults at readiness preserve the accepted preparation.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task ReadyFault()
    {
        var c=new Clock();var e=Engine(c);await e.ConfigureModeAsync("v");e.QueueFault("voltage",new TimeoutException());await e.PrepareMeasurementAsync("voltage",Profile(),0);c.Now=TimeSpan.FromTicks(10);await Assert.ThrowsAsync<TimeoutException>(async()=>await e.MeasureAsync("voltage"));Assert.NotNull(e.PreparedMeasurementStatus("voltage"));Assert.Equal(1,await e.MeasureAsync("voltage"));
    }

    /// <summary>Precancelled preparation leaves revision unchanged.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task CancelledPrepare()
    {
        var e=Engine();await e.ConfigureModeAsync("v");var r=e.Snapshot().Revision;using var c=new CancellationTokenSource();c.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(),0,c.Token));Assert.Equal(r,e.Snapshot().Revision);
    }

    /// <summary>Cancellation during preparation clock read precedes insertion.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task ClockCancellation()
    {
        using var c=new CancellationTokenSource();var clock=new Clock{OnRead=c.Cancel};var e=Engine(clock);await e.ConfigureModeAsync("v");await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(),0,c.Token));Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Cancelled timed reads preserve prepared operations.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task CancelledRead()
    {
        var e=Engine();await e.ConfigureModeAsync("v");await e.PrepareMeasurementAsync("voltage",Profile(0),0);using var c=new CancellationTokenSource();c.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await e.MeasureAsync("voltage",c.Token));Assert.NotNull(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Callback cancellation preserves ready preparation and sequence state.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task CallbackCancellation()
    {
        var e=Engine();await e.ConfigureModeAsync("v");using var c=new CancellationTokenSource();e.SetBehavior("voltage",_=>{c.Cancel();return 7;});await e.PrepareMeasurementAsync("voltage",Profile(0),0);await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await e.MeasureAsync("voltage",c.Token));Assert.NotNull(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Nonfinite model output does not consume ready preparation.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task NonfiniteOutput()
    {
        var e=Engine();await e.ConfigureModeAsync("v");e.SetBehavior("voltage",_=>double.NaN);await e.PrepareMeasurementAsync("voltage",Profile(0),0);await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.MeasureAsync("voltage"));Assert.NotNull(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Negative preparation clock values are rejected.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task NegativeClock()
    {
        var e=Engine(new Clock{Now=TimeSpan.FromTicks(-1)});await e.ConfigureModeAsync("v");await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(),0));Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Readiness overflow preserves preparation and revision state.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task DeadlineOverflow()
    {
        var e=Engine(new Clock{Now=TimeSpan.MaxValue});await e.ConfigureModeAsync("v");var r=e.Snapshot().Revision;await Assert.ThrowsAsync<OverflowException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(),0));Assert.Equal(r,e.Snapshot().Revision);Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Clock rollback before the trigger time preserves the operation.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task ClockRollback()
    {
        var c=new Clock{Now=TimeSpan.FromTicks(5)};var e=Engine(c);await e.ConfigureModeAsync("v");await e.PrepareMeasurementAsync("voltage",Profile(),0);c.Now=TimeSpan.FromTicks(4);await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.MeasureAsync("voltage"));Assert.NotNull(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Real mode changes invalidate outstanding preparations.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task ModeChange()
    {
        var e=Engine();await e.ConfigureModeAsync("v");await e.PrepareMeasurementAsync("voltage",Profile(),0);await e.ConfigureModeAsync("r");Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Reapplying the same mode preserves outstanding preparation.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task IdempotentMode()
    {
        var e=Engine();await e.ConfigureModeAsync("v");var p=await e.PrepareMeasurementAsync("voltage",Profile(),0);await e.ConfigureModeAsync("v");Assert.Equal(p,e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Synchronous reset invalidates timed scalar state.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task SyncReset()
    {
        var e=Engine();await e.ConfigureModeAsync("v");await e.PrepareMeasurementAsync("voltage",Profile(),0);e.Reset();Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Asynchronous reset invalidates timed scalar state.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task AsyncReset()
    {
        var e=Engine();await e.ConfigureModeAsync("v");await e.PrepareMeasurementAsync("voltage",Profile(),0);await e.ResetAsync();Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }
}
