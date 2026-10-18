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
}
