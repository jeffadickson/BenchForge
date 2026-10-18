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

    /// <summary>Create bounded, unambiguous channel state from definition metadata.</summary>
    /// <param name="channelCount">Supported channel count, from one to 256.</param>
    /// <param name="parameters">One to 512 nonnull parameters with unique query/setter patterns.</param>
    /// <exception cref="ArgumentException">Metadata, templates, defaults or bounds are invalid.</exception>
    public DefinitionDrivenScpiMockSession(
        int channelCount, IEnumerable<MockScpiParameter> parameters)
    {
        if (channelCount < 1 || channelCount > 256)
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        ArgumentNullException.ThrowIfNull(parameters);
        _channelCount = channelCount;
        var items = parameters.Take(513).ToArray();
        if (items.Length == 0 || items.Length > 512)
            throw new ArgumentException("Instrument requires 1 to 512 parameters.", nameof(parameters));
        foreach (var item in items) ArgumentNullException.ThrowIfNull(item);
        if (items.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Duplicate parameter names.", nameof(parameters));

        _parameters = items.Select(p =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(p.Name);
            if (p.Name.Length > 128 || p.Name.Any(char.IsControl)) throw new ArgumentException("Invalid parameter name.");
            ArgumentException.ThrowIfNullOrWhiteSpace(p.Quantity);
            if (p.Quantity.Length > 128 || p.Quantity.Any(char.IsControl)) throw new ArgumentException("Invalid quantity identity.");
            ArgumentException.ThrowIfNullOrWhiteSpace(p.Unit);
            if (p.Unit.Length > 80 || p.Unit.Any(char.IsControl)) throw new ArgumentException("Invalid unit identity.");
            if (p.QueryTemplate is null && p.SetTemplate is null)
                throw new ArgumentException($"No commands defined for {p.Name}.");
            if ((p.Minimum is { } min && !double.IsFinite(min)) ||
                (p.Maximum is { } max && !double.IsFinite(max)) ||
                (p.Minimum is { } low && p.Maximum is { } high && low > high))
                throw new ArgumentException($"Invalid range for {p.Name}.");
            ValidateNumber(p, p.DefaultValue);
            return new CompiledParameter(p,
                Compile(p.QueryTemplate, false), Compile(p.SetTemplate, true));
        }).ToArray();
        foreach (var selectors in new[] { _parameters.Select(p => p.Query), _parameters.Select(p => p.Setter) })
        {
            var patterns = selectors.Where(p => p is not null).Select(p => p!.ToString()).ToArray();
            if (patterns.Distinct(StringComparer.Ordinal).Count() != patterns.Length)
                throw new ArgumentException("Ambiguous SCPI command templates.", nameof(parameters));
        }

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

    /// <inheritdoc/>
    public async ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Length > 4096) throw new ArgumentException("SCPI command exceeds 4096 characters.", nameof(command));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (item, match) = Resolve(command, true);
            int channel = ReadChannel(match);
            if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double number))
                throw new FormatException("Invalid numeric SCPI value.");
            ValidateNumber(item.Spec, number);
            _values[(item.Spec.Name, channel)] = number;
            Transcript.Add("W:" + command);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    public async ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Length > 4096) throw new ArgumentException("SCPI command exceeds 4096 characters.", nameof(command));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (item, match) = Resolve(command, false);
            int channel = ReadChannel(match);
            Transcript.Add("Q:" + command);
            return _values[(item.Spec.Name, channel)].ToString("G17", CultureInfo.InvariantCulture);
        }
        finally { _gate.Release(); }
    }

    private (CompiledParameter Parameter, Match Match) Resolve(string command, bool setter)
    {
        (CompiledParameter Parameter, Match Match)? selected = null;
        foreach (var item in _parameters)
        {
            var match = (setter ? item.Setter : item.Query)?.Match(command);
            if (match is not { Success: true }) continue;
            if (selected is not null) throw new InvalidOperationException("Ambiguous simulated SCPI command.");
            selected = (item, match);
        }
        return selected ?? throw new NotSupportedException("Mock does not implement the requested command.");
    }

    /// <summary>Copy accepted command history under the mock transaction lock.</summary>
    /// <returns>Read-only detached commands; never performs physical I/O.</returns>
    public IReadOnlyList<string> GetTranscriptSnapshot()
    {
        _gate.Wait();
        try { return Array.AsReadOnly(Transcript.ToArray()); }
        finally { _gate.Release(); }
    }

    private int ReadChannel(Match match)
    {
        var group = match.Groups["channel"];
        int channel = 1;
        if (group.Success && !int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out channel))
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid channel index.");
        if (channel < 1 || channel > _channelCount)
            throw new ArgumentOutOfRangeException(nameof(channel),
                $"Channel must be between 1 and {_channelCount}.");
        return channel;
    }

    private static Regex? Compile(string? template, bool setter)
    {
        if (template is null) return null;
        if (string.IsNullOrWhiteSpace(template) || template.Length > 4096 || !Regex.IsMatch(template, @"\A[A-Za-z0-9_:+? .{}-]+\z") ||
            template.Replace("{channel}", "", StringComparison.Ordinal)
                .Replace("{value}", "", StringComparison.Ordinal).Contains('{') ||
            template.Replace("{channel}", "", StringComparison.Ordinal)
                .Replace("{value}", "", StringComparison.Ordinal).Contains('}'))
            throw new ArgumentException("Unsupported SCPI template.");
        if (template.Split("{value}", StringSplitOptions.None).Length > 2 ||
            template.Split("{channel}", StringSplitOptions.None).Length > 2)
            throw new ArgumentException("SCPI placeholders must occur at most once.");

        if (setter != template.Contains("{value}", StringComparison.Ordinal))
            throw new ArgumentException("SCPI setter must contain {value}; query must not.");
        string pattern = Regex.Escape(template)
            .Replace(Regex.Escape("{channel}"), @"(?<channel>[0-9]+)", StringComparison.Ordinal)
            .Replace(Regex.Escape("{value}"), @"(?<value>\S+)", StringComparison.Ordinal);
        return new Regex("\\A" + pattern + "\\z",
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
