using BenchDouble.Oscilloscopes;

namespace BenchDouble;

/// <summary>
/// Single-process loopback demonstration of host-authoritative scope reservations.
/// No network authentication, durable fencing, lease expiry or physical safe-state guarantees.
/// </summary>
public sealed class HostedOscilloscopeRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OscilloscopeSessionCoordinator> _coordinators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SCOPE1"] = new(new SimulatedOscilloscopeSession(channels: 2, samples: 128))
        };
    private readonly Dictionary<string, Guid> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reserve the entire instrument for one client.</summary>
    public Guid? Reserve(string resourceId)
    {
        lock (_gate)
        {
            if (!_coordinators.ContainsKey(resourceId) || _leases.ContainsKey(resourceId)) return null;
            var id = Guid.NewGuid();
            _leases.Add(resourceId, id);
            return id;
        }
    }

    /// <summary>
    /// Release only the matching lease. Host lock remains held until any active
    /// acquisition completes, preventing reassignment during in-flight I/O.
    /// </summary>
    public bool Release(string resourceId, Guid leaseId)
    {
        lock (_gate)
        {
            if (!_leases.TryGetValue(resourceId, out var active) || active != leaseId ||
                _inFlight.GetValueOrDefault(resourceId) != 0) return false;
            _leases.Remove(resourceId);
            return true;
        }
    }

    /// <summary>Run one host-owned coordinated acquisition after validating the lease.</summary>
    public async ValueTask<WaveformCapture> AcquireAsync(string resourceId, Guid leaseId, int channel,
        CancellationToken cancellationToken = default)
    {
        OscilloscopeSessionCoordinator coordinator;
        lock (_gate)
        {
            if (!_coordinators.TryGetValue(resourceId, out coordinator!)) throw new KeyNotFoundException();
            if (!_leases.TryGetValue(resourceId, out var active) || active != leaseId)
                throw new UnauthorizedAccessException();
            _inFlight[resourceId] = _inFlight.GetValueOrDefault(resourceId) + 1;
        }
        try
        {
            return await new CoordinatedOscilloscope(coordinator).AcquireAsync(channel, Guid.NewGuid(), cancellationToken);
        }
        finally
        {
            lock (_gate) _inFlight[resourceId]--;
        }
    }
}
