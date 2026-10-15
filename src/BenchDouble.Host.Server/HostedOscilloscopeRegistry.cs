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
    private readonly Dictionary<string, DateTimeOffset> _expires = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _generations = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock;
    private readonly HostFencingLedger? _ledger;
    private readonly TimeSpan _leaseDuration;

    /// <summary>Create a local simulated host with configurable lease duration and test clock.</summary>
    public HostedOscilloscopeRegistry(TimeProvider? clock = null, TimeSpan? leaseDuration = null,
        HostFencingLedger? ledger = null)
    {
        _clock = clock ?? TimeProvider.System;
        _ledger = ledger;
        _leaseDuration = leaseDuration ?? TimeSpan.FromMinutes(2);
        if (_leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }

    /// <summary>Reserve the entire instrument for one client.</summary>
    public Guid? Reserve(string resourceId)
    {
        lock (_gate)
        {
            if (!_coordinators.ContainsKey(resourceId)) return null;
            if (_leases.ContainsKey(resourceId))
            {
                if (_inFlight.GetValueOrDefault(resourceId) != 0 || _expires[resourceId] > _clock.GetUtcNow()) return null;
                _leases.Remove(resourceId); // Expired idle lease; never preempt active I/O.
            }
            var id = Guid.NewGuid();
            _leases.Add(resourceId, id);
            _expires[resourceId] = _clock.GetUtcNow() + _leaseDuration;
            _generations[resourceId] = _ledger?.NextGeneration(resourceId) ??
                checked(_generations.GetValueOrDefault(resourceId) + 1);
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

    /// <summary>Renew an idle or active valid lease without changing its identity.</summary>
    public bool Renew(string resourceId, Guid leaseId)
    {
        lock (_gate)
        {
            if (!_leases.TryGetValue(resourceId, out var active) || active != leaseId ||
                _expires[resourceId] <= _clock.GetUtcNow()) return false;
            _expires[resourceId] = _clock.GetUtcNow() + _leaseDuration;
            return true;
        }
    }

    /// <summary>Current in-memory generation for diagnostics; not persisted fencing.</summary>
    public long Generation(string resourceId)
    {
        lock (_gate) return _generations.GetValueOrDefault(resourceId);
    }

    /// <summary>Run one host-owned coordinated acquisition after validating the lease.</summary>
    public async ValueTask<WaveformCapture> AcquireAsync(string resourceId, Guid leaseId, int channel,
        CancellationToken cancellationToken = default)
    {
        OscilloscopeSessionCoordinator coordinator;
        lock (_gate)
        {
            if (!_coordinators.TryGetValue(resourceId, out coordinator!)) throw new KeyNotFoundException();
            if (!_leases.TryGetValue(resourceId, out var active) || active != leaseId ||
                _expires[resourceId] <= _clock.GetUtcNow())
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
