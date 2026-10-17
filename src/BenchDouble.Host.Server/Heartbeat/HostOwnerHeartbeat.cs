using BenchDouble.Host.Contracts;
using BenchDouble.Host.Server.Heartbeat;

/// <summary>Control-owner heartbeat state in the existing canonical registry; observers never extend ownership.</summary>
public sealed partial class HostResourceRegistry
{
    private readonly Guid _hostEpoch = Guid.NewGuid();
    private long _ownerGeneration;
    private readonly Dictionary<Guid, OwnerHealth> _ownerHealth = new();
    private sealed class OwnerHealth(string resourceId, long generation, long timestamp)
    {
        public readonly string ResourceId = resourceId;
        public readonly long Generation = generation;
        public long LastTimestamp = timestamp;
        public Guid PeerEpoch;
        public long Sequence;
        public string State = "Healthy";
    }
    private readonly HostHeartbeatPolicy? _heartbeatPolicy;
    internal TimeProvider Clock { get; }

    /// <summary>Process incarnation identifier for detecting stale health messages; not a control capability.</summary>
    public Guid HostEpoch => _hostEpoch;

    /// <summary>Inspect cached owner health without renewing ownership or contacting a device.</summary>
    /// <param name="resourceId">Canonical instrument.</param>
    /// <param name="leaseId">Current retained control capability.</param>
    /// <returns>Health snapshot, or null when heartbeat enforcement is disabled for this registry.</returns>
    /// <exception cref="UnauthorizedAccessException">Lease is absent or belongs to another resource.</exception>
    public OwnerHeartbeatSnapshot? InspectOwnerHeartbeat(string resourceId, Guid leaseId)
    {
        lock (_gate)
        {
            CheckHeartbeatOwner(resourceId, leaseId);
            if (!_ownerHealth.TryGetValue(leaseId, out var health)) return null;
            EvaluateHealth(health);
            return Snapshot(health);
        }
    }

    /// <summary>Renew one owner using an exact process epoch, generation and increasing peer sequence; never performs device I/O.</summary>
    /// <param name="resourceId">Canonical instrument.</param>
    /// <param name="leaseId">Current exclusive capability.</param>
    /// <param name="hostEpoch">Host epoch from a lease-scoped snapshot.</param>
    /// <param name="ownerGeneration">Current ownership generation.</param>
    /// <param name="peerEpoch">Nonempty client incarnation, fixed for this lease after first renewal.</param>
    /// <param name="sequence">Positive strictly increasing sequence; duplicates never extend the deadline.</param>
    /// <returns>Accepted renewal snapshot; acknowledgment does not attest instrument readiness.</returns>
    /// <exception cref="UnauthorizedAccessException">Lease, epoch or generation is stale.</exception>
    /// <exception cref="InvalidOperationException">Host is draining, heartbeat is disabled, peer changed or sequence is replayed.</exception>
    public OwnerHeartbeatSnapshot RenewOwnerHeartbeat(string resourceId, Guid leaseId, Guid hostEpoch,
        long ownerGeneration, Guid peerEpoch, long sequence)
    {
        lock (_gate)
        {
            EnsureAccepting(); CheckHeartbeatOwner(resourceId, leaseId);
            if (!_ownerHealth.TryGetValue(leaseId, out var health)) throw new InvalidOperationException("Owner heartbeat is not enabled.");
            EvaluateHealth(health);
            if (health.State == "Expired") throw new InvalidOperationException("Owner heartbeat expired; explicit recovery is required.");
            if (hostEpoch != _hostEpoch || ownerGeneration != health.Generation) throw new UnauthorizedAccessException("Heartbeat ownership is stale.");
            if (peerEpoch == Guid.Empty || sequence <= 0 || sequence <= health.Sequence ||
                health.PeerEpoch != Guid.Empty && health.PeerEpoch != peerEpoch)
                throw new InvalidOperationException("Heartbeat peer or sequence is invalid.");
            var established = health.Sequence == 0; var priorState = health.State;
            health.PeerEpoch = peerEpoch; health.Sequence = sequence; health.LastTimestamp = Clock.GetTimestamp(); health.State = "Healthy";
            if (established || priorState == "Suspect")
                _logger.LogInformation(new EventId(established ? 7493 : 7492, established ? "OwnerHeartbeatEstablished" : "OwnerHeartbeatRestored"),
                    "Owner heartbeat {ResourceId} generation {OwnerGeneration} is healthy", health.ResourceId, health.Generation);
            return Snapshot(health);
        }
    }

    /// <summary>Advance cached heartbeat states without device I/O, renewal, cleanup or ownership reassignment.</summary>
    /// <returns>Number of currently fenced retained owners.</returns>
    public int ObserveOwnerHeartbeats()
    {
        lock (_gate)
        {
            foreach (var health in _ownerHealth.Values) EvaluateHealth(health);
            return _ownerHealth.Values.Count(h => h.State == "Expired");
        }
    }

    private void EvaluateHealth(OwnerHealth health)
    {
        if (health.State == "Expired") return;
        var age = Clock.GetElapsedTime(health.LastTimestamp);
        var previous = health.State;
        health.State = age >= _heartbeatPolicy!.ExpireAfter ? "Expired" : age >= _heartbeatPolicy.SuspectAfter ? "Suspect" : "Healthy";
        if (health.State != previous)
            _logger.LogWarning(new EventId(health.State == "Expired" ? 7491 : 7490, "OwnerHeartbeatChanged"),
                "Owner heartbeat {ResourceId} generation {OwnerGeneration}: {HealthState}; loss policy FenceAndRetain",
                health.ResourceId, health.Generation, health.State);
    }

    internal bool OwnerHeartbeatExpired(string resourceId, Guid leaseId)
    {
        lock (_gate) { CheckHeartbeatOwner(resourceId, leaseId); return HeartbeatExpired(leaseId); }
    }

    internal bool ReleaseRecovered(Guid leaseId) => ReleaseManagedOrPlain(leaseId, recovered: true);

    private bool HeartbeatExpired(Guid leaseId)
    {
        if (!_ownerHealth.TryGetValue(leaseId, out var health)) return false;
        EvaluateHealth(health); return health.State == "Expired";
    }

    private void EnsureHeartbeatLive(Guid leaseId)
    {
        if (HeartbeatExpired(leaseId)) throw new InvalidOperationException("Owner heartbeat expired; ownership retained for explicit recovery.");
    }

    private void CheckHeartbeatOwner(string resourceId, Guid leaseId)
    {
        if (!_byLease.TryGetValue(leaseId, out var owner) || !owner.Equals(resourceId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Lease invalid.");
    }

    private OwnerHeartbeatSnapshot Snapshot(OwnerHealth health) => new(_hostEpoch, health.Generation, health.PeerEpoch,
        health.Sequence, health.State, Math.Max(0, Clock.GetElapsedTime(health.LastTimestamp).TotalMilliseconds),
        _heartbeatPolicy!.Interval.TotalMilliseconds, _heartbeatPolicy.ExpireAfter.TotalMilliseconds, "FenceAndRetain");
}
