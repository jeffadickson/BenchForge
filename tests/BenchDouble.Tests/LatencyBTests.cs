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
        Assert.Throws<ArgumentNullException>(()=>Profile(null!));
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
        var a=new SimulationLatencyProfile(1,[Stage()]);var b=new SimulationLatencyProfile(2,[Stage()]);Assert.NotEqual(Enumerable.Range(0,32).Select(i=>a.Sample(i).Total.Ticks),Enumerable.Range(0,32).Select(i=>b.Sample(i).Total.Ticks));
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
}
