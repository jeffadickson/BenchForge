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
        internal int TimestampReads;
        internal DateTimeOffset Utc = DateTimeOffset.UnixEpoch;
        /// <inheritdoc/>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        /// <inheritdoc/>
        public override long GetTimestamp() { Interlocked.Increment(ref TimestampReads); return Volatile.Read(ref Ticks); }
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

    /// <summary>Invalid rates fail construction before the provider is sampled.</summary>
    /// <param name="speed">Unsupported scaling rate.</param>
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    [InlineData(0)] [InlineData(-1)] [InlineData(0.0001)] [InlineData(1000001)]
    public void InvalidInitialRates(double speed)
    {
        var provider=new Provider(); Assert.Throws<ArgumentOutOfRangeException>(()=>new AdjustableSimulationClock(provider,speed));
        Assert.Equal(0,provider.TimestampReads);
    }

    /// <summary>Rejected rate changes preserve elapsed time and the previous policy.</summary>
    /// <param name="speed">Unsupported scaling rate.</param>
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(0)] [InlineData(-1)] [InlineData(1000001)]
    public void InvalidRateChangeIsAtomic(double speed)
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider,2); clock.Start(); provider.Advance(TimeSpan.FromSeconds(1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>clock.SetSpeed(speed)); Assert.Equal(new SimulationClockStatus(TimeSpan.FromSeconds(2),SimulationClockMode.Accelerated,2),clock.Status());
    }

    /// <summary>A negative manual increment never changes existing time.</summary>
    [Fact] public void NegativeAdvanceIsAtomic()
    {
        var clock=new AdjustableSimulationClock(); clock.AdvanceBy(TimeSpan.FromTicks(7));
        Assert.Throws<ArgumentOutOfRangeException>(()=>clock.AdvanceBy(TimeSpan.FromTicks(-1))); Assert.Equal(TimeSpan.FromTicks(7),clock.Elapsed);
    }

    /// <summary>Running clocks reject explicit increments without stopping advancement.</summary>
    [Fact] public void RunningAdvanceIsRejected()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider); clock.Start();
        Assert.Throws<InvalidOperationException>(()=>clock.AdvanceBy(TimeSpan.Zero)); provider.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(TimeSpan.FromSeconds(1),clock.Elapsed);
    }

    /// <summary>Repeated start calls retain the original running epoch.</summary>
    [Fact] public void RepeatedStartPreservesEpoch()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider); clock.Start(); provider.Advance(TimeSpan.FromSeconds(2)); clock.Start();
        provider.Advance(TimeSpan.FromSeconds(3)); Assert.Equal(TimeSpan.FromSeconds(5),clock.Elapsed); Assert.Equal(SimulationClockMode.RealTime,clock.Status().Mode);
    }

    /// <summary>Paused durations are excluded when automatic advancement resumes.</summary>
    [Fact] public void ResumeExcludesPausedDuration()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider); clock.Start(); provider.Advance(TimeSpan.FromSeconds(2)); clock.Pause();
        provider.Advance(TimeSpan.FromDays(1)); clock.Start(); provider.Advance(TimeSpan.FromSeconds(3)); Assert.Equal(TimeSpan.FromSeconds(5),clock.Elapsed);
    }

    /// <summary>Switching to manual mode freezes automatic time before explicit advancement.</summary>
    [Fact] public void ManualSelectionFreezesTime()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider,2); clock.Start(); provider.Advance(TimeSpan.FromSeconds(2)); clock.UseManual();
        provider.Advance(TimeSpan.FromDays(1)); clock.AdvanceBy(TimeSpan.FromSeconds(3)); Assert.Equal(TimeSpan.FromSeconds(7),clock.Elapsed); Assert.Equal(SimulationClockMode.Manual,clock.Status().Mode);
    }

    /// <summary>Inclusive supported rates select the corresponding running mode.</summary>
    /// <param name="speed">Supported rate.</param>
    /// <param name="mode">Expected running mode.</param>
    [Theory] [InlineData(0.001,SimulationClockMode.Slowed)] [InlineData(1,SimulationClockMode.RealTime)] [InlineData(1000000,SimulationClockMode.Accelerated)]
    public void SupportedRateBoundaries(double speed,SimulationClockMode mode)
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider,speed); clock.Start(); provider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromTicks((long)(TimeSpan.TicksPerSecond*(decimal)speed)),clock.Elapsed); Assert.Equal(mode,clock.Status().Mode);
    }

    /// <summary>Changing speed while stopped neither samples timestamps nor advances logical time.</summary>
    [Fact] public void StoppedRateChangeDoesNotAdvance()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider); clock.AdvanceBy(TimeSpan.FromTicks(3)); clock.Pause(); clock.SetSpeed(2);
        Assert.Equal(TimeSpan.FromTicks(3),clock.Elapsed); Assert.Equal(0,provider.TimestampReads); Assert.Equal(SimulationClockMode.Paused,clock.Status().Mode);
    }

    /// <summary>Backwards provider time fails explicitly and leaves the prior observation recoverable.</summary>
    [Fact] public void BackwardsProviderCanRecover()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider); clock.Start(); provider.Ticks=10; Assert.Equal(TimeSpan.FromTicks(10),clock.Elapsed);
        provider.Ticks=9; Assert.Throws<InvalidOperationException>(()=>clock.Elapsed); provider.Ticks=11; Assert.Equal(TimeSpan.FromTicks(11),clock.Elapsed);
    }

    /// <summary>Manual overflow does not mutate the last representable logical time.</summary>
    [Fact] public void ManualOverflowIsAtomic()
    {
        var clock=new AdjustableSimulationClock(); clock.AdvanceBy(TimeSpan.MaxValue); Assert.Throws<OverflowException>(()=>clock.AdvanceBy(TimeSpan.FromTicks(1))); Assert.Equal(TimeSpan.MaxValue,clock.Elapsed);
    }

    /// <summary>Scaled overflow does not commit a new epoch or rate.</summary>
    [Fact] public void ScaledOverflowIsAtomic()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider,1000000); clock.Start(); provider.Ticks=long.MaxValue;
        Assert.Throws<OverflowException>(()=>clock.SetSpeed(1)); provider.Ticks=1;
        Assert.Equal(TimeSpan.FromTicks(1000000),clock.Elapsed); Assert.Equal(1000000,clock.Status().Speed);
    }

    /// <summary>Parallel manual increments are serialized without lost updates.</summary>
    [Fact] public void ParallelManualAdvancesAreExact()
    {
        var clock=new AdjustableSimulationClock(); Parallel.For(0,10000,_=>clock.AdvanceBy(TimeSpan.FromTicks(1))); Assert.Equal(TimeSpan.FromTicks(10000),clock.Elapsed);
    }

    /// <summary>Concurrent controls and status reads preserve coherent nonnegative snapshots.</summary>
    [Fact] public void ConcurrentControlsRemainCoherent()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider);
        Parallel.For(0,2000,i=>{switch(i%5){case 0:clock.Start();break;case 1:clock.Pause();break;case 2:clock.UseManual();break;case 3:clock.SetSpeed(i%2==0?0.5:2);break;default:var status=clock.Status();Assert.True(status.Elapsed>=TimeSpan.Zero);Assert.Contains(status.Speed,new[]{0.5,1.0,2.0});break;}});
        Assert.Equal(TimeSpan.Zero,clock.Elapsed);
    }

    /// <summary>Wall-clock UTC jumps do not affect monotonic logical time.</summary>
    [Fact] public void UtcJumpsAreIgnored()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider); clock.Start(); provider.Utc=DateTimeOffset.MaxValue; provider.Ticks=3;
        Assert.Equal(TimeSpan.FromTicks(3),clock.Elapsed); provider.Utc=DateTimeOffset.MinValue; provider.Ticks=4; Assert.Equal(TimeSpan.FromTicks(4),clock.Elapsed); Assert.Equal(0,provider.UtcReads);
    }

    /// <summary>Previously returned snapshots stay detached from subsequent controls.</summary>
    [Fact] public void StatusIsDetached()
    {
        var clock=new AdjustableSimulationClock(); var status=clock.Status(); clock.AdvanceBy(TimeSpan.FromSeconds(2)); clock.SetSpeed(2);
        Assert.Equal(new SimulationClockStatus(TimeSpan.Zero,SimulationClockMode.Manual,1),status);
    }

    /// <summary>Equal logical times produce equal model values regardless of pacing rate.</summary>
    [Fact] public void EqualLogicalTimesAreRateIndependent()
    {
        var firstProvider=new Provider(); var secondProvider=new Provider(); var first=new AdjustableSimulationClock(firstProvider,2); var second=new AdjustableSimulationClock(secondProvider,0.5);
        first.Start(); second.Start(); firstProvider.Advance(TimeSpan.FromSeconds(1)); secondProvider.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(first.Elapsed,second.Elapsed); Assert.Equal(Math.Sin(first.Elapsed.TotalSeconds),Math.Sin(second.Elapsed.TotalSeconds));
    }

    /// <summary>All stopped observation APIs avoid querying the provider timestamp.</summary>
    [Fact] public void StoppedReadsDoNotSampleProvider()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider); for(var i=0;i<100;i++){_ = clock.Elapsed;clock.Status();} Assert.Equal(0,provider.TimestampReads);
    }

    /// <summary>A rate change rejected for backwards time preserves the prior running rate.</summary>
    [Fact] public void BackwardsRateChangeIsAtomic()
    {
        var provider=new Provider(); var clock=new AdjustableSimulationClock(provider,2); clock.Start(); provider.Ticks=10; _=clock.Elapsed; provider.Ticks=9;
        Assert.Throws<InvalidOperationException>(()=>clock.SetSpeed(3)); provider.Ticks=11; Assert.Equal(TimeSpan.FromTicks(22),clock.Elapsed); Assert.Equal(2,clock.Status().Speed);
    }

    private sealed class InvalidFrequencyProvider : TimeProvider
    {
        /// <inheritdoc/>
        public override long TimestampFrequency => 0;
    }
    /// <summary>A nonpositive timestamp frequency is rejected during construction.</summary>
    [Fact] public void InvalidProviderFrequencyIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(()=>new AdjustableSimulationClock(new InvalidFrequencyProvider()));
}
