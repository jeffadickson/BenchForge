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

    /// <summary>Negative recorded logical timestamps are rejected.</summary>
    [Fact] public void NegativeTime()
    {
        Assert.Throws<ArgumentException>(()=>R(S(-1)));
    }

    /// <summary>Backwards transcript timestamps are rejected.</summary>
    [Fact] public void BackwardsTime()
    {
        Assert.Throws<ArgumentException>(()=>R(S(10),S(9)));
    }

    /// <summary>Equal-time samples preserve explicit recording order.</summary>
    [Fact] public void EqualTime()
    {
        var r=R(S(0,1),S(0,2));Assert.Equal(1,r.ReadNext("v","voltage",T(0)).Value);Assert.Equal(2,r.ReadNext("v","voltage",T(0)).Value);
    }

    /// <summary>Every nonfinite replay scalar value is rejected.</summary>
    [Fact] public void Nonfinite()
    {
        foreach(var v in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})Assert.Throws<ArgumentException>(()=>R(S(0,v)));
    }

    /// <summary>Finite extreme replay values preserve their original values.</summary>
    [Fact] public void ExtremeValues()
    {
        foreach(var v in new[]{double.MaxValue,double.MinValue,0d,-1d})Assert.Equal(v,R(S(0,v)).ReadNext("v","voltage",T(0)).Value);
    }

    /// <summary>Replay plans are isolated from caller collection replacement.</summary>
    [Fact] public void CopiedPlan()
    {
        var samples=new[]{S(0,1)};var r=new SimulationReplay(samples);samples[0]=S(0,99);Assert.Equal(1,r.ReadNext("v","voltage",T(0)).Value);
    }

    /// <summary>A matching read returns the complete canonical sample.</summary>
    [Fact] public void ExactRead()
    {
        var s=S(7,8);var r=R(s);Assert.Equal(s,r.ReadNext("v","voltage",T(7)));Assert.Equal(new SimulationReplayStatus(1,1,0),r.Status());
    }

    /// <summary>Mismatched replay modes preserve the next sample.</summary>
    [Fact] public void ModeMismatch()
    {
        var r=R(S());Assert.Throws<InvalidOperationException>(()=>r.ReadNext("r","voltage",T(0)));Assert.Equal(0,r.Status().Position);Assert.Equal(S(),r.ReadNext("v","voltage",T(0)));
    }

    /// <summary>Mismatched replay capabilities preserve the next sample.</summary>
    [Fact] public void CapabilityMismatch()
    {
        var r=R(S());Assert.Throws<InvalidOperationException>(()=>r.ReadNext("v","current",T(0)));Assert.Equal(0,r.Status().Position);
    }

    /// <summary>Early logical timestamps never consume replay samples.</summary>
    [Fact] public void EarlyRead()
    {
        var r=R(S(10));Assert.Throws<InvalidOperationException>(()=>r.ReadNext("v","voltage",T(9)));Assert.Equal(0,r.Status().Position);
    }

    /// <summary>Late logical timestamps do not silently reschedule replay.</summary>
    [Fact] public void LateRead()
    {
        var r=R(S(10));Assert.Throws<InvalidOperationException>(()=>r.ReadNext("v","voltage",T(11)));Assert.Equal(0,r.Status().Position);
    }

    /// <summary>Replay mode and capability comparisons are ordinal and case-sensitive.</summary>
    [Fact] public void OrdinalLabels()
    {
        var r=R(S());Assert.Throws<InvalidOperationException>(()=>r.ReadNext("V","voltage",T(0)));Assert.Throws<InvalidOperationException>(()=>r.ReadNext("v","Voltage",T(0)));Assert.Equal(0,r.Status().Position);
    }

    /// <summary>Negative requested timestamps preserve the replay cursor.</summary>
    [Fact] public void NegativeReadTime()
    {
        var r=R(S());Assert.Throws<ArgumentOutOfRangeException>(()=>r.ReadNext("v","voltage",T(-1)));Assert.Equal(0,r.Status().Position);
    }

    /// <summary>Invalid requested labels preserve the replay cursor.</summary>
    [Fact] public void InvalidReadLabel()
    {
        var r=R(S());Assert.Throws<ArgumentException>(()=>r.ReadNext(" ","voltage",T(0)));Assert.Throws<ArgumentException>(()=>r.ReadNext("v","x\n",T(0)));Assert.Equal(0,r.Status().Position);
    }

    /// <summary>Observed cancellation preserves the next replay sample.</summary>
    [Fact] public void CancelledRead()
    {
        var r=R(S());using var c=new CancellationTokenSource();c.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>r.ReadNext("v","voltage",T(0),c.Token));Assert.Equal(0,r.Status().Position);
    }

    /// <summary>Exhausted transcripts reject extra reads without cursor drift.</summary>
    [Fact] public void Exhaustion()
    {
        var r=R(S());r.ReadNext("v","voltage",T(0));Assert.Throws<InvalidOperationException>(()=>r.ReadNext("v","voltage",T(0)));Assert.Equal(new SimulationReplayStatus(1,1,0),r.Status());
    }

    /// <summary>Repeated peeking preserves cursor and next sample.</summary>
    [Fact] public void PeekStable()
    {
        var s=S();var r=R(s);Assert.Equal(s,r.Peek());Assert.Equal(s,r.Peek());Assert.Equal(0,r.Status().Position);
    }

    /// <summary>Full transcript snapshots reject caller mutation.</summary>
    [Fact] public void SnapshotReadOnly()
    {
        var r=R(S());Assert.Throws<NotSupportedException>(()=>((IList<SimulationReplaySample>)r.Snapshot()).Clear());
    }

    /// <summary>Remaining transcript snapshots reject caller mutation.</summary>
    [Fact] public void RemainingReadOnly()
    {
        var r=R(S());Assert.Throws<NotSupportedException>(()=>((IList<SimulationReplaySample>)r.RemainingSamples()).Clear());
    }

    /// <summary>Captured remaining samples survive later cursor changes.</summary>
    [Fact] public void RemainingDetached()
    {
        var r=R(S(0,1),S(1,2));var old=r.RemainingSamples();r.ReadNext("v","voltage",T(0));Assert.Equal(2,old.Count);Assert.Equal(2,Assert.Single(r.RemainingSamples()).Value);
    }
}
