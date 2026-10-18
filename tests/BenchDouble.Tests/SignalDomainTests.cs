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
}
