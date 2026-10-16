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
    private readonly Dictionary<Guid, object> _leaseGates = new();

    internal void RegisterManaged(string resourceId)
    {
        lock (_gate)
        {
            if (_devices.ContainsKey(resourceId) || _managedDescriptors.ContainsKey(resourceId))
                throw new ArgumentException("Canonical resource is already registered.");
            _managedDescriptors.Add(resourceId, new(resourceId, "DigitalMultimeter", "RegisteredIVI"));
        }
    }

    internal ReservationGrant? ReserveManaged(string resourceId, IManagedHostResource driver)
    {
        lock (_gate)
        {
            var grant = Reserve(resourceId);
            if (grant is not null) _managed.Add(grant.LeaseId, driver);
            return grant;
        }
    }

    internal T UseManaged<T>(string resourceId, Guid lease, Func<IManagedHostResource, T> operation,
        CancellationToken cancellationToken = default)
    {
        object gate;
        lock (_gate)
            gate = _leaseGates.GetValueOrDefault(lease) ?? throw new UnauthorizedAccessException("Lease invalid.");
        lock (gate)
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
    }

    private bool RecoveryRequired(string resourceId) => _reservations.TryGetValue(resourceId, out var lease) &&
        _managed.TryGetValue(lease, out var driver) && driver.RequiresRecovery;

    private bool ReleaseManagedOrPlain(Guid lease)
    {
        object? gate;
        lock (_gate) gate = _leaseGates.GetValueOrDefault(lease);
        if (gate is null) return false;
        lock (gate)
        {
            string resource;
            IManagedHostResource? driver;
            lock (_gate)
            {
                if (!_byLease.TryGetValue(lease, out resource!)) return false;
                driver = _managed.GetValueOrDefault(lease);
            }
            try { driver?.Dispose(); }
            catch
            {
                lock (_gate) Record(resource, "RecoveryRequired", "Driver cleanup did not complete; ownership retained");
                throw new InvalidOperationException("Driver cleanup failed or recovery is required; ownership retained.");
            }
            lock (_gate)
            {
                _managed.Remove(lease);
                _leaseGates.Remove(lease);
                _byLease.Remove(lease);
                _reservations.Remove(resource);
                Record(resource, "Released", "Local demonstration lease released after driver cleanup");
            }
            return true;
        }
    }
}
