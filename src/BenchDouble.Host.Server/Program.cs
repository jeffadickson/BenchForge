using BenchDouble;
using BenchDouble.Host.Contracts;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
// Intentionally loopback-only. Remote listeners require TLS and authentication first.
builder.WebHost.UseUrls("http://127.0.0.1:5278");
builder.Services.AddSingleton<HostResourceRegistry>();
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapGet("/api/v1/instruments", (HostResourceRegistry resources) =>
    Results.Ok(resources.List()));
app.MapPost("/api/v1/reservations", (ReservationRequest request, HostResourceRegistry resources) =>
    resources.Reserve(request.ResourceId) is { } grant
        ? Results.Ok(grant)
        : Results.Conflict(new HostError("busy_or_missing", "Instrument is unavailable.")));
app.MapDelete("/api/v1/reservations/{leaseId:guid}", (Guid leaseId, HostResourceRegistry resources) =>
    resources.Release(leaseId) ? Results.NoContent() : Results.NotFound());
app.MapPost("/api/v1/instruments/{resourceId}/query", (string resourceId, ScpiRequest request, HostResourceRegistry resources) =>
{
    try { return Results.Ok(new ScpiResponse(resources.Query(resourceId, request.LeaseId, request.Command))); }
    catch (UnauthorizedAccessException) { return Results.Json(new HostError("invalid_lease", "Lease invalid."), statusCode: 403); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (Exception ex) { return Results.BadRequest(new HostError("instrument_error", ex.Message)); }
});
app.Run();

/// <summary>
/// Minimal local-only demonstration. Not a production lease service: no expiry,
/// authenticated identity, persistence, safe-state recovery, or remote TLS.
/// </summary>
public sealed class HostResourceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IScpiSession> _devices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DMM1"] = new MultimeterSimulator { InjectedVoltage = 5.0 },
        ["FGEN1"] = new GeneratorSimulator()
    };
    private readonly Dictionary<string, Guid> _reservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _byLease = new();

    public InstrumentDescriptor[] List() => new[]
    {
        new InstrumentDescriptor("DMM1", "DigitalMultimeter", "Simulated"),
        new InstrumentDescriptor("FGEN1", "FunctionGenerator", "Simulated")
    };

    public ReservationGrant? Reserve(string resourceId)
    {
        lock (_gate)
        {
            if (!_devices.ContainsKey(resourceId) || _reservations.ContainsKey(resourceId)) return null;
            var lease = Guid.NewGuid();
            _reservations[resourceId] = lease;
            _byLease[lease] = resourceId;
            return new ReservationGrant(resourceId, lease);
        }
    }

    public bool Release(Guid lease)
    {
        lock (_gate)
        {
            if (!_byLease.Remove(lease, out var resource)) return false;
            _reservations.Remove(resource);
            return true;
        }
    }

    public string Query(string resourceId, Guid lease, string command)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(resourceId, out var device)) throw new KeyNotFoundException();
            if (!_byLease.TryGetValue(lease, out var owned) ||
                !string.Equals(owned, resourceId, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException();
            return device.Query(command);
        }
    }
}
