using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BenchDouble.Host.Server.Setup;

/// <summary>Validated, versioned setup persistence and role routing through the existing host registry.</summary>
/// <param name="registry">Canonical reservation authority; no independent setup leases are created.</param>
/// <param name="discovery">Supported discovery provider.</param>
/// <param name="path">Explicit host-owned configuration filename.</param>
public sealed class BenchSetupService(HostResourceRegistry registry, ISetupDiscoveryProvider discovery, string path)
{
    private readonly object _gate = new();
    private SetupDiscovery? _discovered;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private sealed record Stored(bool Draft, BenchSetup Configuration);

    /// <summary>Enumerate inventory without publishing an incomplete or cancelled discovery result.</summary>
    /// <param name="ct">Discovery cancellation.</param>
    /// <param name="provider">Optional explicitly selected provider; defaults to the registered inventory.</param>
    /// <returns>A detached inventory snapshot for explicit operator review.</returns>
    public async ValueTask<SetupDiscovery> DiscoverAsync(CancellationToken ct = default, ISetupDiscoveryProvider? provider = null)
    {
        var result = await (provider ?? discovery).DiscoverAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        lock (_gate) _discovered = Copy(result);
        return Copy(result);
    }

    /// <summary>Parse a strict versioned configuration, rejecting duplicate and unknown JSON fields.</summary>
    /// <param name="json">Configuration JSON; not a saved-revision envelope.</param>
    /// <returns>A detached configuration with structural validation applied.</returns>
    /// <exception cref="JsonException">The JSON is malformed or contains duplicate/unknown fields.</exception>
    /// <exception cref="ArgumentException">The document violates the setup schema.</exception>
    public static BenchSetup Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        CheckDuplicates(document.RootElement);
        var configuration = JsonSerializer.Deserialize<BenchSetup>(json, Json)
            ?? throw new JsonException("Missing setup configuration.");
        var errors = StructuralProblems(configuration);
        if (errors.Length > 0) throw new ArgumentException(string.Join("; ", errors.Select(x => x.Message)));
        return configuration;
    }

    /// <summary>Parse a save envelope using the same strict rules as configuration import.</summary>
    /// <param name="json">Save request containing configuration, expected revision and draft flag.</param>
    /// <returns>A structurally valid save request.</returns>
    public static SaveSetupRequest ParseSaveRequest(string json)
    {
        using var document = JsonDocument.Parse(json);
        CheckDuplicates(document.RootElement);
        var request = JsonSerializer.Deserialize<SaveSetupRequest>(json, Json) ?? throw new JsonException("Missing save request.");
        return request with { Configuration = Parse(JsonSerializer.Serialize(request.Configuration, Json)) };
    }

    /// <summary>Validate structure, controller bindings, association evidence and optional expected identities.</summary>
    /// <param name="configuration">Candidate configuration; never mutates saved state.</param>
    /// <returns>Actionable problems; empty means supported connections passed validation at this instant.</returns>
    public SetupProblem[] Validate(BenchSetup configuration)
    {
        lock (_gate) return ValidateCore(Copy(configuration));
    }

    /// <summary>Read the saved revision; no file means no configuration, while corrupt files are errors.</summary>
    /// <returns>A detached saved snapshot, or null when no file exists.</returns>
    public SavedSetup? Read()
    {
        lock (_gate) return ReadCore();
    }

    /// <summary>Atomically save a reviewed configuration without changing active leases or snapshots.</summary>
    /// <param name="request">Expected prior revision, candidate and explicit offline-draft choice.</param>
    /// <returns>The new saved revision.</returns>
    /// <exception cref="InvalidOperationException">Revision changed, or connection validation failed.</exception>
    /// <exception cref="ArgumentException">Structural validation failed.</exception>
    public SavedSetup Save(SaveSetupRequest request)
    {
        var candidate = Copy(request.Configuration);
        lock (_gate)
        {
            if (ReadCore()?.Revision != request.ExpectedRevision)
                throw new InvalidOperationException("Configuration changed. Reload and review the old/new mappings before saving.");
            var errors = StructuralProblems(candidate);
            if (errors.Length > 0) throw new ArgumentException(string.Join("; ", errors.Select(x => x.Message)));
            if (!request.Draft)
            {
                errors = ValidateCore(candidate);
                if (errors.Length > 0) throw new InvalidOperationException(string.Join("; ", errors.Select(x => x.Message)));
            }
            var text = JsonSerializer.Serialize(new Stored(request.Draft, candidate), Json);
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                File.Move(temporary, fullPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new(Digest(text), request.Draft, Copy(candidate));
        }
    }

    /// <summary>Reserve a configured role through the existing host authority and pin its route and revision.</summary>
    /// <param name="role">Configured logical experiment role.</param>
    /// <returns>A normal host lease plus immutable route evidence, or null if the instrument is busy.</returns>
    /// <exception cref="InvalidOperationException">Setup is absent, draft, stale or no longer valid.</exception>
    /// <exception cref="KeyNotFoundException">The role does not exist.</exception>
    public SetupLease? ReserveRole(string role)
    {
        lock (_gate)
        {
            var saved = ReadCore() ?? throw new InvalidOperationException("Save a validated setup first.");
            if (saved.Draft) throw new InvalidOperationException("Offline drafts cannot control instruments. Discover and validate first.");
            var assigned = saved.Configuration.Roles.SingleOrDefault(x => Same(x.Role, role))
                ?? throw new KeyNotFoundException("Unknown experiment role.");
            var instrument = saved.Configuration.Instruments.Single(x => Same(x.Id, assigned.InstrumentId));
            var route = instrument.Connections.Single(x => Same(x.Id, instrument.SelectedConnection));
            // Restrict revalidation to the requested instrument; other instruments may be in use.
            var selected = new BenchSetup(1, saved.Configuration.Controllers, [instrument], [assigned]);
            var errors = ValidateCore(selected, probe: false);
            if (errors.Length > 0) throw new InvalidOperationException(string.Join("; ", errors.Select(x => x.Message)));
            var lease = registry.Reserve(instrument.Id);
            if (lease is null) return null;
            try
            {
                VerifyIdentity(instrument, lease.LeaseId);
                return new(lease.LeaseId, instrument.Id, saved.Revision, route);
            }
            catch { registry.Release(lease.LeaseId); throw; }
        }
    }

    private SavedSetup? ReadCore()
    {
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        using var document = JsonDocument.Parse(text);
        CheckDuplicates(document.RootElement);
        var saved = JsonSerializer.Deserialize<Stored>(text, Json) ?? throw new JsonException("Missing saved setup.");
        var configuration = Parse(JsonSerializer.Serialize(saved.Configuration, Json));
        return new(Digest(text), saved.Draft, configuration);
    }

    private SetupProblem[] ValidateCore(BenchSetup configuration, bool probe = true)
    {
        var errors = StructuralProblems(configuration).ToList();
        if (errors.Count > 0) return errors.ToArray();
        if (_discovered is null) return [new("discovery_required", "controllers", "Discover the local host before validating; offline drafts may still be saved.")];
        foreach (var instrument in configuration.Instruments)
        {
            var before = errors.Count;
            foreach (var route in instrument.Connections)
            {
                string location = $"{instrument.Id}/{route.Id}";
                string? controllerAddress = null;
                if (route.ControllerId is not null)
                {
                    var binding = configuration.Controllers.Single(x => Same(x.Id, route.ControllerId));
                    var matches = _discovered.Controllers.Where(x => Same(x.Provider, binding.Provider) &&
                        (binding.StableId is null ? Same(x.Address, binding.Address) : x.StableId == binding.StableId)).ToArray();
                    if (matches.Length != 1)
                    {
                        errors.Add(new("controller_unresolved", location, "Controller missing or ambiguous. Rediscover and review its binding."));
                        continue;
                    }
                    if (binding.StableId is null && binding.ConfirmedDiscoveryId != _discovered.Id)
                    {
                        errors.Add(new("controller_confirmation", location, "Confirm this adapter in the current discovery snapshot; no stable identity is available."));
                        continue;
                    }
                    controllerAddress = matches[0].Address;
                }
                var endpoints = _discovered.Endpoints.Where(x => Same(x.Provider, route.Provider) &&
                    Same(x.Transport, route.Transport) && Same(x.Address, route.Address) && Same(x.ControllerAddress, controllerAddress)).ToArray();
                if (endpoints.Length != 1)
                    errors.Add(new("endpoint_unresolved", location, "Endpoint absent or ambiguous. Check the provider, address and selected controller."));
                else if (endpoints[0].ResourceId is null || !Same(endpoints[0].ResourceId, instrument.Id))
                    errors.Add(new("identity_unconfirmed", location, "Endpoint is not registered to this canonical instrument. Review the association; physical providers need host registration."));
            }
            if (!probe || errors.Count != before) continue;
            var lease = registry.Reserve(instrument.Id);
            if (lease is null)
            {
                errors.Add(new("busy", instrument.Id, "Instrument is reserved or unavailable. Wait for its owner; validation has not queried it."));
                continue;
            }
            try { VerifyIdentity(instrument, lease.LeaseId); }
            catch (InvalidOperationException ex) { errors.Add(new("identity_mismatch", instrument.Id, ex.Message)); }
            finally { registry.Release(lease.LeaseId); }
        }
        return errors.ToArray();
    }

    private void VerifyIdentity(SetupInstrument instrument, Guid lease)
    {
        // Only known simulator endpoints reach here. Physical drivers must supply their own supported identity operation.
        var fields = registry.Query(instrument.Id, lease, "*IDN?").Split(',', 4, StringSplitOptions.TrimEntries);
        var expected = instrument.ExpectedIdentity;
        if (expected is null) return;
        var values = new[] { expected.Manufacturer, expected.Model, expected.Serial };
        if (fields.Length < 3 || values.Where((value, index) =>
                !string.IsNullOrWhiteSpace(value) && !Same(value, fields[index])).Any())
            throw new InvalidOperationException("Expected instrument identity does not match. Review the endpoint; no replacement was selected.");
    }

    private static SetupProblem[] StructuralProblems(BenchSetup? configuration)
    {
        var errors = new List<SetupProblem>();
        void Error(string path, string message) => errors.Add(new("invalid_configuration", path, message));
        if (configuration is null || configuration.SchemaVersion != 1 || configuration.Controllers is null ||
            configuration.Instruments is null || configuration.Roles is null)
            return [new("invalid_schema", "$", "Schema version 1 requires controllers, instruments and roles arrays.")];
        if (configuration.Controllers.Any(x => x is null) || configuration.Instruments.Any(x => x is null) || configuration.Roles.Any(x => x is null))
            return [new("invalid_schema", "$", "Array entries cannot be null.")];
        bool Id(string? value) => value is not null && Regex.IsMatch(value, @"\A[A-Za-z0-9_.-]{1,80}\z");
        bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512 && !value.Any(char.IsControl);
        void Unique(IEnumerable<string> ids, string location)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids) if (!Id(id) || !seen.Add(id)) Error(location, "Identifiers must be unique and use letters, digits, dot, underscore or hyphen.");
        }
        Unique(configuration.Controllers.Select(x => x.Id), "controllers");
        Unique(configuration.Instruments.Select(x => x.Id), "instruments");
        Unique(configuration.Roles.Select(x => x.Role), "roles");
        var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var controller in configuration.Controllers)
        {
            if (!Id(controller.Provider) || !Text(controller.Address)) Error(controller.Id, "Controller requires a provider and address.");
            if (!bindings.Add(JsonSerializer.Serialize(new[] { controller.Provider, controller.StableId ?? controller.Address })))
                Error(controller.Id, "Two controller names cannot bind the same adapter.");
        }
        foreach (var instrument in configuration.Instruments)
        {
            if (!Text(instrument.Name) || instrument.Connections is null || instrument.Connections.Length == 0 || instrument.Connections.Any(x => x is null))
            { Error(instrument.Id, "Instrument requires a friendly name and at least one non-null connection."); continue; }
            Unique(instrument.Connections.Select(x => x.Id), instrument.Id);
            if (!instrument.Connections.Any(x => Same(x.Id, instrument.SelectedConnection))) Error(instrument.Id, "Select an existing connection.");
            if (instrument.Connections.Length > 1 && !instrument.AssociationConfirmed) Error(instrument.Id, "Confirm that these alternate routes belong to one physical instrument.");
            foreach (var connection in instrument.Connections)
            {
                if (!Id(connection.Provider) || !Id(connection.Transport) || !Text(connection.Address)) Error(instrument.Id, "Each connection requires provider, transport and address.");
                if (connection.ControllerId is not null && !configuration.Controllers.Any(x => Same(x.Id, connection.ControllerId) && Same(x.Provider, connection.Provider)))
                    Error(instrument.Id, "Connection references an unknown controller or a different provider.");
                if (Same(connection.Transport, "GPIB") && (connection.ControllerId is null || !int.TryParse(connection.Address, out var address) || address < 0 || address > 30 || connection.Address != address.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    Error(instrument.Id, "GPIB requires a named controller and a decimal primary address from 0 to 30.");
                if (!endpoints.Add(JsonSerializer.Serialize(new[] { connection.Provider, connection.Transport, connection.ControllerId, connection.Address })))
                    Error(instrument.Id, "An endpoint cannot be assigned to multiple instruments or duplicated as another route.");
            }
        }
        foreach (var role in configuration.Roles)
            if (!configuration.Instruments.Any(x => Same(x.Id, role.InstrumentId))) Error(role.Role, "Role refers to an unknown instrument.");
        return errors.ToArray();
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!fields.Add(property.Name)) throw new JsonException("Duplicate setup field: " + property.Name);
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckDuplicates(item);
    }
}
