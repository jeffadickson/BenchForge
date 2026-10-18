using BenchDouble.Simulation;
using Xunit;
namespace BenchDouble.Tests;
/// <summary>Explicit virtual scheduler boundaries, isolation and concurrent queue regressions.</summary>
public sealed class SchedulerBTests
{
    private sealed class Clock:ISimulationClock
    {
        internal TimeSpan Now;internal int Reads;internal Action? OnRead;
        /// <inheritdoc/>
        public TimeSpan Elapsed{get{++Reads;OnRead?.Invoke();return Now;}}
    }
    private static TimeSpan T(long ticks)=>TimeSpan.FromTicks(ticks);

    /// <summary>Schedulers require an explicit run clock.</summary>
    [Fact] public void NullClock()
    {
        Assert.Throws<ArgumentNullException>(()=>new SimulationScheduler(null!));
    }

    /// <summary>Zero queue capacity is rejected.</summary>
    [Fact] public void ZeroCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>new SimulationScheduler(new Clock(),0));
    }

    /// <summary>Queue capacity is bounded at 4096.</summary>
    [Fact] public void ExcessCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>new SimulationScheduler(new Clock(),4097));
    }

    /// <summary>Both queue capacity boundaries are supported.</summary>
    [Fact] public void CapacityBounds()
    {
        Assert.Equal(1,new SimulationScheduler(new Clock(),1).RemainingCapacity);Assert.Equal(4096,new SimulationScheduler(new Clock(),4096).RemainingCapacity);
    }

    /// <summary>Blank event labels are rejected.</summary>
    [Fact] public void BlankLabel()
    {
        var s=new SimulationScheduler(new Clock());Assert.Throws<ArgumentException>(()=>s.Schedule(T(0)," "));Assert.Equal(0,s.Count);
    }

    /// <summary>Control characters cannot enter event labels.</summary>
    [Fact] public void ControlLabel()
    {
        var s=new SimulationScheduler(new Clock());Assert.Throws<ArgumentException>(()=>s.Schedule(T(0),"x\n"));
    }

    /// <summary>Oversized event labels are rejected.</summary>
    [Fact] public void LongLabel()
    {
        var s=new SimulationScheduler(new Clock());Assert.Throws<ArgumentException>(()=>s.Schedule(T(0),new string('x',129)));
    }

    /// <summary>Negative logical delays are rejected.</summary>
    [Fact] public void NegativeDelay()
    {
        var s=new SimulationScheduler(new Clock());Assert.Throws<ArgumentOutOfRangeException>(()=>s.Schedule(T(-1),"a"));
    }

    /// <summary>Zero-delay events are due immediately.</summary>
    [Fact] public void Immediate()
    {
        var s=new SimulationScheduler(new Clock());var e=s.Schedule(T(0),"a");Assert.Equal(e,Assert.Single(s.DrainDue()));
    }

    /// <summary>Events become due at the exact logical deadline.</summary>
    [Fact] public void DelayedBoundary()
    {
        var c=new Clock();var s=new SimulationScheduler(c);var e=s.Schedule(T(10),"a");c.Now=T(10);Assert.Equal(e,Assert.Single(s.DrainDue()));
    }

    /// <summary>Future events remain pending before their deadline.</summary>
    [Fact] public void BeforeDeadline()
    {
        var c=new Clock();var s=new SimulationScheduler(c);s.Schedule(T(10),"a");c.Now=T(9);Assert.Empty(s.DrainDue());Assert.Equal(1,s.Count);
    }

    /// <summary>Late pumping returns already due events.</summary>
    [Fact] public void LateDrain()
    {
        var c=new Clock();var s=new SimulationScheduler(c);s.Schedule(T(10),"a");c.Now=T(100);Assert.Single(s.DrainDue());
    }

    /// <summary>Equal deadlines retain accepted identifier order.</summary>
    [Fact] public void TieOrder()
    {
        var s=new SimulationScheduler(new Clock());var a=s.Schedule(T(0),"a");var d=s.Schedule(T(0),"d");Assert.Equal(new[]{a.Id,d.Id},s.DrainDue().Select(e=>e.Id));
    }

    /// <summary>Earlier deadlines drain before later accepted identifiers.</summary>
    [Fact] public void DeadlineOrder()
    {
        var c=new Clock();var s=new SimulationScheduler(c);s.Schedule(T(20),"later");s.Schedule(T(10),"earlier");c.Now=T(20);Assert.Equal(new[]{"earlier","later"},s.DrainDue().Select(e=>e.Label));
    }

    /// <summary>Cancellation removes exactly the selected event.</summary>
    [Fact] public void CancelPresent()
    {
        var s=new SimulationScheduler(new Clock());var a=s.Schedule(T(0),"a");s.Schedule(T(0),"b");Assert.True(s.Cancel(a.Id));Assert.Equal("b",Assert.Single(s.DrainDue()).Label);
    }

    /// <summary>Absent event cancellation leaves other events intact.</summary>
    [Fact] public void CancelAbsent()
    {
        var s=new SimulationScheduler(new Clock());s.Schedule(T(0),"a");Assert.False(s.Cancel(999));Assert.Equal(1,s.Count);
    }

    /// <summary>Nonpositive cancellation identifiers are rejected.</summary>
    [Fact] public void InvalidId()
    {
        var s=new SimulationScheduler(new Clock());Assert.Throws<ArgumentOutOfRangeException>(()=>s.Cancel(0));
    }

    /// <summary>Observed caller cancellation preserves the selected event.</summary>
    [Fact] public void CancelledCancel()
    {
        var s=new SimulationScheduler(new Clock());var e=s.Schedule(T(0),"a");using var c=new CancellationTokenSource();c.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>s.Cancel(e.Id,c.Token));Assert.Equal(1,s.Count);
    }

    /// <summary>Queue clear never reuses acceptance identifiers.</summary>
    [Fact] public void ClearIdentifiers()
    {
        var s=new SimulationScheduler(new Clock());var a=s.Schedule(T(0),"a");Assert.Equal(1,s.Clear());Assert.True(s.Schedule(T(0),"b").Id>a.Id);
    }

    /// <summary>Captured event snapshots survive later cancellation.</summary>
    [Fact] public void DetachedSnapshot()
    {
        var s=new SimulationScheduler(new Clock());var e=s.Schedule(T(0),"a");var before=s.Snapshot();s.Cancel(e.Id);Assert.Single(before);Assert.Empty(s.Snapshot());
    }

    /// <summary>Event snapshots reject caller mutation.</summary>
    [Fact] public void ReadOnlySnapshot()
    {
        var s=new SimulationScheduler(new Clock());s.Schedule(T(0),"a");Assert.Throws<NotSupportedException>(()=>((IList<SimulationScheduledEvent>)s.Snapshot()).Clear());
    }

    /// <summary>Peeking never reads the simulation clock.</summary>
    [Fact] public void PeekNoClock()
    {
        var c=new Clock();var s=new SimulationScheduler(c);var e=s.Schedule(T(0),"a");var reads=c.Reads;Assert.Equal(e,s.Peek());Assert.Equal(reads,c.Reads);
    }

    /// <summary>Counting events never reads the simulation clock.</summary>
    [Fact] public void CountNoClock()
    {
        var c=new Clock();var s=new SimulationScheduler(c);Assert.Equal(0,s.Count);Assert.Equal(0,c.Reads);
    }

    /// <summary>Capacity diagnostics follow queue mutation.</summary>
    [Fact] public void RemainingCapacity()
    {
        var s=new SimulationScheduler(new Clock(),2);var a=s.Schedule(T(0),"a");Assert.Equal(1,s.RemainingCapacity);s.Cancel(a.Id);Assert.Equal(2,s.RemainingCapacity);
    }

    /// <summary>Clock rollback rejects scheduling without consuming identifiers.</summary>
    [Fact] public void BackwardsSchedule()
    {
        var c=new Clock{Now=T(10)};var s=new SimulationScheduler(c);s.Schedule(T(0),"a");c.Now=T(9);Assert.Throws<InvalidOperationException>(()=>s.Schedule(T(0),"bad"));c.Now=T(10);Assert.Equal(2,s.Schedule(T(0),"b").Id);
    }

    /// <summary>Clock rollback never consumes pending events.</summary>
    [Fact] public void BackwardsDrain()
    {
        var c=new Clock{Now=T(10)};var s=new SimulationScheduler(c);s.Schedule(T(0),"a");c.Now=T(9);Assert.Throws<InvalidOperationException>(()=>s.DrainDue());Assert.Equal(1,s.Count);
    }

    /// <summary>Negative clock values cannot enter scheduling state.</summary>
    [Fact] public void NegativeClock()
    {
        var c=new Clock{Now=T(-1)};var s=new SimulationScheduler(c);Assert.Throws<InvalidOperationException>(()=>s.Schedule(T(0),"bad"));Assert.Equal(0,s.Count);
    }

    /// <summary>Deadline overflow leaves identifiers and events untouched.</summary>
    [Fact] public void DeadlineOverflow()
    {
        var c=new Clock{Now=TimeSpan.MaxValue};var s=new SimulationScheduler(c);Assert.Throws<OverflowException>(()=>s.Schedule(T(1),"bad"));Assert.Equal(0,s.Count);Assert.Equal(1,s.Schedule(T(0),"valid").Id);
    }

    /// <summary>Maximum representable absolute deadlines are supported.</summary>
    [Fact] public void MaximumDeadline()
    {
        var s=new SimulationScheduler(new Clock());Assert.Equal(TimeSpan.MaxValue,s.ScheduleAt(TimeSpan.MaxValue,"a").DueAt);
    }

    /// <summary>Cancellation from clock observation precedes event insertion.</summary>
    [Fact] public void CancellationDuringClock()
    {
        using var token=new CancellationTokenSource();var c=new Clock{OnRead=token.Cancel};var s=new SimulationScheduler(c);Assert.ThrowsAny<OperationCanceledException>(()=>s.Schedule(T(0),"bad",token.Token));Assert.Equal(0,s.Count);
    }

    /// <summary>Independent scheduler instances do not share queues or identifiers.</summary>
    [Fact] public void IndependentRuns()
    {
        var c=new Clock();var a=new SimulationScheduler(c);var d=new SimulationScheduler(c);Assert.Equal(1,a.Schedule(T(0),"a").Id);Assert.Equal(1,d.Schedule(T(0),"d").Id);a.Clear();Assert.Equal(1,d.Count);
    }

    /// <summary>Parallel scheduling assigns unique identifiers without lost events.</summary>
    [Fact] public void ParallelSchedule()
    {
        var s=new SimulationScheduler(new Clock());var ids=new System.Collections.Concurrent.ConcurrentBag<long>();Parallel.For(0,128,i=>ids.Add(s.Schedule(T(0),"e"+i).Id));Assert.Equal(128,s.Count);Assert.Equal(128,ids.Distinct().Count());Assert.Equal(Enumerable.Range(1,128).Select(i=>(long)i),s.DrainDue().Select(e=>e.Id));
    }

    /// <summary>Parallel cancellation removes only selected pending events.</summary>
    [Fact] public void ParallelCancel()
    {
        var s=new SimulationScheduler(new Clock());var ids=Enumerable.Range(0,64).Select(i=>s.Schedule(T(0),"e"+i).Id).ToArray();Parallel.For(0,32,i=>Assert.True(s.Cancel(ids[i])));Assert.Equal(ids.Skip(32),s.DrainDue().Select(e=>e.Id));
    }

    /// <summary>Clearing pending events does not sample virtual time.</summary>
    [Fact] public void ClearNoClock()
    {
        var c=new Clock();var s=new SimulationScheduler(c);s.Schedule(T(0),"a");var reads=c.Reads;s.Clear();Assert.Equal(reads,c.Reads);
    }

    /// <summary>Snapshots never advance or sample virtual time.</summary>
    [Fact] public void SnapshotNoClock()
    {
        var c=new Clock();var s=new SimulationScheduler(c);s.Schedule(T(0),"a");var reads=c.Reads;_=s.Snapshot();Assert.Equal(reads,c.Reads);
    }

    /// <summary>Cancelled draining preserves all pending events.</summary>
    [Fact] public void CancelledDrain()
    {
        var s=new SimulationScheduler(new Clock());s.Schedule(T(0),"a");using var c=new CancellationTokenSource();c.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>s.DrainDue(c.Token));Assert.Equal(1,s.Count);
    }

    /// <summary>Full queues reject insertion before clock observation.</summary>
    [Fact] public void CapacityFailureNoClock()
    {
        var c=new Clock();var s=new SimulationScheduler(c,1);s.Schedule(T(0),"a");var reads=c.Reads;Assert.Throws<InvalidOperationException>(()=>s.Schedule(T(0),"b"));Assert.Equal(reads,c.Reads);
    }

    /// <summary>Absolute deadlines before current time are rejected.</summary>
    [Fact] public void PastDeadline()
    {
        var s=new SimulationScheduler(new Clock{Now=T(5)});Assert.Throws<ArgumentOutOfRangeException>(()=>s.ScheduleAt(T(4),"bad"));Assert.Equal(0,s.Count);
    }
}
