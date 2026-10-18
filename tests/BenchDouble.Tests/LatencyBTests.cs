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
}
