using BenchDouble.Host.Server.Recovery;
using BenchDouble.Host.Contracts;

/// <summary>Host-owned driver lifecycle attached to a canonical lease.</summary>
public interface IManagedHostResource : IDisposable
{
    /// <summary>Whether dispatch or cleanup failed and normal operations/release must remain blocked.</summary>
    bool RequiresRecovery { get; }
}

/// <summary>Managed-driver participation in the same canonical host lease authority.</summary>
public sealed partial class HostResourceRegistry
{
    private readonly Dictionary<string, InstrumentDescriptor> _managedDescriptors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, IManagedHostResource> _managed = new();
    private readonly Dictionary<Guid, SemaphoreSlim> _leaseGates = new();

    private readonly HashSet<Guid> _reconciled = new();
    private readonly HashSet<string> _cleanupPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, SemaphoreSlim> _restartGates = new();
    private readonly HashSet<string> _startupQuarantine = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHostOwnershipJournal? _journal;
    private readonly Dictionary<string, OwnershipRecord> _ownership = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _routeDigests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _managedRoutes = new(StringComparer.OrdinalIgnoreCase);

    internal void RegisterManaged(IReadOnlyDictionary<string, string[]> routes)
    {
        lock (_gate)
        {
            var ids = routes.Keys.ToArray();
            if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length ||
                ids.Any(id => _devices.ContainsKey(id) || _managedDescriptors.ContainsKey(id)))
                throw new ArgumentException("Canonical resource is already registered.");
            var proposed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, descriptors) in routes)
                foreach (var descriptor in descriptors.Distinct(StringComparer.OrdinalIgnoreCase))
                    if (_managedRoutes.ContainsKey(descriptor) || !proposed.TryAdd(descriptor, id))
                        throw new ArgumentException("A route is already registered to a canonical instrument.");
            foreach (var id in ids)
                _managedDescriptors.Add(id, new(id, "DigitalMultimeter", "RegisteredIVI"));
            foreach (var (descriptor, id) in proposed) _managedRoutes.Add(descriptor, id);
            foreach (var (id, descriptors) in routes) _routeDigests.Add(id, OwnershipRecord.DigestRoutes(descriptors));
        }
    }

    internal ReservationGrant? ReserveManaged(string resourceId, IManagedHostResource driver, string? revision = null)
    {
        lock (_gate)
        {
            var grant = ReserveCore(resourceId, revision);
            if (grant is not null) _managed.Add(grant.LeaseId, driver);
            return grant;
        }
    }

    internal T UseManaged<T>(string resourceId, Guid lease, Func<IManagedHostResource, T> operation,
        CancellationToken cancellationToken = default)
    {
        SemaphoreSlim gate;
        lock (_gate)
            gate = _leaseGates.GetValueOrDefault(lease) ?? throw new UnauthorizedAccessException("Lease invalid.");
        gate.Wait(cancellationToken);
        try
        {
            IManagedHostResource driver;
            lock (_gate)
            {
                if (!_byLease.TryGetValue(lease, out var owner) || !string.Equals(owner, resourceId, StringComparison.OrdinalIgnoreCase) ||
                    !_managed.TryGetValue(lease, out driver!)) throw new UnauthorizedAccessException("Lease invalid.");
            }
            // Cancellation before dispatch is safe. Once a synchronous driver call starts,
            // retain the lease gate until it actually returns, even if the caller disconnects.
            cancellationToken.ThrowIfCancellationRequested();
            return operation(driver);
        }
        finally { gate.Release(); }
    }

    internal T InspectManaged<T>(string resourceId, Guid lease, Func<IManagedHostResource, T> snapshot)
    {
        lock (_gate)
        {
            if (!_byLease.TryGetValue(lease, out var owner) || !string.Equals(owner, resourceId, StringComparison.OrdinalIgnoreCase) ||
                !_managed.TryGetValue(lease, out var driver)) throw new UnauthorizedAccessException("Lease invalid.");
            // Only immutable cached metadata; never acquire the vendor-call gate here.
            return snapshot(driver);
        }
    }

    /// <summary>Read unresolved startup evidence; this does not expose old leases or dispatch hardware I/O.</summary>
    /// <returns>Immutable records for explicit operator reconciliation, including unregistered resources.</returns>
    public OwnershipRecord[] PendingRecovery()
    {
        lock (_gate) return _startupQuarantine.Select(id => _ownership[id]).OrderBy(r => r.ResourceId, StringComparer.Ordinal).ToArray();
    }

    internal void ReconcileRestart(string resourceId, Guid recordId, Action recover, CancellationToken ct)
    {
        SemaphoreSlim gate;
        lock (_gate) gate = _restartGates.GetValueOrDefault(recordId) ?? throw new InvalidOperationException("Startup recovery review is stale.");
        gate.Wait(ct);
        try
        {
            lock (_gate)
            {
                if (!_startupQuarantine.Contains(resourceId) || !_ownership.TryGetValue(resourceId, out var record) || record.RecordId != recordId)
                    throw new InvalidOperationException("Startup recovery review is stale.");
                if (!_routeDigests.TryGetValue(resourceId, out var digest) || digest != record.RoutesDigest)
                    throw new InvalidOperationException("Registered routes changed; restore the reviewed mapping before reconciliation.");
            }
            ct.ThrowIfCancellationRequested();
            bool completed;
            lock (_gate) completed = _reconciled.Contains(recordId);
            if (!completed)
            {
                _logger.LogInformation(new EventId(7473, "RestartRecoveryStarted"), "Restart recovery {ResourceId} record {RecordId} started", resourceId, recordId);
                try { recover(); }
                catch (Exception ex)
                {
                    _logger.LogWarning(new EventId(7475, "RestartRecoveryFailed"), "Restart recovery {ResourceId} record {RecordId} failed: {FailureKind}; quarantine retained", resourceId, recordId, ex.GetType().Name);
                    throw;
                }
                lock (_gate) _reconciled.Add(recordId);
            }
            _journal!.Remove(resourceId, recordId);
            _logger.LogInformation(new EventId(7474, "RestartRecoveryCompleted"), "Restart recovery {ResourceId} record {RecordId} durably acknowledged", resourceId, recordId);
            lock (_gate)
            {
                _ownership.Remove(resourceId);_startupQuarantine.Remove(resourceId);_restartGates.Remove(recordId);_reconciled.Remove(recordId);
            }
        }
        finally { gate.Release(); }
    }

    internal bool PersistencePending(string resourceId) { lock (_gate) return _cleanupPending.Contains(resourceId); }

    private bool RecoveryRequired(string resourceId) => _startupQuarantine.Contains(resourceId) || _cleanupPending.Contains(resourceId) || _reservations.TryGetValue(resourceId, out var lease) &&
        _managed.TryGetValue(lease, out var driver) && driver.RequiresRecovery;

    private bool ReleaseManagedOrPlain(Guid lease)
    {
        SemaphoreSlim? gate;
        lock (_gate) gate = _leaseGates.GetValueOrDefault(lease);
        if (gate is null) return false;
        gate.Wait();
        try
        {
            string resource;
            IManagedHostResource? driver;
            lock (_gate)
            {
                if (!_byLease.TryGetValue(lease, out resource!)) return false;
                driver = _managed.GetValueOrDefault(lease);
            }
            try
            {
                driver?.Dispose();
                OwnershipRecord? record;
                lock (_gate) record = _ownership.GetValueOrDefault(resource);
                if (record is not null)
                {
                    lock (_gate) _cleanupPending.Add(resource);
                    _journal!.Remove(resource, record.RecordId);
                    _logger.LogInformation(new EventId(7471, "OwnershipCleared"), "Ownership cleared for {ResourceId} record {RecordId} after cleanup", resource, record.RecordId);
                }
            }
            catch
            {
                lock (_gate) Record(resource, "RecoveryRequired", "Driver cleanup did not complete; ownership retained");
                throw new InvalidOperationException("Driver cleanup failed or recovery is required; ownership retained.");
            }
            lock (_gate)
            {
                _ownership.Remove(resource);_cleanupPending.Remove(resource);
                _managed.Remove(lease);
                _leaseGates.Remove(lease);
                _byLease.Remove(lease);
                _reservations.Remove(resource);
                Record(resource, "Released", "Local demonstration lease released after driver cleanup");
            }
            return true;
        }
        finally { gate.Release(); }
    }
}
