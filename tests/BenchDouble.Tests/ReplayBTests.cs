using BenchDouble.Simulation;
using Xunit;
namespace BenchDouble.Tests;
/// <summary>Bounded standalone canonical transcript replay and concurrent consumption regressions.</summary>
public sealed class ReplayBTests
{
    private static TimeSpan T(long ticks)=>TimeSpan.FromTicks(ticks);
    private static SimulationReplaySample S(long ticks=0,double value=1)=>new("v","voltage","ElectricPotential","Volt",T(ticks),value);
    private static SimulationReplay R(params SimulationReplaySample[] samples)=>new(samples);

    /// <summary>Null replay plans are rejected.</summary>
    [Fact] public void NullPlan()
    {
        Assert.Throws<ArgumentNullException>(()=>new SimulationReplay(null!));
    }
}
