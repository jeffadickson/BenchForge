namespace BenchDouble.Declarative;

/// <summary>
/// Composable value validation for instrument parameters. Inspired by the
/// validator patterns used in PyMeasure, independently implemented for C#.
/// </summary>
public static class ParameterValidators
{
    /// <summary>Accept finite values inside inclusive bounds.</summary>
    public static Func<double, bool> InclusiveRange(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min > max)
            throw new ArgumentOutOfRangeException(nameof(min), "Range boundaries must be finite and ordered.");
        return value => double.IsFinite(value) && value >= min && value <= max;
    }

    /// <summary>Accept only the explicitly supported settings, such as NPLC values.</summary>
    public static Func<T, bool> Discrete<T>(params T[] allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        if (allowed.Length == 0) throw new ArgumentException("Allowed values must not be empty.", nameof(allowed));
        var values = new HashSet<T>(allowed);
        return value => values.Contains(value);
    }

    /// <summary>Compose several validators without reimplementing boilerplate.</summary>
    public static Func<T, bool> All<T>(params Func<T, bool>[] validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        if (validators.Any(v => v is null)) throw new ArgumentException("Validators must not contain null.", nameof(validators));
        return value => validators.All(check => check(value));
    }

    /// <summary>Accept values satisfying at least one policy.</summary>
    public static Func<T, bool> Any<T>(params Func<T, bool>[] validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        if (validators.Length == 0 || validators.Any(v => v is null))
            throw new ArgumentException("Provide at least one non-null validator.", nameof(validators));
        return value => validators.Any(check => check(value));
    }
}

/// <summary>
/// Encodes instruments' SCPI wire token sets, independent of C# enum names.
/// Examples include SIN/SQU/DC and IMM/EXT/BUS.
/// </summary>
public sealed class MappedCodec<T> : IScpiCodec<T> where T : notnull
{
    private readonly IReadOnlyDictionary<T, string> _outbound;
    private readonly IReadOnlyDictionary<string, T> _inbound;

    public MappedCodec(IEnumerable<KeyValuePair<T, string>> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var entries = mappings.ToArray();
        if (entries.Length == 0) throw new ArgumentException("At least one SCPI mapping is required.", nameof(mappings));
        if (entries.Any(item => string.IsNullOrWhiteSpace(item.Value)))
            throw new ArgumentException("SCPI tokens must be nonempty.", nameof(mappings));
        _outbound = entries.ToDictionary(item => item.Key, item => item.Value);
        _inbound = entries.ToDictionary(item => item.Value, item => item.Key, StringComparer.OrdinalIgnoreCase);
    }

    public string Format(T value) => _outbound.TryGetValue(value, out var token)
        ? token : throw new ArgumentOutOfRangeException(nameof(value), "Unsupported instrument value.");

    public T Parse(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return _inbound.TryGetValue(response.Trim(), out var result)
            ? result : throw new FormatException($"Unsupported SCPI token '{response}'.");
    }
}
