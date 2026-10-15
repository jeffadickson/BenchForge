using System.Globalization;
using System.Text.RegularExpressions;

namespace BenchDouble.Declarative;

/// <summary>Public metadata for a parameter exposed by a SCPI instrument or channel.</summary>
public sealed record ParameterMetadata(string Name, string Description, string? Unit = null,
    double? Minimum = null, double? Maximum = null, bool Readable = true, bool Writable = true);

/// <summary>Converts a typed value to and from SCPI wire representations.</summary>
public interface IScpiCodec<T>
{
    string Format(T value);
    T Parse(string response);
}

/// <summary>Culture-invariant floating-point SCPI encoding.</summary>
public sealed class DoubleCodec : IScpiCodec<double>
{
    public static DoubleCodec Instance { get; } = new();
    public string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    public double Parse(string response) => double.Parse(response.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>Wire codec for SCPI ON/OFF and 1/0 values.</summary>
public sealed class BooleanCodec : IScpiCodec<bool>
{
    public static BooleanCodec Instance { get; } = new();
    public string Format(bool value) => value ? "ON" : "OFF";
    public bool Parse(string response) => response.Trim().ToUpperInvariant() switch
    {
        "1" or "ON" => true,
        "0" or "OFF" => false,
        _ => throw new FormatException($"Invalid SCPI Boolean: {response}")
    };
}

/// <summary>
/// Immutable parameter descriptor. Resolve creates an object bound to a specific
/// instrument channel and session without exposing transport concerns to callers.
/// </summary>
public sealed class ScpiParameterDefinition<T>
{
    public ParameterMetadata Metadata { get; }
    public string? QueryTemplate { get; }
    public string? SetTemplate { get; }
    public IScpiCodec<T> Codec { get; }
    public Func<T, bool>? Validator { get; }

    internal ScpiParameterDefinition(ParameterMetadata metadata, string? queryTemplate,
        string? setTemplate, IScpiCodec<T> codec, Func<T, bool>? validator)
    {
        Metadata = metadata;
        QueryTemplate = queryTemplate;
        SetTemplate = setTemplate;
        Codec = codec;
        Validator = validator;
    }

    public BoundScpiParameter<T> Bind(IScpiSession session, ScpiAddress? address = null)
        => new(this, session, address ?? ScpiAddress.Root);
}

/// <summary>Factory for declarative control, measurement, and setting definitions.</summary>
public static class ScpiParameter
{
    public static ScpiParameterDefinition<T> Control<T>(ParameterMetadata metadata,
        string query, string set, IScpiCodec<T> codec, Func<T, bool>? validate = null) =>
        Create(metadata with { Readable = true, Writable = true }, query, set, codec, validate);

    public static ScpiParameterDefinition<T> Measurement<T>(ParameterMetadata metadata,
        string query, IScpiCodec<T> codec) =>
        Create(metadata with { Readable = true, Writable = false }, query, null, codec, null);

    public static ScpiParameterDefinition<T> Setting<T>(ParameterMetadata metadata,
        string set, IScpiCodec<T> codec, Func<T, bool>? validate = null) =>
        Create(metadata with { Readable = false, Writable = true }, null, set, codec, validate);

    private static ScpiParameterDefinition<T> Create<T>(ParameterMetadata metadata,
        string? query, string? set, IScpiCodec<T> codec, Func<T, bool>? validate)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(codec);
        if (query is null && set is null) throw new ArgumentException("A command template is required.");
        if (query is not null) ScpiAddress.ValidateTemplate(query);
        if (set is not null) ScpiAddress.ValidateTemplate(set);
        return new(metadata, query, set, codec, validate);
    }
}

/// <summary>Hierarchical addressing of SCPI channels through explicit {channel} tokens.</summary>
public sealed record ScpiAddress(string Prefix, string? Channel)
{
    public static ScpiAddress Root { get; } = new("", null);

    public ScpiAddress Child(string prefix, string channel)
    {
        if (!Regex.IsMatch(prefix, @"^[A-Za-z][A-Za-z0-9:]*$"))
            throw new ArgumentException("Channel prefix must be a SCPI mnemonic.", nameof(prefix));
        if (!Regex.IsMatch(channel, @"^[A-Za-z0-9_]+$"))
            throw new ArgumentException("Channel identifier must be alphanumeric.", nameof(channel));
        return new(Prefix + prefix, channel);
    }

    internal static void ValidateTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) throw new ArgumentException("SCPI command cannot be empty.");
        if (template.Contains('{') && !(template.Contains("{channel}", StringComparison.Ordinal) || template.Contains("{value}", StringComparison.Ordinal)))
            throw new ArgumentException("Unsupported template placeholder.");
        var withoutTokens = template.Replace("{channel}", "", StringComparison.Ordinal).Replace("{value}", "", StringComparison.Ordinal);
        if (withoutTokens.Contains('{') || withoutTokens.Contains('}'))
            throw new ArgumentException("Unsupported template placeholder.");
    }

    public string Resolve(string template) =>
        template.Contains("{channel}", StringComparison.Ordinal)
            ? template.Replace("{channel}", Channel ?? throw new InvalidOperationException(
                "A channel-specific SCPI command must be bound to a channel."), StringComparison.Ordinal)
            : template;
}

/// <summary>Typed bound parameter implementing validation, command formatting and parsing.</summary>
public sealed class BoundScpiParameter<T>
{
    private readonly ScpiParameterDefinition<T> _definition;
    private readonly IScpiSession _session;
    private readonly ScpiAddress _address;

    internal BoundScpiParameter(ScpiParameterDefinition<T> definition, IScpiSession session, ScpiAddress address)
    {
        _definition = definition;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _address = address;
    }

    public ParameterMetadata Metadata => _definition.Metadata;

    public T Get()
    {
        if (_definition.QueryTemplate is null) throw new NotSupportedException($"Parameter '{Metadata.Name}' is write-only.");
        return _definition.Codec.Parse(_session.Query(_address.Resolve(_definition.QueryTemplate)));
    }

    public void Set(T value)
    {
        if (_definition.SetTemplate is null) throw new NotSupportedException($"Parameter '{Metadata.Name}' is read-only.");
        if (_definition.Validator is not null && !_definition.Validator(value))
            throw new ArgumentOutOfRangeException(nameof(value), $"Invalid {Metadata.Name}.");
        if (value is double numeric && (!double.IsFinite(numeric) ||
            (Metadata.Minimum is not null && numeric < Metadata.Minimum) ||
            (Metadata.Maximum is not null && numeric > Metadata.Maximum)))
            throw new ArgumentOutOfRangeException(nameof(value), $"Outside limits for {Metadata.Name}.");
        var format = _definition.Codec.Format(value);
        _session.Write(_address.Resolve(_definition.SetTemplate).Replace("{value}", format, StringComparison.Ordinal));
    }
}

/// <summary>Reusable channel tree root, including arbitrary model-specific channel IDs.</summary>
public sealed class ScpiChannelGroup(IScpiSession session)
{
    public ScpiChannel Channel(string channel, string commandPrefix = "SOUR") =>
        new(session, ScpiAddress.Root.Child(commandPrefix, channel));
}

public sealed class ScpiChannel(IScpiSession session, ScpiAddress address)
{
    public BoundScpiParameter<T> Parameter<T>(ScpiParameterDefinition<T> definition)
        => definition.Bind(session, address);
}
