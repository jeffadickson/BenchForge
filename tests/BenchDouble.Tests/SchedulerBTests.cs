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
}
