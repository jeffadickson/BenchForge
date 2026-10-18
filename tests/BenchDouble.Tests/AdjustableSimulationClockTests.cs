using BenchDouble.Simulation;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Deterministic clock-mode tests using a monotonic provider without real sleeps.</summary>
public sealed class AdjustableSimulationClockTests
{
    private sealed class Provider : TimeProvider
    {
        internal long Ticks;
        internal int TimerCalls;
        internal int UtcReads;
        internal DateTimeOffset Utc = DateTimeOffset.UnixEpoch;
        /// <inheritdoc/>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        /// <inheritdoc/>
        public override long GetTimestamp() => Volatile.Read(ref Ticks);
        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() { ++UtcReads; return Utc; }
        /// <inheritdoc/>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { ++TimerCalls; throw new InvalidOperationException("Clock must not create timers."); }
        internal void Advance(TimeSpan duration) => Interlocked.Add(ref Ticks,duration.Ticks);
    }

    /// <summary>Manual time does not advance with the provider, and paused time remains fixed.</summary>
    [Fact] public void ManualAndPausedModesAreExplicit()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider);
        Assert.Equal(SimulationClockMode.Manual,clock.Status().Mode); provider.Advance(TimeSpan.FromHours(1)); Assert.Equal(TimeSpan.Zero,clock.Elapsed);
        clock.AdvanceBy(TimeSpan.FromSeconds(3)); clock.Pause(); provider.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(3),clock.Elapsed); Assert.Equal(SimulationClockMode.Paused,clock.Status().Mode);
        clock.UseManual(); clock.AdvanceBy(TimeSpan.FromSeconds(2)); Assert.Equal(TimeSpan.FromSeconds(5),clock.Elapsed);
    }

    /// <summary>Speed changes preserve prior elapsed time and affect only subsequent monotonic duration.</summary>
    [Fact] public void SpeedChangesAreContinuous()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider); clock.Start(); provider.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2),clock.Elapsed); clock.SetSpeed(3); provider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(5),clock.Elapsed); Assert.Equal(SimulationClockMode.Accelerated,clock.Status().Mode);
        clock.SetSpeed(0.5); provider.Advance(TimeSpan.FromSeconds(2)); Assert.Equal(TimeSpan.FromSeconds(6),clock.Elapsed); Assert.Equal(SimulationClockMode.Slowed,clock.Status().Mode);
    }

    /// <summary>Sub-tick scaled time survives pause/resume and rate changes.</summary>
    [Fact] public void FractionalTicksAreRetained()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider,0.5); clock.Start(); provider.Advance(TimeSpan.FromTicks(1)); clock.Pause();
        Assert.Equal(TimeSpan.Zero,clock.Elapsed); clock.Start(); provider.Advance(TimeSpan.FromTicks(1)); Assert.Equal(TimeSpan.FromTicks(1),clock.Elapsed);
        clock.SetSpeed(1.5); provider.Advance(TimeSpan.FromTicks(2)); Assert.Equal(TimeSpan.FromTicks(4),clock.Elapsed);
    }

    /// <summary>Clock control never creates timers or consults wall-clock UTC.</summary>
    [Fact] public void ClockModesDoNotOwnTimersOrReadUtc()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider,60); clock.Start(); provider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromMinutes(1),clock.Elapsed); clock.Pause(); clock.UseManual(); clock.AdvanceBy(TimeSpan.FromSeconds(1)); clock.Status();
        Assert.Equal(0,provider.TimerCalls); Assert.Equal(0,provider.UtcReads);
    }
}
