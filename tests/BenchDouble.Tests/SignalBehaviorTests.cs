using BenchDouble.Simulation;
using Xunit;

namespace BenchDouble.Tests;

public class SignalBehaviorTests
{
    [Fact]
    public void SineBehaviorProducesKnownSample()
    {
        var signal = new SineBehavior(2, 1, 5);
        Assert.Equal(5, signal.Evaluate(TimeSpan.Zero), 10);
        Assert.Equal(7, signal.Evaluate(TimeSpan.FromSeconds(0.25)), 10);
    }

    [Fact]
    public void RandomBehaviorIsSeedRepeatable()
    {
        var a = new SeededRandomBehavior(42, 4.9, 5.1);
        var b = new SeededRandomBehavior(42, 4.9, 5.1);
        for (int i = 0; i < 10; ++i) Assert.Equal(a.Evaluate(TimeSpan.Zero), b.Evaluate(TimeSpan.Zero));
    }

    [Fact]
    public void CompositeBehaviorSupportsSignalWithNoise()
    {
        var signal = new SumBehavior(new SineBehavior(1, 1), new SeededRandomBehavior(9, -0.1, 0.1));
        Assert.InRange(signal.Evaluate(TimeSpan.FromSeconds(0.25)), 0.9, 1.1);
    }
}
