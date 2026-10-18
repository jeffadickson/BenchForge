using BenchDouble.Simulation;
using Xunit;
namespace BenchDouble.Tests;
/// <summary>Phase latency and index-addressed jitter regression tests.</summary>
public sealed class LatencyBTests
{
    private static SimulationLatencyStage Stage(long duration=100,long jitter=10,SimulationLatencyPhase phase=SimulationLatencyPhase.Integration)=>new(phase,TimeSpan.FromTicks(duration),TimeSpan.FromTicks(jitter));
    private static SimulationLatencyProfile Profile(params SimulationLatencyStage[] stages)=>new(42,stages);

    /// <summary>Empty latency is explicitly instantaneous.</summary>
    [Fact] public void Empty()
    {
        var p=Profile();Assert.Empty(p.Stages);Assert.Equal(TimeSpan.Zero,p.Sample(0).Total);
    }

    /// <summary>All seven phases remain distinct.</summary>
    [Fact] public void AllPhases()
    {
        var p=Profile(Enum.GetValues<SimulationLatencyPhase>().Select(v=>Stage(10,0,v)).ToArray());Assert.Equal(7,p.Stages.Count);Assert.Equal(70,p.Sample(0).Total.Ticks);
    }

    /// <summary>Over-limit phase enumeration is rejected.</summary>
    [Fact] public void TooMany()
    {
        Assert.Throws<ArgumentException>(()=>Profile(Enumerable.Repeat(Stage(),8).ToArray()));
    }

    /// <summary>Null phase enumerations fail explicitly.</summary>
    [Fact] public void NullSource()
    {
        Assert.Throws<ArgumentNullException>(()=>new SimulationLatencyProfile(0,null!));
    }

    /// <summary>Null phase entries are rejected.</summary>
    [Fact] public void NullStage()
    {
        Assert.Throws<ArgumentNullException>(()=>Profile(new SimulationLatencyStage[]{null!}));
    }

    /// <summary>Unknown phase identifiers are rejected.</summary>
    [Fact] public void UnknownPhase()
    {
        Assert.Throws<ArgumentException>(()=>Profile(Stage(10,0,(SimulationLatencyPhase)99)));
    }

    /// <summary>Duplicate phase identities are rejected.</summary>
    [Fact] public void Duplicate()
    {
        Assert.Throws<ArgumentException>(()=>Profile(Stage(),Stage()));
    }

    /// <summary>Negative base duration is rejected.</summary>
    [Fact] public void NegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>Profile(Stage(-1,0)));
    }

    /// <summary>Durations beyond one day are rejected.</summary>
    [Fact] public void OverDayDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>Profile(Stage(TimeSpan.TicksPerDay+1,0)));
    }

    /// <summary>Zero base duration remains exactly zero.</summary>
    [Fact] public void ZeroDuration()
    {
        Assert.Equal(TimeSpan.Zero,Profile(Stage(0,0)).Sample(4).Total);
    }

    /// <summary>Negative jitter radii are rejected.</summary>
    [Fact] public void NegativeJitter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>Profile(Stage(10,-1)));
    }

    /// <summary>Jitter cannot exceed base duration.</summary>
    [Fact] public void JitterBeyondBase()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>Profile(Stage(10,11)));
    }

    /// <summary>The upper phase bound cannot exceed one day.</summary>
    [Fact] public void UpperBeyondDay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>Profile(Stage(TimeSpan.TicksPerDay,1)));
    }

    /// <summary>Aggregate phase upper bounds remain bounded.</summary>
    [Fact] public void AggregateOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>Profile(Stage(TimeSpan.TicksPerDay,0),Stage(1,0,SimulationLatencyPhase.Display)));
    }

    /// <summary>The exact total day boundary is supported.</summary>
    [Fact] public void ExactDay()
    {
        Assert.Equal(TimeSpan.FromDays(1),Profile(Stage(TimeSpan.TicksPerDay,0)).Sample(0).Total);
    }

    /// <summary>Later caller array edits cannot rewire a profile.</summary>
    [Fact] public void CopiedInput()
    {
        var a=new[]{Stage(100,0)};var p=Profile(a);a[0]=Stage(200,0);Assert.Equal(100,p.Sample(0).Total.Ticks);
    }

    /// <summary>Profile phase collections reject caller mutation.</summary>
    [Fact] public void ReadOnlyProfile()
    {
        var p=Profile(Stage());Assert.Throws<NotSupportedException>(()=>((IList<SimulationLatencyStage>)p.Stages).Clear());
    }

    /// <summary>Profile phases use canonical enum order.</summary>
    [Fact] public void CanonicalOrder()
    {
        var p=Profile(Stage(10,0,SimulationLatencyPhase.Display),Stage(10,0,SimulationLatencyPhase.Trigger));Assert.Equal(SimulationLatencyPhase.Trigger,p.Stages[0].Phase);
    }

    /// <summary>Repeated seed and index return identical durations.</summary>
    [Fact] public void SameIndex()
    {
        var p=Profile(Stage());Assert.Equal(p.Sample(9).Stages,p.Sample(9).Stages);
    }

    /// <summary>Different seeds change a nonzero jitter sequence.</summary>
    [Fact] public void SeedVariation()
    {
        var a=new SimulationLatencyProfile(1,[Stage()]);var b=new SimulationLatencyProfile(2,[Stage()]);Assert.False(Enumerable.Range(0,32).Select(i=>a.Sample(i).Total.Ticks).SequenceEqual(Enumerable.Range(0,32).Select(i=>b.Sample(i).Total.Ticks)));
    }

    /// <summary>Different operation indices vary nonzero jitter.</summary>
    [Fact] public void IndexVariation()
    {
        var p=Profile(Stage());Assert.True(Enumerable.Range(0,64).Select(i=>p.Sample(i).Total.Ticks).Distinct().Count()>1);
    }

    /// <summary>Negative indices fail without changing later samples.</summary>
    [Fact] public void NegativeIndex()
    {
        var p=Profile(Stage());var before=p.Sample(0).Total;Assert.Throws<ArgumentOutOfRangeException>(()=>p.Sample(-1));Assert.Equal(before,p.Sample(0).Total);
    }

    /// <summary>Maximum operation indices remain finite and bounded.</summary>
    [Fact] public void MaximumIndex()
    {
        var s=Profile(Stage()).Sample(long.MaxValue);Assert.InRange(s.Total.Ticks,90,110);Assert.Equal(long.MaxValue,s.OperationIndex);
    }

    /// <summary>Jitter never leaves declared integer tick bounds.</summary>
    [Fact] public void Bounds()
    {
        var p=Profile(Stage());for(var i=0;i<4096;i++)Assert.InRange(p.Sample(i).Total.Ticks,90,110);
    }

    /// <summary>Zero jitter preserves every operation duration.</summary>
    [Fact] public void NoJitter()
    {
        var p=Profile(Stage(100,0));for(var i=0;i<64;i++)Assert.Equal(100,p.Sample(i).Total.Ticks);
    }

    /// <summary>Parallel identical-index samples are repeatable.</summary>
    [Fact] public void ParallelSameIndex()
    {
        var p=Profile(Stage());var expected=p.Sample(7).Total;Parallel.For(0,128,_=>Assert.Equal(expected,p.Sample(7).Total));
    }

    /// <summary>Parallel indexed sampling matches serial results exactly.</summary>
    [Fact] public void ParallelIndices()
    {
        var p=Profile(Stage());var expected=Enumerable.Range(0,128).Select(i=>p.Sample(i).Total).ToArray();var actual=new TimeSpan[128];Parallel.For(0,128,i=>actual[i]=p.Sample(i).Total);Assert.Equal(expected,actual);
    }

    /// <summary>Sample phase collections reject caller mutation.</summary>
    [Fact] public void ReadOnlySample()
    {
        var s=Profile(Stage()).Sample(0);Assert.Throws<NotSupportedException>(()=>((IList<SampledLatencyStage>)s.Stages).Clear());
    }

    /// <summary>Sample totals equal the exact tick sum of phases.</summary>
    [Fact] public void ExactTotal()
    {
        var s=Profile(Stage(),Stage(80,20,SimulationLatencyPhase.Display)).Sample(3);Assert.Equal(s.Stages.Sum(v=>v.Duration.Ticks),s.Total.Ticks);
    }

    /// <summary>Unconfigured phases report zero without sampling.</summary>
    [Fact] public void AbsentPhase()
    {
        Assert.Equal(TimeSpan.Zero,Profile(Stage()).Sample(0).DurationOf(SimulationLatencyPhase.Display));
    }

    /// <summary>Configured phases return their sampled duration.</summary>
    [Fact] public void PresentPhase()
    {
        var s=Profile(Stage()).Sample(0);Assert.Equal(s.Total,s.DurationOf(SimulationLatencyPhase.Integration));
    }

    /// <summary>Unknown phase lookups fail explicitly.</summary>
    [Fact] public void InvalidLookup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>Profile().Sample(0).DurationOf((SimulationLatencyPhase)(-1)));
    }

    /// <summary>Negative logical starts are rejected.</summary>
    [Fact] public void NegativeStart()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>Profile(Stage()).Sample(0).ReadyAt(TimeSpan.FromTicks(-1)));
    }

    /// <summary>Ready times use exact logical tick addition.</summary>
    [Fact] public void ExactReadiness()
    {
        Assert.Equal(TimeSpan.FromTicks(150),Profile(Stage(100,0)).Sample(0).ReadyAt(TimeSpan.FromTicks(50)));
    }

    /// <summary>Unrepresentable readiness fails explicitly.</summary>
    [Fact] public void ReadyOverflow()
    {
        Assert.Throws<OverflowException>(()=>Profile(Stage(1,0)).Sample(0).ReadyAt(TimeSpan.MaxValue));
    }

    /// <summary>Worst-case duration sums all configured phase bounds.</summary>
    [Fact] public void MaximumBound()
    {
        Assert.Equal(210,Profile(Stage(),Stage(80,20,SimulationLatencyPhase.Display)).MaximumDuration.Ticks);
    }

    /// <summary>Extreme signed seeds remain supported.</summary>
    [Fact] public void SignedSeed()
    {
        Assert.Equal(int.MinValue,new SimulationLatencyProfile(int.MinValue,[Stage()]).Sample(0).Seed);Assert.Equal(int.MaxValue,new SimulationLatencyProfile(int.MaxValue,[Stage()]).Sample(0).Seed);
    }

    /// <summary>Input phase order does not affect indexed samples.</summary>
    [Fact] public void ConfigurationOrder()
    {
        var a=Stage();var d=Stage(50,5,SimulationLatencyPhase.Display);Assert.Equal(Profile(a,d).Sample(9).Stages,Profile(d,a).Sample(9).Stages);
    }

    /// <summary>Out-of-order sampling does not mutate a random cursor.</summary>
    [Fact] public void NoRandomCursor()
    {
        var p=Profile(Stage());var s=p.Sample(5).Total;_=p.Sample(1);_=p.Sample(100);Assert.Equal(s,p.Sample(5).Total);
    }

    /// <summary>Oversized phase sources are not enumerated indefinitely.</summary>
    [Fact] public void BoundedEnumeration()
    {
        IEnumerable<SimulationLatencyStage> Values(){for(var i=0;i<8;i++)yield return Stage();throw new InvalidOperationException("over-enumerated");}Assert.Throws<ArgumentException>(()=>new SimulationLatencyProfile(0,Values()));
    }
}
