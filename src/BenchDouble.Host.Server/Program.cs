using BenchDouble.Host.Server.Heartbeat;
using BenchDouble.Host.Server.Lifecycle;
using BenchDouble.Host.Server.Recovery;
using BenchDouble.Host.Server;
using BenchDouble.Host.Server.Setup;
using BenchDouble;
using BenchDouble.Host.Contracts;
using System.Collections.Concurrent;
using BenchDouble.Observability;
using BenchDouble.Oscilloscopes;
using Microsoft.Extensions.Logging.Abstractions;

var builder = WebApplication.CreateBuilder(args);
// Intentionally loopback-only. Remote listeners require TLS and authentication first.
builder.WebHost.UseUrls("http://127.0.0.1:5278");
// One host-owned directory and single writer for all registered physical DMM ownership.
var setupPath = builder.Configuration["BenchSetup:Path"] ?? Path.Combine(builder.Environment.ContentRootPath, "build", "bench-setup.json");
builder.Services.AddSingleton<IHostOwnershipJournal>(_ => new FileHostOwnershipJournal(
    builder.Configuration["BenchSetup:OwnershipDirectory"] ?? setupPath + ".ownership"));
builder.Services.AddSingleton(HostHeartbeatPolicy.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<HostResourceRegistry>();
builder.Services.AddHostedService<OwnerHeartbeatService>();
var shutdownPolicy = HostShutdownPolicy.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(shutdownPolicy);
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = shutdownPolicy.Timeout + TimeSpan.FromSeconds(2));
builder.Services.AddHostedService<CanonicalShutdownService>();
builder.Services.AddSingleton<IviSetupCatalogService>();
builder.Services.AddSingleton<HostedIviDmmService>();
builder.Services.AddSingleton<HostedOscilloscopeRegistry>();
builder.Services.AddSingleton<ISetupDiscoveryProvider, SimulatedSetupDiscoveryProvider>();
builder.Services.AddSingleton(sp => new BenchSetupService(
    sp.GetRequiredService<HostResourceRegistry>(), sp.GetRequiredService<ISetupDiscoveryProvider>(),
    setupPath, sp.GetRequiredService<ILogger<BenchSetupService>>()));
var app = builder.Build();
// Registers metadata only. Integrators explicitly add IviDmmRegistration instances
// before Build; the stock host installs no vendor driver factories.
_ = app.Services.GetRequiredService<HostedIviDmmService>();
app.Use(async (context, next) =>
{
    if (app.Services.GetRequiredService<HostResourceRegistry>().IsDraining &&
        !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { error = "Host is stopping; new work is refused." });
        return;
    }
    await next(context);
});
app.MapBenchSetup();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/dashboard/"));

app.MapGet("/health", (HostResourceRegistry resources) => resources.IsDraining
    ? Results.Json(new { status = "stopping" }, statusCode: 503) : Results.Ok(new { status = "ready" }));
app.MapGet("/api/v1/monitor", (HostResourceRegistry resources) =>
    Results.Ok(resources.Monitor()));
app.MapGet("/api/v1/instruments", (HostResourceRegistry resources) =>
    Results.Ok(resources.List()));
app.MapPost("/api/v1/reservations", (ReservationRequest request, HostResourceRegistry resources) =>
    resources.Reserve(request.ResourceId) is { } grant
        ? Results.Ok(grant)
        : Results.Conflict(new HostError("busy_or_missing", "Instrument is unavailable.")));
app.MapDelete("/api/v1/reservations/{leaseId:guid}", (Guid leaseId, HostResourceRegistry resources) =>
{
    try { return resources.Release(leaseId) ? Results.NoContent() : Results.NotFound(); }
    catch (InvalidOperationException) { return Results.Conflict(new HostError("recovery_required", "Driver cleanup failed or recovery is required; ownership retained.")); }
});
app.MapPost("/api/v1/instruments/{resourceId}/query", (string resourceId, ScpiRequest request, HostResourceRegistry resources) =>
{
    try { return Results.Ok(new ScpiResponse(resources.Query(resourceId, request.LeaseId, request.Command))); }
    catch (UnauthorizedAccessException) { return Results.Json(new HostError("invalid_lease", "Lease invalid."), statusCode: 403); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (Exception ex) { return Results.BadRequest(new HostError("instrument_error", ex.Message)); }
});
// Local-only demonstration: all scope clients share the host-owned coordinator.
app.MapPost("/api/v1/scopes/{resourceId}/reserve", (string resourceId, HostedOscilloscopeRegistry scopes) =>
    scopes.Reserve(resourceId) is { } lease ? Results.Ok(new { leaseId = lease }) : Results.Conflict());
app.MapPost("/api/v1/scopes/{resourceId}/acquire", async (string resourceId, HostedScopeAcquisition request,
    HostedOscilloscopeRegistry scopes, CancellationToken cancellationToken) =>
{
    try
    {
        var capture = await scopes.AcquireAsync(resourceId, request.LeaseId, request.Channel, cancellationToken);
        return Results.Ok(new HostedScopeResult(capture.AcquisitionId, capture.Channel, capture.SampleCount,
            capture.Preamble, Enumerable.Range(0, Math.Min(16, capture.SampleCount))
                .Select(i => capture.VoltageAt(i).Volts).ToArray()));
    }
    catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (InvalidOperationException) { return Results.Conflict(); }
    catch (ArgumentOutOfRangeException) { return Results.BadRequest(); }
});
app.MapPost("/api/v1/scopes/{resourceId}/reserve/{leaseId:guid}/renew", (string resourceId, Guid leaseId,
    HostedOscilloscopeRegistry scopes) => scopes.Renew(resourceId, leaseId) ? Results.NoContent() : Results.StatusCode(403));
app.MapDelete("/api/v1/scopes/{resourceId}/reserve/{leaseId:guid}", (string resourceId, Guid leaseId,
    HostedOscilloscopeRegistry scopes) => scopes.Release(resourceId, leaseId) ? Results.NoContent() : Results.NotFound());
app.Run();

/// <summary>
/// Local-only demonstration with optional owner-heartbeat fencing and bounded shutdown decisions.
/// Remote identity/TLS and qualified physical safe-state behavior remain integrator responsibilities.
/// </summary>
public sealed partial class HostResourceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IScpiSession> _devices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DMM1"] = new MultimeterSimulator { InjectedVoltage = 5.0 },
        ["FGEN1"] = new GeneratorSimulator()
    };
    private readonly Dictionary<string, Guid> _reservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _byLease = new();
    private readonly List<HostActivity> _activity = new();
    private readonly Dictionary<string, InstrumentIdentification> _identities = new(StringComparer.OrdinalIgnoreCase);
    private readonly FailureIsolatedLogger<HostResourceRegistry> _logger;

    /// <summary>Number of diagnostic sink failures; logging failures never release an instrument.</summary>
    public long LoggingFailureCount => _logger.FailureCount;
    private const int MaxEvents = 200;

    /// <summary>Open the demo devices and identify once, before granting any lease.</summary>
    /// <param name="logger">Optional diagnostics; sink failures are isolated.</param>
    /// <param name="journal">Optional durable ownership journal; omission is for ephemeral tests and simulation.</param>
    /// <param name="heartbeatPolicy">Optional owner heartbeat enforcement; omission is for legacy/direct simulation callers.</param>
    /// <param name="timeProvider">Monotonic host clock with positive timestamp frequency when heartbeat enforcement is enabled; defaults to system time, independent of simulation.</param>
    public HostResourceRegistry(ILogger<HostResourceRegistry>? logger = null, IHostOwnershipJournal? journal = null,
        HostHeartbeatPolicy? heartbeatPolicy = null, TimeProvider? timeProvider = null)
    {
        heartbeatPolicy?.Validate(); _heartbeatPolicy = heartbeatPolicy; Clock = timeProvider ?? TimeProvider.System;
        if (heartbeatPolicy is not null && Clock.TimestampFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(timeProvider), "Heartbeat clock frequency must be positive.");
        _journal = journal;
        foreach (var record in journal?.Read() ?? [])
        {
            record.Validate();_ownership.Add(record.ResourceId, record);_startupQuarantine.Add(record.ResourceId);
            _restartGates.Add(record.RecordId, new SemaphoreSlim(1, 1));
        }
        _logger = new(logger ?? NullLogger<HostResourceRegistry>.Instance);
        foreach (var record in _ownership.Values)
            _logger.LogWarning(new EventId(7472, "OwnershipRestored"), "Startup quarantine {ResourceId} record {RecordId}", record.ResourceId, record.RecordId);
        foreach (var (resource, session) in _devices)
        {
            // Both registered simulators explicitly implement *IDN?. Never probe unknown hardware.
            var identity = InstrumentIdentificationReader.Capture(resource, "Simulated",
                () => session.Query("*IDN?"), _logger);
            _identities.Add(resource, identity);
            Record(resource, "Identified", identity.Response ?? identity.Status);
        }
    }

    /// <summary>Read-only local host snapshot; contains no raw SCPI commands or credentials.</summary>
    public HostMonitorSnapshot Monitor()
    {
        lock (_gate)
        {
            var instruments = List().Select(d => new HostInstrumentStatus(
                d.ResourceId, d.InstrumentClass, d.Backend, "localhost:5278",
                RecoveryRequired(d.ResourceId) ? "RecoveryRequired" : _reservations.ContainsKey(d.ResourceId) ? "Reserved" : "Idle",
                _activity.Where(a => a.ResourceId == d.ResourceId)
                    .TakeLast(15).ToArray(), _identities.GetValueOrDefault(d.ResourceId),
                "ExclusiveControl", _reservations.ContainsKey(d.ResourceId) || _startupQuarantine.Contains(d.ResourceId))).ToArray();
            return new HostMonitorSnapshot(DateTimeOffset.UtcNow, instruments);
        }
    }

    private void Record(string resourceId, string action, string message)
    {
        // Call only under _gate. Do not log command text or measurement payloads.
        _activity.Add(new HostActivity(DateTimeOffset.UtcNow, resourceId, action, message));
        _logger.LogInformation("Instrument {ResourceId}: {Action}; {Message}", resourceId, action, message);
        if (_activity.Count > MaxEvents) _activity.RemoveRange(0, _activity.Count - MaxEvents);
    }

    public InstrumentDescriptor[] List()
    {
        lock (_gate) return new[]
        {
            new InstrumentDescriptor("DMM1", "DigitalMultimeter", "Simulated"),
            new InstrumentDescriptor("FGEN1", "FunctionGenerator", "Simulated")
        }.Concat(_managedDescriptors.Values).Concat(_startupQuarantine
            .Where(id => !_devices.ContainsKey(id) && !_managedDescriptors.ContainsKey(id))
            .Select(id => new InstrumentDescriptor(id, "Unknown", "UnresolvedOwnership"))).ToArray();
    }

    public ReservationGrant? Reserve(string resourceId) => ReserveCore(resourceId, null);

    private ReservationGrant? ReserveCore(string resourceId, string? revision)
    {
        lock (_gate)
        {
            if (_draining) return null;
            if ((!_devices.ContainsKey(resourceId) && !_managedDescriptors.ContainsKey(resourceId)) || _reservations.ContainsKey(resourceId) || _startupQuarantine.Contains(resourceId)) return null;
            var heartbeatTimestamp = _heartbeatPolicy is null ? (long?)null : Clock.GetTimestamp();
            var nextOwnerGeneration = _heartbeatPolicy is null ? 0 : checked(_ownerGeneration + 1);
            if (_journal is not null && _managedDescriptors.ContainsKey(resourceId))
            {
                // A renamed or missing registration cannot prove that its routes are disjoint.
                // Keep physical grants blocked until every interrupted mapping is restored.
                if (_startupQuarantine.Any(id => !_routeDigests.TryGetValue(id, out var digest) || digest != _ownership[id].RoutesDigest))
                    throw new InvalidOperationException("Unresolved startup mappings block physical ownership; restore the recorded registrations first.");
                var record = new OwnershipRecord(resourceId, Guid.NewGuid(), _routeDigests[resourceId], revision, DateTimeOffset.UtcNow);
                try { _journal.Add(record); }
                catch (Exception ex)
                {
                    _logger.LogError(new EventId(7478, "OwnershipPersistenceFailed"), "Ownership persistence failed for {ResourceId}: {FailureKind}; dispatch refused", resourceId, ex.GetType().Name);
                    throw new InvalidOperationException("Ownership persistence failed; no driver was opened. Inspect the journal before retrying.");
                }
                _ownership.Add(resourceId, record);
                _logger.LogInformation(new EventId(7470, "OwnershipPersisted"), "Ownership recorded for {ResourceId} record {RecordId} before dispatch", resourceId, record.RecordId);
            }
            var lease = Guid.NewGuid();
            _reservations[resourceId] = lease;
            _byLease[lease] = resourceId;
            _leaseGates[lease] = new SemaphoreSlim(1, 1);
            if (_heartbeatPolicy is not null) { _ownerHealth.Add(lease, new(resourceId, nextOwnerGeneration, heartbeatTimestamp!.Value)); _ownerGeneration = nextOwnerGeneration; }
            Record(resourceId, "Reserved", "Exclusive local demonstration lease acquired");
            return new ReservationGrant(resourceId, lease);
        }
    }

    public bool Release(Guid lease) => ReleaseManagedOrPlain(lease);

    public string Query(string resourceId, Guid lease, string command)
    {
        lock (_gate)
        {
            EnsureAccepting(); EnsureHeartbeatLive(lease);
            if (!_devices.TryGetValue(resourceId, out var device)) throw new KeyNotFoundException();
            if (!_byLease.TryGetValue(lease, out var owned) ||
                !string.Equals(owned, resourceId, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException();
            var result = device.Query(command);
            Record(resourceId, "Query", "One instrument query completed");
            return result;
        }
    }
}


/// <summary>Public read-only host status for the operator dashboard.</summary>
public sealed record HostMonitorSnapshot(DateTimeOffset CapturedUtc,
    IReadOnlyList<HostInstrumentStatus> Instruments);
public sealed record HostInstrumentStatus(string ResourceId, string InstrumentClass,
    string Backend, string Host, string Status, IReadOnlyList<HostActivity> Activity,
    InstrumentIdentification? Identification = null, string AccessPolicy = "ExclusiveControl",
    bool IsLocked = false);
public sealed record HostActivity(DateTimeOffset TimestampUtc, string ResourceId,
    string Action, string Message);

