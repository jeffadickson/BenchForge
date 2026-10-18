using BenchDouble.Simulation;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Finite signal-domain and composition isolation regressions for TR-043 and TR-049.</summary>
public sealed class SignalDomainTests
{
    private sealed class Constant(double value) : ISignalBehavior
    {
        /// <inheritdoc/>
        public double Evaluate(TimeSpan elapsed) => value;
    }
    private sealed class Probe(Func<double> action) : ISignalBehavior
    {
        internal int Calls;
        /// <inheritdoc/>
        public double Evaluate(TimeSpan elapsed) { ++Calls; return action(); }
    }
    /// <summary>Noise interpolation stays finite across opposite double extremes.</summary>
    [Fact] public void ExtremeNoiseRemainsFinite()
    {
        var first = new SeededRandomBehavior(42,-double.MaxValue,double.MaxValue);
        var second = new SeededRandomBehavior(42,-double.MaxValue,double.MaxValue);
        for (int i=0;i<128;i++) { var value=first.Evaluate(TimeSpan.Zero); Assert.True(double.IsFinite(value)); Assert.Equal(value,second.Evaluate(TimeSpan.Zero)); }
    }
    /// <summary>Unrepresentable sine outputs fail explicitly instead of publishing infinity.</summary>
    [Fact] public void SineRejectsUnrepresentableOutput()
    {
        Assert.Throws<InvalidOperationException>(() => new SineBehavior(double.MaxValue,1,double.MaxValue).Evaluate(TimeSpan.FromSeconds(0.25)));
        Assert.Throws<ArgumentException>(() => new SineBehavior(1,double.NaN).Evaluate(TimeSpan.Zero));
    }
    /// <summary>Composite membership is independent of caller array mutation and every result stays finite.</summary>
    [Fact] public void CompositeMembershipAndOutputAreValidated()
    {
        ISignalBehavior[] values=[new Constant(1)]; var sum=new SumBehavior(values); values[0]=new Constant(99);
        Assert.Equal(1,sum.Evaluate(TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => new SumBehavior(new Constant(double.NaN)).Evaluate(TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => new SumBehavior(new Constant(double.MaxValue),new Constant(double.MaxValue)).Evaluate(TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new SumBehavior(null!));
    }

    /// <summary>Concurrent seeded draws preserve the exact serial sample multiset without corrupting Random state.</summary>
    [Fact] public void ParallelSeededDrawsMatchSerialSamples()
    {
        var serial=new SeededRandomBehavior(123,-10,10); var parallel=new SeededRandomBehavior(123,-10,10);
        var expected=Enumerable.Range(0,128).Select(_=>serial.Evaluate(TimeSpan.Zero)).Order().ToArray();
        var actual=new System.Collections.Concurrent.ConcurrentBag<double>();
        Parallel.For(0,128,new ParallelOptions{MaxDegreeOfParallelism=8},_=>actual.Add(parallel.Evaluate(TimeSpan.Zero)));
        Assert.Equal(expected,actual.Order());
    }

    /// <summary>Rejected noise requests do not consume seeded state, and invalid bounds fail explicitly.</summary>
    [Fact] public void NoiseDomainRejectionPreservesSeededState()
    {
        var signal=new SeededRandomBehavior(42,-1,1); var reference=new SeededRandomBehavior(42,-1,1);
        Assert.Throws<ArgumentOutOfRangeException>(()=>signal.Evaluate(TimeSpan.FromTicks(-1)));
        Assert.Equal(reference.Evaluate(TimeSpan.Zero),signal.Evaluate(TimeSpan.Zero));
        foreach(var bounds in new[]{(double.NaN,1d),(0d,double.PositiveInfinity),(2d,1d)})
            Assert.Throws<ArgumentOutOfRangeException>(()=>new SeededRandomBehavior(42,bounds.Item1,bounds.Item2).Evaluate(TimeSpan.Zero));
        Assert.Equal(double.MaxValue,new SeededRandomBehavior(42,double.MaxValue,double.MaxValue).Evaluate(TimeSpan.Zero));
    }

    /// <summary>Sine input checks preserve valid phase, periodicity and signed-amplitude behavior.</summary>
    [Fact] public void SineDomainAndPeriodicityAreExplicit()
    {
        foreach(var signal in new[]{new SineBehavior(double.NaN,1),new SineBehavior(1,double.PositiveInfinity),new SineBehavior(1,-1),new SineBehavior(1,1,double.NaN),new SineBehavior(1,1,0,double.PositiveInfinity)})
            Assert.Throws<ArgumentException>(()=>signal.Evaluate(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new SineBehavior(1,1).Evaluate(TimeSpan.FromTicks(-1)));
        Assert.Throws<InvalidOperationException>(()=>new SineBehavior(1,double.MaxValue).Evaluate(TimeSpan.FromSeconds(2)));
        var periodic=new SineBehavior(2,1,5); Assert.Equal(periodic.Evaluate(TimeSpan.FromSeconds(0.25)),periodic.Evaluate(TimeSpan.FromSeconds(128.25)));
        Assert.Equal(3,new SineBehavior(-2,1,5).Evaluate(TimeSpan.FromSeconds(0.25)),10);
        Assert.InRange(new SineBehavior(1,0,2,double.MaxValue).Evaluate(TimeSpan.Zero),1,3);
    }

    /// <summary>Invalid or failed earlier components never consume later stateful components.</summary>
    [Fact] public void CompositeFailureStopsBeforeLaterComponents()
    {
        var later=new Probe(()=>7);
        Assert.Throws<InvalidOperationException>(()=>new SumBehavior(new Constant(double.NaN),later).Evaluate(TimeSpan.Zero)); Assert.Equal(0,later.Calls);
        var original=new IOException("provider"); Assert.Same(original,Assert.Throws<IOException>(()=>new SumBehavior(new Probe(()=>throw original),later).Evaluate(TimeSpan.Zero))); Assert.Equal(0,later.Calls);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new SumBehavior(later).Evaluate(TimeSpan.FromTicks(-1))); Assert.Equal(0,later.Calls);
        Assert.Equal(0,new SumBehavior().Evaluate(TimeSpan.Zero));
    }
}
