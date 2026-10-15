using System.Globalization;
using System.Text.RegularExpressions;
using BenchDouble.Async;

namespace BenchDouble.Simulation;

/// <summary>
/// One typed instrument-definition parameter. The generator supplies validated
/// SCPI templates, physical quantity identity, range and a finite default.
/// </summary>
public sealed record MockScpiParameter(
    string Name, string Quantity, string Unit, string? QueryTemplate,
    string? SetTemplate, double DefaultValue, double? Minimum, double? Maximum);

/// <summary>
/// Stateful SCPI session constructed from the same instrument metadata as the
/// generated typed driver. The mock parses only declared commands and never
/// silently guesses a quantity, measurement mode or unsupported command.
/// </summary>
public class DefinitionDrivenScpiMockSession : IAsyncScpiSession
{
    private sealed record CompiledParameter(
        MockScpiParameter Spec, Regex? Query, Regex? Setter);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string Name, int Channel), double> _values = new();
    private readonly CompiledParameter[] _parameters;
    private readonly int _channelCount;

    public DefinitionDrivenScpiMockSession(
        int channelCount, IEnumerable<MockScpiParameter> parameters)
    {
        if (channelCount < 1 || channelCount > 256)
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        ArgumentNullException.ThrowIfNull(parameters);
        _channelCount = channelCount;
        var items = parameters.ToArray();
        if (items.Length == 0 || items.Length > 512)
            throw new ArgumentException("Instrument requires 1 to 512 parameters.", nameof(parameters));
        if (items.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Duplicate parameter names.", nameof(parameters));

        _parameters = items.Select(p =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(p.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(p.Quantity);
            ArgumentException.ThrowIfNullOrWhiteSpace(p.Unit);
            ValidateNumber(p, p.DefaultValue);
            return new CompiledParameter(p,
                Compile(p.QueryTemplate, false), Compile(p.SetTemplate, true));
        }).ToArray();

        foreach (var item in items)
            for (var channel = 1; channel <= _channelCount; channel++)
                _values.Add((item.Name, channel), item.DefaultValue);
    }

    /// <summary>Recorded accepted SCPI commands, excluding any unsupported input.</summary>
    public List<string> Transcript { get; } = new();

    /// <summary>
    /// Configure a simulated reading by its schema parameter name. Generated
    /// SetMock* methods wrap this API with UnitsNet types for compile-time safety.
    /// </summary>
    protected void SetReading(string parameterName, int channel, double value)
    {
        if (channel < 1 || channel > _channelCount)
            throw new ArgumentOutOfRangeException(nameof(channel));
        var spec = _parameters.FirstOrDefault(p => p.Spec.Name == parameterName)?.Spec
            ?? throw new KeyNotFoundException($"Unknown parameter '{parameterName}'.");
        ValidateNumber(spec, value);
        _gate.Wait();
        try { _values[(parameterName, channel)] = value; }
        finally { _gate.Release(); }
    }

    public async ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var item in _parameters)
            {
                var match = item.Setter?.Match(command);
                if (match is not { Success: true }) continue;
                int channel = ReadChannel(match);
                if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double number))
                    throw new FormatException("Invalid numeric SCPI value.");
                ValidateNumber(item.Spec, number);
                _values[(item.Spec.Name, channel)] = number;
                Transcript.Add("W:" + command);
                return;
            }
            throw new NotSupportedException($"Mock does not implement command '{command}'.");
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var item in _parameters)
            {
                var match = item.Query?.Match(command);
                if (match is not { Success: true }) continue;
                int channel = ReadChannel(match);
                Transcript.Add("Q:" + command);
                return _values[(item.Spec.Name, channel)]
                    .ToString("G17", CultureInfo.InvariantCulture);
            }
            throw new NotSupportedException($"Mock does not implement query '{command}'.");
        }
        finally { _gate.Release(); }
    }

    private int ReadChannel(Match match)
    {
        var group = match.Groups["channel"];
        int channel = group.Success
            ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 1;
        if (channel < 1 || channel > _channelCount)
            throw new ArgumentOutOfRangeException(nameof(channel),
                $"Channel must be between 1 and {_channelCount}.");
        return channel;
    }

    private static Regex? Compile(string? template, bool setter)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        if (!Regex.IsMatch(template, @"^[A-Za-z0-9_:+? .{}-]+$") ||
            template.Replace("{channel}", "", StringComparison.Ordinal)
                .Replace("{value}", "", StringComparison.Ordinal).Contains('{') ||
            template.Replace("{channel}", "", StringComparison.Ordinal)
                .Replace("{value}", "", StringComparison.Ordinal).Contains('}'))
            throw new ArgumentException("Unsupported SCPI template.");

        if (setter != template.Contains("{value}", StringComparison.Ordinal))
            throw new ArgumentException("SCPI setter must contain {value}; query must not.");
        string pattern = Regex.Escape(template)
            .Replace(Regex.Escape("{channel}"), @"(?<channel>[0-9]+)", StringComparison.Ordinal)
            .Replace(Regex.Escape("{value}"), @"(?<value>\S+)", StringComparison.Ordinal);
        return new Regex("^" + pattern + "$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));
    }

    private static void ValidateNumber(MockScpiParameter parameter, double number)
    {
        if (!double.IsFinite(number) ||
            (parameter.Minimum is { } minimum && number < minimum) ||
            (parameter.Maximum is { } maximum && number > maximum))
            throw new ArgumentOutOfRangeException(nameof(number),
                $"Value is outside the declared range for {parameter.Name}.");
    }
}
