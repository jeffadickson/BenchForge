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

    /// <summary>Null transcript entries are rejected.</summary>
    [Fact] public void NullSample()
    {
        Assert.Throws<ArgumentException>(()=>R(new SimulationReplaySample[]{null!}));
    }

    /// <summary>Empty replay plans are explicitly exhausted.</summary>
    [Fact] public void EmptyPlan()
    {
        var r=R();Assert.Null(r.Peek());Assert.Equal(new SimulationReplayStatus(0,0,0),r.Status());Assert.Throws<InvalidOperationException>(()=>r.ReadNext("v","voltage",T(0)));
    }

    /// <summary>The supported transcript size boundary is accepted.</summary>
    [Fact] public void MaximumPlan()
    {
        var r=new SimulationReplay(Enumerable.Repeat(S(),4096));Assert.Equal(4096,r.Status().Length);
    }

    /// <summary>Excess transcript size is rejected.</summary>
    [Fact] public void ExcessPlan()
    {
        Assert.Throws<ArgumentException>(()=>new SimulationReplay(Enumerable.Repeat(S(),4097)));
    }

    /// <summary>Infinite transcript enumeration stops at the bounded rejection point.</summary>
    [Fact] public void BoundedEnumeration()
    {
        var reads=0;IEnumerable<SimulationReplaySample> Endless(){while(true){++reads;yield return S();}}Assert.Throws<ArgumentException>(()=>new SimulationReplay(Endless()));Assert.Equal(4097,reads);
    }

    /// <summary>Blank transcript modes cannot enter replay.</summary>
    [Fact] public void BlankMode()
    {
        Assert.Throws<ArgumentException>(()=>R(S() with{Mode=" "}));
    }

    /// <summary>Null transcript capability labels are rejected.</summary>
    [Fact] public void NullCapability()
    {
        Assert.Throws<ArgumentNullException>(()=>R(S() with{Capability=null!}));
    }

    /// <summary>Oversized canonical quantity labels are rejected.</summary>
    [Fact] public void LongQuantity()
    {
        Assert.Throws<ArgumentException>(()=>R(S() with{Quantity=new string('x',129)}));
    }

    /// <summary>Canonical unit label size is bounded.</summary>
    [Fact] public void LongUnit()
    {
        Assert.Throws<ArgumentException>(()=>R(S() with{Unit=new string('x',81)}));
    }

    /// <summary>Control characters in every transcript label are rejected.</summary>
    [Fact] public void ControlLabels()
    {
        foreach(var s in new[]{S() with{Mode="v\n"},S() with{Capability="x\t"},S() with{Quantity="x\r"},S() with{Unit="x\0"}})Assert.Throws<ArgumentException>(()=>R(s));
    }

    /// <summary>Exact supported label lengths are accepted.</summary>
    [Fact] public void LabelBounds()
    {
        var s=S() with{Mode=new string('m',128),Capability=new string('c',128),Quantity=new string('q',128),Unit=new string('u',80)};Assert.Equal(s,R(s).ReadNext(s.Mode,s.Capability,T(0)));
    }
}
