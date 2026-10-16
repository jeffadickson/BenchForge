using System.Text.Json;
using System.Text.RegularExpressions;
using BenchDouble.Ivi;
using BenchDouble.Ivi.Official;

namespace BenchDouble.Host.Server.Setup;

/// <summary>A typed official IVI bridge and its exclusively host-owned cleanup operation.</summary>
/// <param name="Bridge">Adapter over an actual official IIviDmm implementation.</param>
/// <param name="Close">Closes the vendor session; must complete before the host releases ownership.</param>
public sealed record OwnedIviDmm(OfficialDmmBridge Bridge, Action Close);

/// <summary>Explicit initialization policy; imported settings are data for a reviewed provider to interpret.</summary>
/// <param name="Binding">Detached, exactly approved route and settings.</param>
/// <param name="Reset">Always false for this activation path.</param>
/// <param name="IdQuery">True: the provider must verify instrument compatibility during opening.</param>
public sealed record IviDmmOpenRequest(SetupDriverBinding Binding, bool Reset, bool IdQuery);

/// <summary>Host-code registration of one physical DMM, its approved routes and provider operations.</summary>
/// <param name="ResourceId">Canonical host resource; every approved alternate route shares its lease.</param>
/// <param name="ApprovedBindings">Exact source/options/route snapshots approved by the host integrator.</param>
/// <param name="Open">Typed provider factory. Must clean up partial allocations on failure; the host still retains ownership for recovery.</param>
/// <param name="Recover">Provider-specific recovery, including cleanup and physical state verification. Return only when ownership can safely be released.</param>
public sealed record IviDmmRegistration(string ResourceId, SetupDriverBinding[] ApprovedBindings,
    Func<IviDmmOpenRequest, OwnedIviDmm> Open, Action<OwnedIviDmm?> Recover);

/// <summary>Activation outcome with the pinned configuration and retained lease, including failed initialization.</summary>
/// <param name="Lease">Canonical lease and route snapshot; never a second ownership authority.</param>
/// <param name="Active">True only after successful driver initialization.</param>
/// <param name="ErrorCode">Stable diagnostic on failure; vendor exception payloads are excluded.</param>
public sealed record IviDmmActivation(SetupLease Lease, bool Active, string? ErrorCode);

/// <summary>Activates explicitly registered IVI DMM providers under canonical host ownership.</summary>
public sealed class HostedIviDmmService
{
    private readonly HostResourceRegistry _registry;
    private readonly BenchSetupService _setup;
    private readonly ILogger<HostedIviDmmService> _logger;
    private readonly Dictionary<string, IviDmmRegistration> _registrations;

    /// <summary>Register provider metadata only; no vendor driver is constructed here.</summary>
    /// <param name="registry">Shared host ownership authority.</param>
    /// <param name="setup">Versioned saved setup.</param>
    /// <param name="registrations">Host-code allowlist; configuration XML cannot add factories.</param>
    /// <param name="logger">Structured lifecycle logging with no options, measurements or exception messages.</param>
    public HostedIviDmmService(HostResourceRegistry registry, BenchSetupService setup,
        IEnumerable<IviDmmRegistration> registrations, ILogger<HostedIviDmmService> logger)
    {
        _registry = registry; _setup = setup; _logger = logger;
        _registrations = new(StringComparer.OrdinalIgnoreCase);
        var descriptors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            if (!Regex.IsMatch(registration.ResourceId, @"\A[A-Za-z0-9_.-]{1,80}\z") ||
                registration.ApprovedBindings is not { Length: > 0 } || registration.Open is null || registration.Recover is null)
                throw new ArgumentException("DMM registration requires a canonical ID, approved bindings, factory and recovery operation.");
            foreach (var descriptor in registration.ApprovedBindings.Select(b => b.ResourceDescriptor).Distinct(StringComparer.OrdinalIgnoreCase))
                if (!descriptors.Add(descriptor)) throw new ArgumentException("A descriptor cannot be approved for multiple canonical resources.");
            _registrations.Add(registration.ResourceId, registration with { ApprovedBindings = Copy(registration.ApprovedBindings) });
        }
        foreach (var registration in _registrations.Values) _registry.RegisterManaged(registration.ResourceId);
    }

    /// <summary>Explicitly activate one saved reviewed route, including an offline draft, against the host allowlist.</summary>
    /// <param name="role">Configured experiment role.</param>
    /// <param name="expectedRevision">Exact saved revision reviewed by the operator.</param>
    /// <param name="confirmed">Explicit activation confirmation, separate from configuration save.</param>
    /// <param name="cancellationToken">Honored before dispatch; cannot abort synchronous vendor I/O.</param>
    /// <returns>Active or recovery-required retained lease, or null if another owner holds the instrument.</returns>
    /// <exception cref="InvalidOperationException">Review, route or approved driver binding is missing or changed.</exception>
    public IviDmmActivation? Activate(string role, string expectedRevision, bool confirmed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!confirmed) throw new InvalidOperationException("Confirm activation of the reviewed driver and route.");
        var saved = _setup.Read() ?? throw new InvalidOperationException("Save and review the setup first.");
        if (saved.Revision != expectedRevision) throw new InvalidOperationException("Setup changed. Review its saved revision before activation.");
        var assignment = saved.Configuration.Roles.SingleOrDefault(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Configured role not found.");
        var instrument = saved.Configuration.Instruments.Single(i => i.Id.Equals(assignment.InstrumentId, StringComparison.OrdinalIgnoreCase));
        var route = instrument.Connections.Single(c => c.Id.Equals(instrument.SelectedConnection, StringComparison.OrdinalIgnoreCase));
        var binding = route.DriverBinding ?? throw new InvalidOperationException("Selected route has no reviewed IVI binding.");
        if (instrument.ExpectedIdentity is { } identity && new[] { identity.Manufacturer, identity.Model, identity.Serial }.Any(x => !string.IsNullOrWhiteSpace(x)))
            throw new InvalidOperationException("This provider path does not yet implement optional expected-identity checks; activation has not opened the instrument.");
        if (!_registrations.TryGetValue(instrument.Id, out var registration) ||
            !registration.ApprovedBindings.Any(b => Key(b) == Key(binding)))
            throw new InvalidOperationException("No host-registered DMM provider approves this exact route, source revision and settings.");
        CheckRoute(saved.Configuration, route, binding);
        _setup.CheckActivationController(saved.Configuration, route);
        var runtime = new Runtime(registration, Copy(binding), _logger);
        var grant = _registry.ReserveManaged(instrument.Id, runtime);
        if (grant is null) { _logger.LogInformation(new EventId(7450, "IviActivationBusy"), "IVI activation refused: resource busy"); return null; }
        var pinned = new SetupLease(grant.LeaseId, instrument.Id, saved.Revision, route);
        try
        {
            _registry.UseManaged(instrument.Id, grant.LeaseId, _ => { runtime.Open(); return true; }, cancellationToken);
            return new(pinned, true, null);
        }
        catch (OperationCanceledException) when (!runtime.RequiresRecovery)
        { _registry.Release(grant.LeaseId); throw; }
        catch (InvalidOperationException) when (runtime.RequiresRecovery)
        { return new(pinned, false, "recovery_required"); }
    }

    /// <summary>Configure a typed DMM operation under its existing lease.</summary>
    /// <param name="resourceId">Canonical resource.</param>
    /// <param name="leaseId">Activation lease.</param>
    /// <param name="function">Requested measurement function.</param>
    /// <param name="range">Optional positive range in the function's base SI unit.</param>
    /// <param name="resolution">Optional positive resolution in the function's base SI unit.</param>
    /// <param name="ct">Cancellation before dispatch only.</param>
    public void Configure(string resourceId, Guid leaseId, IviMeasurementFunction function,
        double? range = null, double? resolution = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(function) || range is { } r && (!double.IsFinite(r) || r <= 0) ||
            resolution is { } v && (!double.IsFinite(v) || v <= 0)) throw new ArgumentException("Invalid DMM function, range or resolution.");
        _registry.UseManaged(resourceId, leaseId, d => ((Runtime)d).Run(driver =>
        { driver.Bridge.ConfigureMeasurement(function, range, resolution); return true; }), ct);
    }

    /// <summary>Read one measurement under the active lease; vendor failure blocks further dispatch.</summary>
    /// <param name="resourceId">Canonical resource.</param>
    /// <param name="leaseId">Activation lease.</param>
    /// <param name="ct">Cancellation before dispatch only.</param>
    /// <returns>Value in the configured measurement function's base SI unit.</returns>
    public double Measure(string resourceId, Guid leaseId, CancellationToken ct = default) =>
        _registry.UseManaged(resourceId, leaseId, d => ((Runtime)d).Run(driver =>
        {
            var value = driver.Bridge.Measure();
            if (!double.IsFinite(value)) throw new InvalidOperationException("Non-finite DMM result requires provider interpretation.");
            return value;
        }), ct);

    /// <summary>Run the registered provider's explicit recovery procedure, then release the retained lease.</summary>
    /// <param name="resourceId">Canonical resource.</param>
    /// <param name="leaseId">Failed activation or operation lease.</param>
    /// <param name="confirmed">Explicit operator authorization for provider-specific recovery.</param>
    public void Recover(string resourceId, Guid leaseId, bool confirmed)
    {
        if (!confirmed) throw new InvalidOperationException("Confirm provider-specific recovery first.");
        _registry.UseManaged(resourceId, leaseId, d => { ((Runtime)d).Recover(); return true; });
        _registry.Release(leaseId);
    }

    private static void CheckRoute(BenchSetup config, SetupConnection route, SetupDriverBinding binding)
    {
        if (binding.ResourceDescriptor.StartsWith("GPIB", StringComparison.OrdinalIgnoreCase) &&
            !route.Transport.Equals("GPIB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("GPIB activation requires an explicitly confirmed controller-scoped route.");
        var expected = route.Transport.Equals("GPIB", StringComparison.OrdinalIgnoreCase)
            ? config.Controllers.Single(c => c.Id.Equals(route.ControllerId, StringComparison.OrdinalIgnoreCase)).Address + "::" + route.Address + "::INSTR"
            : route.Address;
        if (!route.Provider.Equals("visa", StringComparison.OrdinalIgnoreCase) ||
            !expected.Equals(binding.ResourceDescriptor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Route no longer matches its reviewed IVI resource descriptor.");
    }

    private static string Key(SetupDriverBinding b) => JsonSerializer.Serialize(new
    { b.SourceRevision, b.LogicalName, b.Session, b.Module, b.ResourceDescriptor,
        Settings = b.Settings.OrderBy(x => x.Key, StringComparer.Ordinal),
        VirtualNames = b.VirtualNames.OrderBy(x => x.Key, StringComparer.Ordinal) });
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private sealed class Runtime(IviDmmRegistration registration, SetupDriverBinding binding,
        ILogger logger) : IManagedHostResource
    {
        private OwnedIviDmm? _driver;
        private volatile bool _faulted;
        private bool _closed;
        public bool RequiresRecovery => _faulted;

        public void Open()
        {
            logger.LogInformation(new EventId(7451, "IviOpening"), "Opening registered IVI DMM {ResourceId} with reset disabled", registration.ResourceId);
            try
            {
                _driver = registration.Open(new(binding, Reset: false, IdQuery: true));
                if (_driver?.Bridge is null || _driver.Close is null) throw new InvalidOperationException();
                logger.LogInformation(new EventId(7452, "IviOpened"), "Registered IVI DMM {ResourceId} initialization completed", registration.ResourceId);
            }
            catch (Exception ex) { Fault(ex); throw new InvalidOperationException("IVI initialization failed; ownership retained for recovery."); }
        }

        public T Run<T>(Func<OwnedIviDmm, T> action)
        {
            if (_closed || _faulted || _driver is null) throw new InvalidOperationException("IVI session is closed or requires recovery.");
            try
            {
                var value = action(_driver);
                logger.LogInformation(new EventId(7453, "IviOperationCompleted"), "Typed IVI DMM {ResourceId} operation completed", registration.ResourceId);
                return value;
            }
            catch (Exception ex) { Fault(ex); throw new InvalidOperationException("IVI operation failed; ownership retained for recovery."); }
        }

        public void Dispose()
        {
            if (_closed) return;
            if (_faulted) throw new InvalidOperationException("Explicit IVI recovery is required before release.");
            try
            {
                _driver?.Close(); _driver = null; _closed = true;
                logger.LogInformation(new EventId(7454, "IviClosed"), "IVI driver {ResourceId} cleanup completed before lease release", registration.ResourceId);
            }
            catch (Exception ex) { Fault(ex); throw new InvalidOperationException("IVI cleanup failed; ownership retained for recovery."); }
        }

        public void Recover()
        {
            if (_closed || !_faulted) throw new InvalidOperationException("Recovery requires a failed retained session.");
            try
            {
                registration.Recover(_driver); _driver = null; _closed = true; _faulted = false;
                logger.LogInformation(new EventId(7456, "IviRecovered"), "Provider confirmed IVI {ResourceId} cleanup and physical state recovery", registration.ResourceId);
            }
            catch (Exception ex) { Fault(ex); throw new InvalidOperationException("IVI recovery failed; ownership retained."); }
        }

        private void Fault(Exception ex)
        {
            _faulted = true;
            logger.LogWarning(new EventId(7455, "IviRecoveryRequired"), "IVI {ResourceId} lifecycle failed: {FailureKind}; ownership retained", registration.ResourceId, ex.GetType().Name);
        }
    }
}
