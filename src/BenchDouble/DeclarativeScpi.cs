using System.Globalization;
using System.Text.RegularExpressions;

namespace BenchDouble.Declarative;

/// <summary>Instrument parameter identity, limits, and access metadata.</summary>
/// <param name="Name">Public parameter identifier.</param>
/// <param name="Description">Human-readable description of the parameter.</param>
/// <param name="Unit">Optional physical unit symbol.</param>
/// <param name="Minimum">Optional inclusive minimum in the declared unit.</param>
/// <param name="Maximum">Optional inclusive maximum in the declared unit.</param>
/// <param name="Readable">Whether the parameter can be queried.</param>
/// <param name="Writable">Whether the parameter can be written.</param>
public sealed record ParameterMetadata(string Name, string Description, string? Unit = null,
    double? Minimum = null, double? Maximum = null, bool Readable = true, bool Writable = true);

/// <summary>Converts a typed value to and from SCPI wire representations.</summary>
public interface IScpiCodec<T>
{
    /// <summary>Format a typed value for a SCPI command.</summary>
    /// <param name="value">Value to encode.</param>
    /// <returns>SCPI wire representation of the value.</returns>
    string Format(T value);
    /// <summary>Parse a SCPI response into a typed value.</summary>
    /// <param name="response">Raw SCPI response string.</param>
    /// <returns>Decoded typed value.</returns>
    T Parse(string response);
}

/// <summary>Culture-invariant floating-point SCPI encoding.</summary>
public sealed class DoubleCodec : IScpiCodec<double>
{
    /// <summary>Get the shared invariant-culture floating-point codec.</summary>
    public static DoubleCodec Instance { get; } = new();
    /// <summary>Format a floating-point number with invariant culture and round-trip precision.</summary>
    /// <param name="value">Numeric value to encode.</param>
    /// <returns>Invariant-culture SCPI number.</returns>
    public string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    /// <summary>Parse an invariant-culture floating-point SCPI response.</summary>
    /// <param name="response">Raw numeric response string.</param>
    /// <returns>Parsed floating-point value.</returns>
    public double Parse(string response) => double.Parse(response.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>Wire codec for SCPI ON/OFF and 1/0 values.</summary>
public sealed class BooleanCodec : IScpiCodec<bool>
{
    /// <summary>Get the shared SCPI Boolean codec.</summary>
    public static BooleanCodec Instance { get; } = new();
    /// <summary>Format a Boolean as the SCPI ON or OFF token.</summary>
    /// <param name="value">Boolean output state.</param>
    /// <returns>ON when enabled, otherwise OFF.</returns>
    public string Format(bool value) => value ? "ON" : "OFF";
    /// <summary>Parse ON, OFF, 1, or 0 as a Boolean.</summary>
    /// <param name="response">SCPI Boolean response.</param>
    /// <returns>Decoded Boolean state.</returns>
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
    /// <summary>Get the public metadata associated with the parameter definition.</summary>
    public ParameterMetadata Metadata { get; }
    /// <summary>Get the optional SCPI query template.</summary>
    public string? QueryTemplate { get; }
    /// <summary>Get the optional SCPI write template.</summary>
    public string? SetTemplate { get; }
    /// <summary>Get the codec used to format and parse parameter values.</summary>
    public IScpiCodec<T> Codec { get; }
    /// <summary>Get the optional typed value validator.</summary>
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

    /// <summary>Bind this definition to a caller-owned session and optional channel address.</summary>
    /// <param name="session">Session used for subsequent SCPI operations.</param>
    /// <param name="address">Optional channel address; defaults to the root.</param>
    /// <returns>A typed parameter bound to the selected session and address.</returns>
    public BoundScpiParameter<T> Bind(IScpiSession session, ScpiAddress? address = null)
        => new(this, session, address ?? ScpiAddress.Root);
}

/// <summary>Factory for declarative control, measurement, and setting definitions.</summary>
public static class ScpiParameter
{
    /// <summary>Define a readable and writable typed SCPI control.</summary>
    /// <param name="metadata">Public parameter metadata.</param>
    /// <param name="query">SCPI query command template.</param>
    /// <param name="set">SCPI write command template.</param>
    /// <param name="codec">Typed wire-value codec.</param>
    /// <param name="validate">Optional value validation delegate.</param>
    /// <returns>A validated read/write parameter definition.</returns>
    public static ScpiParameterDefinition<T> Control<T>(ParameterMetadata metadata,
        string query, string set, IScpiCodec<T> codec, Func<T, bool>? validate = null) =>
        Create(metadata with { Readable = true, Writable = true }, query, set, codec, validate);

    /// <summary>Define a read-only typed SCPI measurement.</summary>
    /// <param name="metadata">Public parameter metadata.</param>
    /// <param name="query">SCPI query command template.</param>
    /// <param name="codec">Typed response codec.</param>
    /// <returns>A validated read-only measurement definition.</returns>
    public static ScpiParameterDefinition<T> Measurement<T>(ParameterMetadata metadata,
        string query, IScpiCodec<T> codec) =>
        Create(metadata with { Readable = true, Writable = false }, query, null, codec, null);

    /// <summary>Define a write-only typed SCPI setting.</summary>
    /// <param name="metadata">Public parameter metadata.</param>
    /// <param name="set">SCPI write command template.</param>
    /// <param name="codec">Typed command-value codec.</param>
    /// <param name="validate">Optional value validation delegate.</param>
    /// <returns>A validated write-only parameter definition.</returns>
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

/// <summary>Represent the root or channel-specific address of a SCPI command.</summary>
/// <param name="Prefix">SCPI command mnemonic prefix.</param>
/// <param name="Channel">Optional channel identifier.</param>
public sealed record ScpiAddress(string Prefix, string? Channel)
{
    /// <summary>Get the channel-independent root address.</summary>
    public static ScpiAddress Root { get; } = new("", null);

    /// <summary>Construct a validated channel-specific child address.</summary>
    /// <param name="prefix">SCPI mnemonic prefix for the channel.</param>
    /// <param name="channel">Alphanumeric channel identifier.</param>
    /// <returns>A new immutable channel address.</returns>
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

    /// <summary>Substitute the bound channel into a SCPI command template.</summary>
    /// <param name="template">Command template containing an optional channel placeholder.</param>
    /// <returns>Resolved SCPI command string.</returns>
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

    /// <summary>Get the metadata of the bound parameter.</summary>
    public ParameterMetadata Metadata => _definition.Metadata;

    /// <summary>Query and decode the parameter value from the bound SCPI session.</summary>
    /// <returns>Decoded parameter value in its declared type.</returns>
    public T Get()
    {
        if (_definition.QueryTemplate is null) throw new NotSupportedException($"Parameter '{Metadata.Name}' is write-only.");
        return _definition.Codec.Parse(_session.Query(_address.Resolve(_definition.QueryTemplate)));
    }

    /// <summary>Validate, format, and write the parameter to the bound SCPI session.</summary>
    /// <param name="value">Typed parameter value to write.</param>
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

/// <summary>Create a reusable channel-addressing group.</summary>
/// <param name="session">Caller-owned SCPI session shared by the group.</param>
public sealed class ScpiChannelGroup(IScpiSession session)
{
    /// <summary>Create a channel-specific parameter binding context.</summary>
    /// <param name="channel">Alphanumeric channel identifier.</param>
    /// <param name="commandPrefix">SCPI command mnemonic prefix; defaults to SOUR.</param>
    /// <returns>Channel context sharing the group's session.</returns>
    public ScpiChannel Channel(string channel, string commandPrefix = "SOUR") =>
        new(session, ScpiAddress.Root.Child(commandPrefix, channel));
}

/// <summary>Bind an address and SCPI session to parameter definitions.</summary>
/// <param name="session">Caller-owned SCPI session.</param>
/// <param name="address">Channel address applied to parameter commands.</param>
public sealed class ScpiChannel(IScpiSession session, ScpiAddress address)
{
    /// <summary>Bind a typed parameter definition to this channel.</summary>
    /// <param name="definition">Validated parameter definition.</param>
    /// <returns>A typed bound parameter for the channel.</returns>
    public BoundScpiParameter<T> Parameter<T>(ScpiParameterDefinition<T> definition)
        => definition.Bind(session, address);
}
