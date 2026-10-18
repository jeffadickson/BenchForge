namespace BenchDouble.Simulation;

/// <summary>Produces a sample based on an explicit elapsed time, allowing deterministic simulation.</summary>
public interface ISignalBehavior
{
    /// <summary>Evaluate one finite simulated scalar at a nonnegative run time.</summary>
    /// <param name="elapsed">Run-scoped logical elapsed time.</param>
    /// <returns>Finite scalar in the units agreed by the caller and model.</returns>
    double Evaluate(TimeSpan elapsed);
}

/// <summary>Deterministic sine-wave source with a user-selected phase and offset.</summary>
/// <param name="Amplitude">Finite signed scalar amplitude.</param>
/// <param name="FrequencyHz">Finite nonnegative frequency, in hertz.</param>
/// <param name="Offset">Finite DC scalar offset.</param>
/// <param name="PhaseRadians">Finite initial phase in radians.</param>
public sealed record SineBehavior(double Amplitude, double FrequencyHz, double Offset = 0, double PhaseRadians = 0) : ISignalBehavior
{
    /// <inheritdoc/>
    public double Evaluate(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (!double.IsFinite(Amplitude) || !double.IsFinite(FrequencyHz) || FrequencyHz < 0 || !double.IsFinite(Offset) || !double.IsFinite(PhaseRadians))
            throw new ArgumentException("Sine parameters must be finite and frequency nonnegative.");
        var cycles = FrequencyHz * elapsed.TotalSeconds;
        if (!double.IsFinite(cycles)) throw new InvalidOperationException("Sine phase exceeds the representable domain.");
        var angle = 2 * Math.PI * (cycles % 1) + Math.IEEERemainder(PhaseRadians, 2 * Math.PI);
        var value = Offset + Amplitude * Math.Sin(angle);
        if (!double.IsFinite(value)) throw new InvalidOperationException("Sine output is not finite.");
        return value;
    }
}

/// <summary>Repeatable uniform noise; order matters. This is a mock behavior, not a calibrated noise model.</summary>
/// <param name="seed">Run-owned pseudorandom seed.</param>
/// <param name="minimum">Finite inclusive lower scalar bound.</param>
/// <param name="maximum">Finite upper scalar bound, at least minimum.</param>
public sealed class SeededRandomBehavior(int seed, double minimum, double maximum) : ISignalBehavior
{
    private readonly Random _random = new(seed);
    private readonly object _gate = new();
    /// <inheritdoc/>
    public double Evaluate(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        double weight;
        lock (_gate) weight = _random.NextDouble();
        // Weighted endpoints avoid overflowing maximum - minimum for opposite extremes.
        return Math.Clamp((1 - weight) * minimum + weight * maximum, minimum, maximum);
    }
}

/// <summary>Composes deterministic signal and seeded noise without scattering logic across drivers.</summary>
/// <param name="behaviors">Nonnull component list copied at construction; components retain their own state.</param>
public sealed class SumBehavior(params ISignalBehavior[] behaviors) : ISignalBehavior
{
    private readonly ISignalBehavior[] _behaviors = Copy(behaviors);
    private static ISignalBehavior[] Copy(ISignalBehavior[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copied = values.ToArray();
        foreach (var item in copied) ArgumentNullException.ThrowIfNull(item);
        return copied;
    }
    /// <inheritdoc/>
    public double Evaluate(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        double result = 0;
        foreach (var behavior in _behaviors)
        {
            var value = behavior.Evaluate(elapsed);
            if (!double.IsFinite(value) || !double.IsFinite(result + value)) throw new InvalidOperationException("Composite signal is not finite.");
            result += value;
        }
        return result;
    }
}
