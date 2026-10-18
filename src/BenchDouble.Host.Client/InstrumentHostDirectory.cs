using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace BenchDouble.Host.Client;

/// <summary>Resolved logical role and authoritative server resource; contains no credentials.</summary>
public sealed record InstrumentRoute(string Role, string HostId, Uri Endpoint, string ResourceId);

/// <summary>
/// Immutable local-first routing snapshot. A missing conventional file enables
/// localhost defaults; an explicit file or configured role never silently falls back.
/// This locates resources; it does not grant ownership or enable remote hosting.
/// </summary>
public sealed class InstrumentHostDirectory
{
    public static readonly Uri LocalEndpoint = new("http://127.0.0.1:5278/");
    private static readonly Regex Identifier = new(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z");
    private readonly IReadOnlyDictionary<string, InstrumentRoute> _routes;
    private readonly ILogger _logger;
    public bool UsesLocalDefaults { get; }

    private InstrumentHostDirectory(Dictionary<string, InstrumentRoute> routes,
        bool defaults, ILogger logger)
    {
        _routes = routes;
        UsesLocalDefaults = defaults;
        _logger = logger;
    }

    /// <summary>
    /// Load instruments.json from the current working directory when path is null.
    /// Only absence of that conventional file permits implicit local routing.
    /// Parse, access and explicit-path failures are logged and propagated.
    /// </summary>
    public static InstrumentHostDirectory Load(ILogger logger, string? path = null, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var selected = path ?? Path.Combine(baseDirectory ?? Environment.CurrentDirectory, "instruments.json");
        try
        {
            using var stream = File.OpenRead(selected);
            if (stream.Length > 1024 * 1024)
                throw new InvalidDataException("Instrument configuration exceeds 1 MiB.");
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            Fields(root, "schemaVersion", "hosts", "instruments");
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidDataException("Unsupported instrument configuration schemaVersion.");
            var hosts = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            var hostObject = root.GetProperty("hosts");
            UniqueObject(hostObject);
            foreach (var property in hostObject.EnumerateObject())
            {
                CheckIdentifier(property.Name);
                var endpoint = ParseEndpoint(property.Value.GetString());
                if (!hosts.TryAdd(property.Name, endpoint))
                    throw new InvalidDataException("Duplicate host identifier.");
            }
            var routes = new Dictionary<string, InstrumentRoute>(StringComparer.OrdinalIgnoreCase);
            var instruments = root.GetProperty("instruments");
            UniqueObject(instruments);
            foreach (var property in instruments.EnumerateObject())
            {
                CheckIdentifier(property.Name);
                Fields(property.Value, "host", "resourceId");
                var host = property.Value.GetProperty("host").GetString() ?? "";
                var resource = property.Value.GetProperty("resourceId").GetString() ?? "";
                CheckIdentifier(host);
                CheckIdentifier(resource);
                if (!hosts.TryGetValue(host, out var endpoint))
                    throw new InvalidDataException("Instrument references an unknown host.");
                if (!routes.TryAdd(property.Name, new(property.Name, host, endpoint, resource)))
                    throw new InvalidDataException("Duplicate instrument role.");
            }
            logger.LogInformation("Instrument routing configuration loaded; SchemaVersion={SchemaVersion}; HostCount={HostCount}; InstrumentCount={InstrumentCount}",
                1, hosts.Count, routes.Count);
            return new(routes, false, logger);
        }
        catch (FileNotFoundException) when (path is null)
        {
            logger.LogInformation("No instruments.json found; routing defaults to local host {Endpoint}", LocalEndpoint);
            return new(new(StringComparer.OrdinalIgnoreCase), true, logger);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // Do not echo configuration payloads, paths or credentials in diagnostics.
            logger.LogError("Instrument routing configuration rejected; ErrorType={ErrorType}; no local fallback", exception.GetType().Name);
            throw new InvalidDataException("Instrument routing configuration is invalid or unreadable; no local fallback.", exception);
        }
    }

    /// <summary>Resolve and log the selected route; unknown configured roles are errors.</summary>
    public InstrumentRoute Resolve(string role)
    {
        CheckIdentifier(role);
        if (!UsesLocalDefaults && !_routes.ContainsKey(role))
        {
            _logger.LogWarning("Unknown configured instrument role {Role}; no local fallback", role);
            throw new KeyNotFoundException($"No configured instrument role '{role}'.");
        }
        var route = UsesLocalDefaults ? new InstrumentRoute(role, "local", LocalEndpoint, role)
            : _routes.TryGetValue(role, out var configured) ? configured
            : throw new KeyNotFoundException($"No configured instrument role '{role}'.");
        _logger.LogInformation("Instrument route selected; Role={Role}; HostId={HostId}; Endpoint={Endpoint}; ResourceId={ResourceId}; LocalDefault={LocalDefault}",
            route.Role, route.HostId, route.Endpoint, route.ResourceId, UsesLocalDefaults);
        return route;
    }

    /// <summary>Create a role-bound client without changing or owning the caller's HttpClient.</summary>
    public InstrumentEndpointClient CreateClient(string role, HttpClient http)
    {
        var route = Resolve(role);
        return new(new InstrumentHostClient(http, route.Endpoint), route);
    }

    private static void CheckIdentifier(string value)
    {
        if (value is null || !Identifier.IsMatch(value)) throw new InvalidDataException("Invalid instrument/host identifier.");
    }

    private static Uri ParseEndpoint(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/" ||
            (uri.Scheme == "http" && !uri.IsLoopback))
            throw new InvalidDataException("Endpoint must be an HTTPS origin, or loopback HTTP, without credentials, query or path.");
        return uri;
    }

    private static void UniqueObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected configuration object.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate configuration key.");
    }

    private static void Fields(JsonElement element, params string[] expected)
    {
        UniqueObject(element);
        var actual = element.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected)) throw new InvalidDataException("Missing or unknown configuration fields.");
    }
}

/// <summary>Client for one configured resource. A reservation is still required for each query.</summary>
public sealed class InstrumentEndpointClient(InstrumentHostClient host, InstrumentRoute route)
{
    public InstrumentRoute Route { get; } = route;
    public Task<Contracts.ReservationGrant> ReserveAsync(CancellationToken ct = default) =>
        host.ReserveAsync(Route.ResourceId, ct);
    public Task<string> QueryAsync(Guid leaseId, string command, CancellationToken ct = default) =>
        host.QueryAsync(Route.ResourceId, leaseId, command, ct);
    public Task ReleaseAsync(Guid leaseId, CancellationToken ct = default) => host.ReleaseAsync(leaseId, ct);
}
