using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BenchDouble.Generated;

/// <summary>Validated schema-v2 definition used by simulation-only generated-style adapters.</summary>
public sealed class ImportedInstrumentDefinition
{
    private readonly Dictionary<string, string> _modeCommands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImportedCapability> _capabilities = new(StringComparer.Ordinal);
    /// <summary>Instrument's validated C# identifier.</summary>
    public string DriverName { get; }
    /// <summary>All supported operating mode identifiers.</summary>
    public IReadOnlyCollection<string> Modes => _modeCommands.Keys;
    /// <summary>All imported scalar capabilities.</summary>
    public IReadOnlyCollection<ImportedCapability> Capabilities => _capabilities.Values;

    private ImportedInstrumentDefinition(string name) => DriverName = name;

    /// <summary>Parse schema v2 with strict supported-subset validation and no code execution.</summary>
    public static ImportedInstrumentDefinition Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2) throw new InvalidDataException("Expected schema v2.");
        var identity = root.GetProperty("identity");
        string name = identity.GetProperty("driverName").GetString() ?? "";
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidDataException("Invalid driver identifier.");
        var result = new ImportedInstrumentDefinition(name);
        foreach (var mode in root.GetProperty("modes").EnumerateArray())
        {
            string id = mode.GetProperty("id").GetString() ?? "";
            string command = mode.TryGetProperty("selectionCommand", out var node) && node.ValueKind == JsonValueKind.String
                ? node.GetString()! : "";
            ValidateCommand(command, allowEmpty: false);
            if (!Regex.IsMatch(id, "^[A-Za-z_][A-Za-z0-9_]*$") || !result._modeCommands.TryAdd(id, command))
                throw new InvalidDataException("Invalid or duplicate mode.");
        }
        foreach (var capability in root.GetProperty("capabilities").EnumerateArray())
        {
            string id = capability.GetProperty("name").GetString() ?? "";
            string quantity = capability.GetProperty("quantity").GetString() ?? "";
            string unit = capability.GetProperty("unit").GetString() ?? "";
            string query = capability.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString()! : "";
            if (!Regex.IsMatch(id, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidDataException("Invalid capability.");
            if (!new[] { "ElectricPotential:Volt", "ElectricResistance:Ohm", "ElectricCurrent:Ampere", "Frequency:Hertz" }.Contains(quantity + ":" + unit))
                throw new InvalidDataException("Unsupported quantity/unit.");
            ValidateCommand(query, allowEmpty: false);
            if (capability.GetProperty("access").GetString() is not "read" and not "readwrite")
                throw new InvalidDataException("Only readable scalar capabilities supported.");
            var modes = capability.GetProperty("modes").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            if (modes.Length == 0 || modes.Any(x => !result._modeCommands.ContainsKey(x)))
                throw new InvalidDataException("Capability references unknown mode.");
            double? mockDefault = capability.TryGetProperty("mockDefault", out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble() : null;
            if (mockDefault.HasValue && !double.IsFinite(mockDefault.Value)) throw new InvalidDataException("Nonfinite default.");
            if (!result._capabilities.TryAdd(id, new ImportedCapability(id, quantity, unit, query, modes, mockDefault)))
                throw new InvalidDataException("Duplicate capability.");
        }
        if (result._modeCommands.Count == 0 || result._capabilities.Count == 0)
            throw new InvalidDataException("Definition needs modes and capabilities.");
        return result;
    }

    private static void ValidateCommand(string command, bool allowEmpty)
    {
        if (command.Length == 0 && allowEmpty) return;
        if (!Regex.IsMatch(command, @"^[A-Za-z][A-Za-z0-9_:? .+-]*$") ||
            command.Contains('\n') || command.Contains('\r'))
            throw new InvalidDataException("Unsafe or unsupported SCPI command.");
    }

    /// <summary>Get a mode selection command from a validated definition.</summary>
    public string ModeCommand(string mode) => _modeCommands.TryGetValue(mode, out var value)
        ? value : throw new NotSupportedException("Unknown mode.");

    /// <summary>Get a capability from a validated definition.</summary>
    public ImportedCapability Capability(string name) => _capabilities.TryGetValue(name, out var value)
        ? value : throw new NotSupportedException("Unknown capability.");
}

/// <summary>Immutable scalar measurement metadata.</summary>
public sealed record ImportedCapability(string Name, string Quantity, string Unit, string Query,
    IReadOnlyList<string> Modes, double? MockDefault);

/// <summary>Simulation-only mode-aware driver and deterministic mock sharing one imported definition.</summary>
public sealed class ImportedInstrumentMock(ImportedInstrumentDefinition definition)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _mode;
    private readonly Dictionary<string, double> _values = definition.Capabilities
        .Where(x => x.MockDefault.HasValue).ToDictionary(x => x.Name, x => x.MockDefault!.Value, StringComparer.Ordinal);

    /// <summary>Set a supported mode; no physical command is dispatched.</summary>
    public async ValueTask ConfigureModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        definition.ModeCommand(mode);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _mode = mode; }
        finally { _gate.Release(); }
    }

    /// <summary>Read a declared scalar quantity only in a compatible mode.</summary>
    public async ValueTask<double> MeasureAsync(string capability, CancellationToken cancellationToken = default)
    {
        var item = definition.Capability(capability);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_mode is null || !item.Modes.Contains(_mode)) throw new InvalidOperationException("Incompatible measurement mode.");
            return _values.TryGetValue(capability, out var value) ? value :
                throw new InvalidOperationException("No configured mock reading.");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Set a finite mock reading for a declared capability.</summary>
    public void SetReading(string capability, double value)
    {
        definition.Capability(capability);
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        _gate.Wait();
        try { _values[capability] = value; }
        finally { _gate.Release(); }
    }
}
