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
    /// <param name="json">Schema-v2 JSON, at most 1 Mi characters and 32 nesting levels.</param>
    /// <returns>A validated simulation-only definition.</returns>
    /// <exception cref="InvalidDataException">The definition is oversized, ambiguous or contains unsupported capabilities.</exception>
    /// <exception cref="JsonException">JSON is malformed or exceeds the nesting limit.</exception>
    /// <exception cref="ArgumentNullException">The input is null.</exception>
    public static ImportedInstrumentDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 1024 * 1024) throw new InvalidDataException("Instrument definition exceeds one million characters.");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = doc.RootElement;
        ValidateUniqueKeys(root);
        if (root.GetProperty("schemaVersion").GetInt32() != 2) throw new InvalidDataException("Expected schema v2.");
        var identity = root.GetProperty("identity");
        string name = identity.GetProperty("driverName").GetString() ?? "";
        if (!Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]*\z")) throw new InvalidDataException("Invalid driver identifier.");
        var result = new ImportedInstrumentDefinition(name);
        foreach (var mode in root.GetProperty("modes").EnumerateArray())
        {
            string id = mode.GetProperty("id").GetString() ?? "";
            string command = mode.TryGetProperty("selectionCommand", out var node) && node.ValueKind == JsonValueKind.String
                ? node.GetString()! : "";
            ValidateCommand(command, allowEmpty: false);
            if (result._modeCommands.Values.Contains(command, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Ambiguous mode selection command.");
            if (!Regex.IsMatch(id, @"\A[A-Za-z_][A-Za-z0-9_]*\z") || !result._modeCommands.TryAdd(id, command))
                throw new InvalidDataException("Invalid or duplicate mode.");
        }
        foreach (var capability in root.GetProperty("capabilities").EnumerateArray())
        {
            string id = capability.GetProperty("name").GetString() ?? "";
            string quantity = capability.GetProperty("quantity").GetString() ?? "";
            string unit = capability.GetProperty("unit").GetString() ?? "";
            string query = capability.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString()! : "";
            if (!Regex.IsMatch(id, @"\A[A-Za-z_][A-Za-z0-9_]*\z")) throw new InvalidDataException("Invalid capability.");
            if (!new[] { "ElectricPotential:Volt", "ElectricResistance:Ohm", "ElectricCurrent:Ampere", "Frequency:Hertz" }.Contains(quantity + ":" + unit))
                throw new InvalidDataException("Unsupported quantity/unit.");
            ValidateCommand(query, allowEmpty: false);
            if (capability.GetProperty("access").GetString() is not "read" and not "readwrite")
                throw new InvalidDataException("Only readable scalar capabilities supported.");
            var modes = capability.GetProperty("modes").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            if (modes.Length == 0 || modes.Distinct(StringComparer.Ordinal).Count() != modes.Length || modes.Any(x => !result._modeCommands.ContainsKey(x)))
                throw new InvalidDataException("Capability references unknown mode.");
            if (capability.TryGetProperty("mockDefault", out var defaultNode) && defaultNode.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
                throw new InvalidDataException("Mock default must be numeric or null.");
            double? mockDefault = capability.TryGetProperty("mockDefault", out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble() : null;
            if (mockDefault.HasValue && !double.IsFinite(mockDefault.Value)) throw new InvalidDataException("Nonfinite default.");
            if (result._capabilities.Values.Any(existing => string.Equals(existing.Query, query, StringComparison.OrdinalIgnoreCase) && existing.Modes.Intersect(modes).Any()))
                throw new InvalidDataException("Ambiguous query in the same operating mode.");
            if (!result._capabilities.TryAdd(id, new ImportedCapability(id, quantity, unit, query, Array.AsReadOnly(modes), mockDefault)))
                throw new InvalidDataException("Duplicate capability.");
        }
        if (result._modeCommands.Count == 0 || result._capabilities.Count == 0)
            throw new InvalidDataException("Definition needs modes and capabilities.");
        return result;
    }

    private static void ValidateUniqueKeys(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate instrument definition field.");
                ValidateUniqueKeys(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) ValidateUniqueKeys(item);
    }

    private static void ValidateCommand(string command, bool allowEmpty)
    {
        if (command.Length == 0 && allowEmpty) return;
        if (!Regex.IsMatch(command, @"^[A-Za-z][A-Za-z0-9_:? .+-]*$") ||
            command.Contains('\n') || command.Contains('\r'))
            throw new InvalidDataException("Unsafe or unsupported SCPI command.");
    }

    /// <summary>Get a mode selection command from a validated definition.</summary>
    /// <param name="mode">Declared mode identifier.</param>
    /// <returns>Validated mode-selection command text; this does not execute the command.</returns>
    public string ModeCommand(string mode) => _modeCommands.TryGetValue(mode, out var value)
        ? value : throw new NotSupportedException("Unknown mode.");

    /// <summary>Get a capability from a validated definition.</summary>
    /// <param name="name">Declared capability identifier.</param>
    /// <returns>Immutable measurement metadata.</returns>
    public ImportedCapability Capability(string name) => _capabilities.TryGetValue(name, out var value)
        ? value : throw new NotSupportedException("Unknown capability.");
}

/// <summary>Immutable scalar measurement metadata.</summary>
/// <param name="Name">Validated capability identifier.</param>
/// <param name="Quantity">UnitsNet quantity name.</param>
/// <param name="Unit">Canonical measurement unit.</param>
/// <param name="Query">Validated SCPI query template.</param>
/// <param name="Modes">Operating modes in which the capability is valid.</param>
/// <param name="MockDefault">Optional explicitly configured mock reading in the declared unit.</param>
public sealed record ImportedCapability(string Name, string Quantity, string Unit, string Query,
    IReadOnlyList<string> Modes, double? MockDefault);

/// <summary>Simulation-only mode-aware driver and deterministic mock sharing one imported definition.</summary>
public sealed class ImportedInstrumentMock
{
    private readonly StatefulMockEngine _engine;

    /// <summary>Create a simulated instrument with an optional run-scoped clock and logger.</summary>
    /// <param name="definition">Validated imported instrument definition.</param>
    /// <param name="clock">Optional injected virtual clock.</param>
    /// <param name="logger">Optional structured logger for state transitions.</param>
    public ImportedInstrumentMock(ImportedInstrumentDefinition definition,
        BenchDouble.Simulation.ISimulationClock? clock = null,
        Microsoft.Extensions.Logging.ILogger<StatefulMockEngine>? logger = null)
    {
        _engine = new StatefulMockEngine(definition, clock, logger);
    }

    /// <summary>Get the shared engine used for all stateful operations.</summary>
    public StatefulMockEngine Engine => _engine;

    /// <summary>Set a supported mode without dispatching physical commands.</summary>
    /// <param name="mode">Declared mode identifier.</param>
    /// <param name="cancellationToken">Cancellation while waiting for exclusive access.</param>
    /// <returns>Completion of the mode transition.</returns>
    public ValueTask ConfigureModeAsync(string mode, CancellationToken cancellationToken = default) =>
        _engine.ConfigureModeAsync(mode, cancellationToken);

    /// <summary>Measure a compatible scalar capability.</summary>
    /// <param name="capability">Declared capability identifier.</param>
    /// <param name="cancellationToken">Cancellation while waiting for the state transaction.</param>
    /// <returns>Finite reading in the capability's canonical unit.</returns>
    public ValueTask<double> MeasureAsync(string capability, CancellationToken cancellationToken = default) =>
        _engine.MeasureAsync(capability, cancellationToken);

    /// <summary>Read a voltage or current capability from one immutable acquisition.</summary>
    /// <param name="capability">Declared compatible scalar capability.</param>
    /// <param name="acquisition">Source and waveform snapshot with a shared acquisition identity.</param>
    /// <param name="cancellationToken">Cancellation while waiting for exclusive access.</param>
    /// <returns>Captured voltage in volts or current in amperes.</returns>
    public ValueTask<double> MeasureCapturedAsync(string capability,
        BenchDouble.Simulation.VirtualCoherentAcquisition acquisition,
        CancellationToken cancellationToken = default) =>
        _engine.MeasureCapturedAsync(capability, acquisition, cancellationToken);

    /// <summary>Read a captured scalar with acquisition and revision provenance.</summary>
    /// <param name="capability">Declared voltage or current capability.</param>
    /// <param name="acquisition">Immutable correlated source and scope acquisition.</param>
    /// <param name="cancellationToken">Cancellation while waiting for exclusive access.</param>
    /// <returns>Captured scalar with physical units and source/mock revisions.</returns>
    public ValueTask<CapturedMockReading> ReadCapturedAsync(string capability,
        BenchDouble.Simulation.VirtualCoherentAcquisition acquisition,
        CancellationToken cancellationToken = default) =>
        _engine.ReadCapturedAsync(capability, acquisition, cancellationToken);

    /// <summary>Override a scalar reading with a finite canonical-unit value.</summary>
    /// <param name="capability">Declared capability identifier.</param>
    /// <param name="value">Finite simulated reading.</param>
    public void SetReading(string capability, double value) => _engine.SetReading(capability, value);

    /// <summary>Install a copied scalar sequence without dispatching physical commands.</summary>
    /// <param name="capability">Declared capability identifier.</param>
    /// <param name="values">One to 4096 finite canonical-unit values.</param>
    /// <param name="repeatLast">Whether to hold the final value after initial exhaustion.</param>
    /// <exception cref="ArgumentException">The values are missing, oversized or nonfinite.</exception>
    public void SetSequence(string capability, IEnumerable<double> values, bool repeatLast = false) => _engine.SetSequence(capability, values, repeatLast);

    /// <summary>Remove a sequence, restoring an override or definition fallback.</summary>
    /// <param name="capability">Declared capability identifier.</param>
    /// <returns>True only when an installed sequence was removed.</returns>
    public bool ClearSequence(string capability) => _engine.ClearSequence(capability);

    /// <summary>Read cached cursor diagnostics without consuming a value or reading the clock.</summary>
    /// <param name="capability">Declared capability identifier.</param>
    /// <returns>A detached status, or null when no sequence is installed.</returns>
    public MockSequenceStatus? SequenceStatus(string capability) => _engine.SequenceStatus(capability);

    /// <summary>Install or remove a deterministic virtual-time behavior.</summary>
    /// <param name="capability">Declared capability identifier.</param>
    /// <param name="behavior">Pure, non-reentrant behavior, or null to remove.</param>
    public void SetBehavior(string capability, Func<MockReadingContext, double>? behavior) =>
        _engine.SetBehavior(capability, behavior);

    /// <summary>Install or remove a cancellation-aware async measurement model.</summary>
    /// <param name="capability">Declared capability identifier.</param>
    /// <param name="behavior">Non-reentrant async behavior, or null for fallback.</param>
    public void SetAsyncBehavior(string capability,
        Func<MockReadingContext, CancellationToken, ValueTask<double>>? behavior) =>
        _engine.SetAsyncBehavior(capability, behavior);

    /// <summary>Remove an explicit scalar reading.</summary>
    /// <param name="capability">Declared capability identifier.</param>
    public void ClearReading(string capability) => _engine.ClearReading(capability);

    /// <summary>Clear all explicit scalar readings while retaining the selected mode.</summary>
    public void ClearAllReadings() => _engine.ClearAllReadings();

    /// <summary>Clear pending simulated faults for one capability without changing its mode or reading.</summary>
    /// <param name="capability">Declared scalar capability.</param>
    /// <returns>Number of discarded pending faults.</returns>
    /// <exception cref="NotSupportedException">The capability is not declared by this simulation definition.</exception>
    public int ClearFaults(string capability) => _engine.ClearFaults(capability);

    /// <summary>Queue a one-shot simulated measurement failure.</summary>
    /// <param name="capability">Declared capability identifier.</param>
    /// <param name="failure">Exception to throw on the next compatible measurement.</param>
    public void QueueFault(string capability, Exception failure) => _engine.QueueFault(capability, failure);

    /// <summary>Capture a detached, immutable snapshot of the current mock state.</summary>
    /// <returns>Mode, revision, virtual time, and copied overrides.</returns>
    public StatefulMockSnapshot Snapshot() => _engine.Snapshot();

    /// <summary>Reset all mock state to its definition defaults.</summary>
    public void Reset() => _engine.Reset();
}
