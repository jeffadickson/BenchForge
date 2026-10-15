namespace BenchDouble.Simulation;

/// <summary>Produces a sample based on an explicit elapsed time, allowing deterministic simulation.</summary>
public interface ISignalBehavior { double Evaluate(TimeSpan elapsed); }

/// <summary>Deterministic sine-wave source with a user-selected phase and offset.</summary>
public sealed record SineBehavior(double Amplitude, double FrequencyHz, double Offset = 0, double PhaseRadians = 0) : ISignalBehavior
{
    public double Evaluate(TimeSpan elapsed) => Offset + Amplitude * Math.Sin(2 * Math.PI * FrequencyHz * elapsed.TotalSeconds + PhaseRadians);
}

/// <summary>Repeatable uniform noise; order matters. This is a mock behavior, not a calibrated noise model.</summary>
public sealed class SeededRandomBehavior(int seed, double minimum, double maximum) : ISignalBehavior
{
    private readonly Random _random = new(seed);
    public double Evaluate(TimeSpan elapsed)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        return minimum + _random.NextDouble() * (maximum - minimum);
    }
}

/// <summary>Composes deterministic signal and seeded noise without scattering logic across drivers.</summary>
public sealed class SumBehavior(params ISignalBehavior[] behaviors) : ISignalBehavior
{
    public double Evaluate(TimeSpan elapsed) => behaviors.Sum(b => b.Evaluate(elapsed));
}
