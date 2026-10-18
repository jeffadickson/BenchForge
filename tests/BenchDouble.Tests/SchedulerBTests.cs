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
}
