using BenchDouble.Host.Contracts;
using BenchDouble.Host.Server.Heartbeat;
using BenchDouble.Host.Server.Lifecycle;
using BenchDouble.Host.Server.Recovery;
using System.Diagnostics;
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
/// <param name="ReadIdentity">Optional typed identity reader. Required only when the saved setup specifies expected fields; serial remains optional.</param>
/// <param name="SupportedFunctions">Qualified measurement functions; omission permits DC voltage only. Empty/invalid declarations are rejected.</param>
/// <param name="RecoverAfterRestart">Optional qualified procedure for reconciliation without a live vendor session; never inferred from normal recovery.</param>
/// <param name="PrepareForShutdown">Optional qualified abort/disable and safe-state confirmation on the live session; normal close follows only on success. No automatic recovery is inferred.</param>
public sealed record IviDmmRegistration(string ResourceId, SetupDriverBinding[] ApprovedBindings,
    Func<IviDmmOpenRequest, OwnedIviDmm> Open, Action<OwnedIviDmm?> Recover,
    Func<OwnedIviDmm, SetupIdentity>? ReadIdentity = null, IviMeasurementFunction[]? SupportedFunctions = null, Action? RecoverAfterRestart = null, Action<OwnedIviDmm>? PrepareForShutdown = null);

/// <summary>Activation outcome with the pinned configuration and retained lease, including failed initialization.</summary>
/// <param name="Lease">Canonical lease and route snapshot; never a second ownership authority.</param>
/// <param name="Active">True only after successful driver initialization.</param>
/// <param name="ErrorCode">Stable diagnostic on failure; vendor exception payloads are excluded.</param>
public sealed record IviDmmActivation(SetupLease Lease, bool Active, string? ErrorCode);

/// <summary>Cached session state; reading it never dispatches instrument I/O.</summary>
/// <param name="ResourceId">Canonical instrument ID.</param>
/// <param name="Revision">Pinned saved configuration revision.</param>
/// <param name="State">Opening, Ready, Busy, Stopping, Closing, Recovering, Closed, RecoveryRequired, OwnerExpired or JournalUpdateRequired.</param>
/// <param name="Function">Last successfully configured function, or null before configuration.</param>
/// <param name="RequestedRange">Last successfully requested range in base SI units, or provider default when null.</param>
/// <param name="RequestedResolution">Last successfully requested resolution in base SI units, or provider default when null.</param>
/// <param name="LastFailurePhase">Last failed provider operation; null before a failure. No vendor message is retained.</param>
/// <param name="CorrelationId">Diagnostic session correlation, distinct from the lease capability.</param>
/// <param name="DiagnosticWriteFailures">Observed lifecycle and registry logging failures, not a durable audit counter.</param>
/// <param name="Heartbeat">Cached control-owner health; independent of device readiness and never renewed by status inspection.</param>
/// <param name="LastDeviceSuccessUtc">Host UTC time of last successful device operation, not a hardware timestamp.</param>
/// <param name="LastReadingUtc">Host UTC receipt time of last successful reading; retained after faults.</param>
/// <param name="ReadingAgeMilliseconds">Monotonic age of the cached reading in milliseconds, or null before a reading.</param>
public sealed record IviDmmSessionStatus(string ResourceId, string Revision, string State, IviMeasurementFunction? Function,
    double? RequestedRange = null, double? RequestedResolution = null, string? LastFailurePhase = null,
    Guid CorrelationId = default, long DiagnosticWriteFailures = 0, OwnerHeartbeatSnapshot? Heartbeat = null,
    DateTimeOffset? LastDeviceSuccessUtc = null, DateTimeOffset? LastReadingUtc = null, double? ReadingAgeMilliseconds = null)
{
    /// <summary>Internal monotonic reading timestamp; excluded from public JSON.</summary>
    internal long? ReadingTimestamp { get; init; }
}

/// <summary>Operator-facing startup recovery evidence with registration readiness; contains no lease capability.</summary>
/// <param name="ResourceId">Canonical instrument.</param>
/// <param name="RecordId">Exact review record.</param>
/// <param name="Revision">Configuration revision at acquisition, if available.</param>
/// <param name="RecordedUtc">Host time when ownership was recorded.</param>
/// <param name="CanReconcile">Whether the registered routes and restart provider match this record.</param>
/// <param name="Code">Stable readiness code; not evidence of safe physical state.</param>
public sealed record RestartRecoveryItem(string ResourceId, Guid RecordId, string? Revision,
    DateTimeOffset RecordedUtc, bool CanReconcile, string Code);

/// <summary>Activates explicitly registered IVI DMM providers under canonical host ownership.</summary>
public sealed class HostedIviDmmService
{
    private readonly HostResourceRegistry _registry;
    private readonly BenchSetupService _setup;
    private readonly FailureIsolatedLogger<HostedIviDmmService> _logger;

    /// <summary>Number of failed diagnostic writes; these failures do not alter driver state.</summary>
    public long LoggingFailureCount => _logger.FailureCount;
    private readonly Dictionary<string, IviDmmRegistration> _registrations;

    /// <summary>Register provider metadata only; no vendor driver is constructed here.</summary>
    /// <param name="registry">Shared host ownership authority.</param>
    /// <param name="setup">Versioned saved setup.</param>
    /// <param name="registrations">Host-code allowlist; configuration XML cannot add factories.</param>
    /// <param name="logger">Structured lifecycle logging with no options, measurements or exception messages.</param>
    public HostedIviDmmService(HostResourceRegistry registry, BenchSetupService setup,
        IEnumerable<IviDmmRegistration> registrations, ILogger<HostedIviDmmService> logger)
    {
        _registry = registry; _setup = setup; _logger = new(logger);
        _registrations = new(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            if (registration is null || registration.ResourceId is null || !Regex.IsMatch(registration.ResourceId, @"\A[A-Za-z0-9_.-]{1,80}\z") ||
                registration.ApprovedBindings is not { Length: > 0 } ||
                registration.ApprovedBindings.Any(b => !BenchSetupService.ValidDriverBinding(b)) || registration.Open is null || registration.Recover is null)
                throw new ArgumentException("DMM registration requires a canonical ID, approved bindings, factory and recovery operation.");
            var functions = registration.SupportedFunctions ?? [IviMeasurementFunction.DcVoltage];
            if (functions.Length == 0 || functions.Any(f => !Enum.IsDefined(f)) || functions.Distinct().Count() != functions.Length)
                throw new ArgumentException("Provider functions must be nonempty, unique and defined.");
            _registrations.Add(registration.ResourceId, registration with
            { ApprovedBindings = Copy(registration.ApprovedBindings), SupportedFunctions = functions.ToArray() });
        }
        _registry.RegisterManaged(_registrations.ToDictionary(x => x.Key,
            x => x.Value.ApprovedBindings.Select(b => b.ResourceDescriptor).ToArray(), StringComparer.OrdinalIgnoreCase));
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
        if (!confirmed) throw Reject("confirmation_missing", "Confirm activation of the reviewed driver and route.");
        var saved = _setup.Read() ?? throw Reject("setup_missing", "Save and review the setup first.");
        if (saved.Revision != expectedRevision) throw Reject("revision_changed", "Setup changed. Review its saved revision before activation.");
        var assignment = saved.Configuration.Roles.SingleOrDefault(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase))
            ?? throw Reject("role_missing", "Configured role not found.");
        var instrument = saved.Configuration.Instruments.Single(i => i.Id.Equals(assignment.InstrumentId, StringComparison.OrdinalIgnoreCase));
        var route = instrument.Connections.Single(c => c.Id.Equals(instrument.SelectedConnection, StringComparison.OrdinalIgnoreCase));
        var binding = route.DriverBinding ?? throw Reject("binding_missing", "Selected route has no reviewed IVI binding.");
        if (!_registrations.TryGetValue(instrument.Id, out var registration) ||
            !registration.ApprovedBindings.Any(b => Key(b) == Key(binding)))
            throw Reject("binding_not_approved", "No host-registered DMM provider approves this exact route, source revision and settings.");
        var identity = instrument.ExpectedIdentity;
        var checkIdentity = identity is not null && new[] { identity.Manufacturer, identity.Model, identity.Serial }.Any(x => !string.IsNullOrWhiteSpace(x));
        if (checkIdentity && registration.ReadIdentity is null)
            throw Reject("identity_unsupported", "This provider has no identity reader for the expected fields; activation has not opened the instrument.");
        try
        {
            CheckRoute(saved.Configuration, route, binding);
            _setup.CheckActivationController(saved.Configuration, route);
        }
        catch (InvalidOperationException ex) { throw Reject("route_unconfirmed", ex.Message); }
        var runtime = new Runtime(registration, Copy(binding), saved.Revision, checkIdentity ? identity : null, _logger, _registry.Clock);
        var grant = _registry.ReserveManaged(instrument.Id, runtime, saved.Revision);
        if (grant is null) { _logger.LogInformation(new EventId(7450, "IviActivationBusy"), "IVI activation refused: resource {ResourceId} busy", registration.ResourceId); return null; }
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

    /// <summary>Read a detached copy of the provider's declared functions without opening a driver.</summary>
    /// <param name="resourceId">Canonical registered instrument ID.</param>
    /// <returns>Functions explicitly permitted by host registration; not a live capability probe.</returns>
    /// <exception cref="KeyNotFoundException">No provider is registered for the resource.</exception>
    public IviMeasurementFunction[] SupportedFunctions(string resourceId) =>
        _registrations.TryGetValue(resourceId, out var registration) ? registration.SupportedFunctions!.ToArray()
            : throw new KeyNotFoundException("No registered DMM provider.");

    /// <summary>Inspect cached state without waiting for or dispatching synchronous vendor I/O.</summary>
    /// <param name="resourceId">Canonical instrument.</param>
    /// <param name="leaseId">Existing activation capability; stale or mismatched leases are rejected.</param>
    /// <returns>Immutable state snapshot pinned to the original activation revision.</returns>
    /// <exception cref="UnauthorizedAccessException">The lease does not own this managed resource.</exception>
    public IviDmmSessionStatus Status(string resourceId, Guid leaseId)
    {
        var status = _registry.InspectManaged(resourceId, leaseId, d => ((Runtime)d).Status);
        var heartbeat = _registry.InspectOwnerHeartbeat(resourceId, leaseId);
        return status with { Heartbeat = heartbeat, State = _registry.PersistencePending(resourceId) ? "JournalUpdateRequired" : heartbeat?.State == "Expired" ? "OwnerExpired" : status.State,
            DiagnosticWriteFailures = LoggingFailureCount + _registry.LoggingFailureCount };
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
        _registry.UseManaged(resourceId, leaseId, d => ((Runtime)d).Configure(function, range, resolution), ct);
    }

    /// <summary>Read one measurement under the active lease; vendor failure blocks further dispatch.</summary>
    /// <param name="resourceId">Canonical resource.</param>
    /// <param name="leaseId">Activation lease.</param>
    /// <param name="ct">Cancellation before dispatch only.</param>
    /// <returns>Value in the configured measurement function's base SI unit.</returns>
    public double Measure(string resourceId, Guid leaseId, CancellationToken ct = default) =>
        MeasureWithMetadata(resourceId, leaseId, ct).Value;

    /// <summary>Measure and capture configuration provenance atomically under the lease gate.</summary>
    /// <param name="resourceId">Canonical instrument ID.</param>
    /// <param name="leaseId">Existing activation lease.</param>
    /// <param name="ct">Cancellation while queued or before dispatch; never aborts in-flight vendor I/O.</param>
    /// <returns>A finite reading with units, sequence and host receipt timestamp; no hardware timing claim.</returns>
    public IviDmmMeasurement MeasureWithMetadata(string resourceId, Guid leaseId, CancellationToken ct = default) =>
        _registry.UseManaged(resourceId, leaseId, d => ((Runtime)d).Measure(), ct);

    /// <summary>Run the registered provider's explicit recovery procedure, then release the retained lease.</summary>
    /// <param name="resourceId">Canonical resource.</param>
    /// <param name="leaseId">Failed activation or operation lease.</param>
    /// <param name="confirmed">Explicit operator authorization for provider-specific recovery.</param>
    /// <param name="ct">Cancels queued recovery only; dispatched recovery completes and releases ownership only on success.</param>
    public void Recover(string resourceId, Guid leaseId, bool confirmed, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!confirmed) throw new InvalidOperationException("Confirm provider-specific recovery first.");
        _registry.UseManaged(resourceId, leaseId, d => { ((Runtime)d).Recover(_registry.OwnerHeartbeatExpired(resourceId, leaseId)); return true; }, ct, allowExpired: true);
        _registry.ReleaseRecovered(leaseId);
    }

    /// <summary>Describe unresolved startup records, including missing or changed provider registrations.</summary>
    /// <returns>Read-only readiness metadata; no factory or recovery callback is invoked.</returns>
    public RestartRecoveryItem[] RecoveryOverview() => _registry.PendingRecovery().Select(record =>
    {
        var code = !_registrations.TryGetValue(record.ResourceId, out var registration) ? "provider_missing"
            : registration.RecoverAfterRestart is null ? "restart_provider_missing"
            : OwnershipRecord.DigestRoutes(registration.ApprovedBindings.Select(b => b.ResourceDescriptor)) != record.RoutesDigest ? "routes_changed" : "ready";
        return new RestartRecoveryItem(record.ResourceId, record.RecordId, record.Revision, record.RecordedUtc, code == "ready", code);
    }).ToArray();

    /// <summary>Reconcile unresolved startup ownership through an explicitly registered provider procedure.</summary>
    /// <param name="resourceId">Canonical instrument from the pending recovery list.</param>
    /// <param name="recordId">Exact unresolved record reviewed by the operator.</param>
    /// <param name="confirmed">Explicit confirmation of provider-specific physical reconciliation.</param>
    /// <param name="ct">Cancellation before dispatch; in-flight reconciliation cannot be interrupted.</param>
    /// <exception cref="InvalidOperationException">Review or provider is unavailable, or reconciliation did not complete.</exception>
    public void ReconcileRestart(string resourceId, Guid recordId, bool confirmed, CancellationToken ct = default)
    {
        if (!confirmed) throw new InvalidOperationException("Confirm restart reconciliation first.");
        if (!_registrations.TryGetValue(resourceId, out var registration) || registration.RecoverAfterRestart is null)
            throw new InvalidOperationException("No qualified restart recovery provider is registered.");
        try { _registry.ReconcileRestart(resourceId, recordId, registration.RecoverAfterRestart, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(new EventId(7476, "RestartReconciliationIncomplete"), "Restart reconciliation incomplete: {FailureKind}; quarantine retained", ex.GetType().Name);
            throw new InvalidOperationException("Restart reconciliation did not complete; ownership remains quarantined.");
        }
    }

    private InvalidOperationException Reject(string reason, string message)
    {
        _logger.LogWarning(new EventId(7458, "IviActivationRejected"),
            "IVI activation rejected before I/O: {Reason}", reason);
        return new InvalidOperationException(message);
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

    private sealed class Runtime(IviDmmRegistration registration, SetupDriverBinding binding, string revision, SetupIdentity? expectedIdentity,
        ILogger logger, TimeProvider clock) : IManagedShutdownResource
    {
        private readonly Guid _sessionId = Guid.NewGuid();
        private OwnedIviDmm? _driver;
        private volatile bool _faulted;
        private bool _closed;
        private long _sequence;
        private volatile IviDmmSessionStatus _status = new(registration.ResourceId, revision, "Opening", null);
        public IviDmmSessionStatus Status
        {
            get
            {
                var snapshot = _status;
                return snapshot with { CorrelationId = _sessionId, ReadingAgeMilliseconds = snapshot.ReadingTimestamp is { } timestamp
                    ? Math.Max(0, clock.GetElapsedTime(timestamp).TotalMilliseconds) : null };
            }
        }
        public bool RequiresRecovery => _faulted;

        public void Open()
        {
            logger.LogInformation(new EventId(7451, "IviOpening"), "Opening registered IVI DMM {ResourceId} session {SessionId} revision {ConfigurationRevision} with reset disabled", registration.ResourceId, _sessionId, revision);
            var phase = "Open";
            try
            {
                _driver = registration.Open(new(binding, Reset: false, IdQuery: true));
                if (_driver?.Bridge is null || _driver.Close is null) throw new InvalidOperationException();
                if (expectedIdentity is not null)
                {
                    phase = "Identity";
                    var actual = registration.ReadIdentity!(_driver);
                    bool Matches(string? expected, string? found, StringComparison comparison) =>
                        string.IsNullOrWhiteSpace(expected) || string.Equals(expected.Trim(), found?.Trim(), comparison);
                    if (actual is null || !Matches(expectedIdentity.Manufacturer, actual.Manufacturer, StringComparison.OrdinalIgnoreCase) ||
                        !Matches(expectedIdentity.Model, actual.Model, StringComparison.OrdinalIgnoreCase) ||
                        !Matches(expectedIdentity.Serial, actual.Serial, StringComparison.Ordinal))
                        throw new InvalidOperationException("Expected identity did not match.");
                    logger.LogInformation(new EventId(7459, "IviIdentityAccepted"),
                        "IVI {ResourceId} session {SessionId} expected identity fields matched", registration.ResourceId, _sessionId);
                }
                _status = _status with { State = "Ready", LastDeviceSuccessUtc = clock.GetUtcNow() };
                logger.LogInformation(new EventId(7452, "IviOpened"), "IVI {ResourceId} session {SessionId} initialization completed", registration.ResourceId, _sessionId);
            }
            catch (Exception ex) { Fault(ex, phase); throw new InvalidOperationException("IVI initialization failed; ownership retained for recovery."); }
        }

        public bool Configure(IviMeasurementFunction function, double? range, double? resolution)
        {
            if (!registration.SupportedFunctions!.Contains(function))
                throw new ArgumentException("Measurement function is not approved for this provider.");
            Run("Configure", driver =>
            {
                driver.Bridge.ConfigureMeasurement(function, range, resolution);
                _status = _status with { Function = function, RequestedRange = range, RequestedResolution = resolution };
                return true;
            });
            return true;
        }

        public IviDmmMeasurement Measure()
        {
            if (_status.Function is null) throw new InvalidOperationException("Configure a measurement function before measuring.");
            return Run("Measure", driver =>
            {
                var value = driver.Bridge.Measure();
                if (!double.IsFinite(value)) throw new InvalidOperationException("Non-finite DMM result requires provider interpretation.");
                var function = _status.Function.Value;
                var unit = function switch
                {
                    IviMeasurementFunction.DcVoltage or IviMeasurementFunction.AcVoltage => "V",
                    IviMeasurementFunction.DcCurrent or IviMeasurementFunction.AcCurrent => "A",
                    IviMeasurementFunction.Resistance => "ohm",
                    _ => throw new InvalidOperationException("Unknown measurement function.")
                };
                var receivedUtc = clock.GetUtcNow();
                _status = _status with { LastReadingUtc = receivedUtc, ReadingTimestamp = clock.GetTimestamp() };
                return new IviDmmMeasurement(registration.ResourceId, revision, _sessionId, value, function, unit,
                    checked(++_sequence), receivedUtc, _status.RequestedRange, _status.RequestedResolution);
            });
        }

        private T Run<T>(string operation, Func<OwnedIviDmm, T> action)
        {
            if (_closed || _faulted || _driver is null) throw new InvalidOperationException("IVI session is closed or requires recovery.");
            _status = _status with { State = "Busy" };
            var started = Stopwatch.GetTimestamp();
            logger.LogInformation(new EventId(7457, "IviOperationStarted"),
                "IVI {ResourceId} session {SessionId} operation {Operation} started", registration.ResourceId, _sessionId, operation);
            try
            {
                var value = action(_driver);
                _status = _status with { State = "Ready", LastDeviceSuccessUtc = clock.GetUtcNow() };
                logger.LogInformation(new EventId(7453, "IviOperationCompleted"), "IVI {ResourceId} session {SessionId} operation {Operation} completed in {ElapsedMilliseconds} ms", registration.ResourceId, _sessionId, operation, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                return value;
            }
            catch (Exception ex) { Fault(ex, operation); throw new InvalidOperationException("IVI operation failed; ownership retained for recovery."); }
        }

        public bool SupportsShutdown => _closed || registration.PrepareForShutdown is not null;

        public void PrepareForShutdown()
        {
            if (_closed) return;
            if (_faulted || _driver is null) throw new InvalidOperationException("Uncertain session requires explicit recovery; shutdown cannot infer safe state.");
            if (registration.PrepareForShutdown is null) throw new NotSupportedException("Qualified shutdown procedure missing.");
            _status = _status with { State = "Stopping" };
            var started = Stopwatch.GetTimestamp();
            logger.LogInformation(new EventId(7462, "IviShutdownPreparationStarted"),
                "IVI {ResourceId} session {SessionId} qualified shutdown preparation started", registration.ResourceId, _sessionId);
            try
            {
                registration.PrepareForShutdown(_driver);
                logger.LogInformation(new EventId(7463, "IviShutdownPreparationCompleted"),
                    "IVI {ResourceId} session {SessionId} qualified shutdown preparation completed in {ElapsedMilliseconds} ms",
                    registration.ResourceId, _sessionId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (Exception ex) { Fault(ex, "Shutdown"); throw new InvalidOperationException("Qualified shutdown failed; ownership retained."); }
        }

        public void Dispose()
        {
            if (_closed) return;
            if (_faulted) throw new InvalidOperationException("Explicit IVI recovery is required before release.");
            _status = _status with { State = "Closing" };
            var started = Stopwatch.GetTimestamp();
            logger.LogInformation(new EventId(7460, "IviCloseStarted"),
                "IVI {ResourceId} session {SessionId} close started", registration.ResourceId, _sessionId);
            try
            {
                _driver?.Close(); _driver = null; _closed = true;
                _status = _status with { State = "Closed" };
                logger.LogInformation(new EventId(7454, "IviClosed"), "IVI {ResourceId} session {SessionId} cleanup completed before lease release in {ElapsedMilliseconds} ms", registration.ResourceId, _sessionId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (Exception ex) { Fault(ex, "Close"); throw new InvalidOperationException("IVI cleanup failed; ownership retained for recovery."); }
        }

        public void Recover(bool ownerExpired = false)
        {
            if (_closed || !_faulted && !ownerExpired) throw new InvalidOperationException("Recovery requires a failed or owner-expired retained session.");
            _status = _status with { State = "Recovering" };
            var started = Stopwatch.GetTimestamp();
            logger.LogInformation(new EventId(7461, "IviRecoveryStarted"),
                "IVI {ResourceId} session {SessionId} recovery started", registration.ResourceId, _sessionId);
            try
            {
                registration.Recover(_driver); _driver = null; _closed = true; _faulted = false;
                _status = _status with { State = "Closed" };
                logger.LogInformation(new EventId(7456, "IviRecovered"), "Provider confirmed IVI {ResourceId} session {SessionId} cleanup and physical state recovery in {ElapsedMilliseconds} ms", registration.ResourceId, _sessionId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (Exception ex) { Fault(ex, "Recover"); throw new InvalidOperationException("IVI recovery failed; ownership retained."); }
        }

        private void Fault(Exception ex, string operation)
        {
            _faulted = true;
            _status = _status with { State = "RecoveryRequired", LastFailurePhase = operation };
            logger.LogWarning(new EventId(7455, "IviRecoveryRequired"), "IVI {ResourceId} session {SessionId} operation {Operation} failed: {FailureKind}; ownership retained", registration.ResourceId, _sessionId, operation, ex.GetType().Name);
        }
    }
}
