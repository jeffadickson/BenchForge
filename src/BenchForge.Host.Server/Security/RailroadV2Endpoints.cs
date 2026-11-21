using System.Collections.Concurrent;
using BenchForge.Async;
using BenchForge.Contracts;
using BenchForge.Flight;
using BenchForge.Host.Contracts;
using BenchForge.Host.Server.Recovery;
using BenchForge.Host.Server.Security;
using BenchForge.Outcomes;
using UnitsNet;

namespace BenchForge.Host.Server.Security;

/// <summary>
/// Coordinates principal-bound v2 leases, canonical fenced command dispatch, durable command receipts,
/// fail-safe instrument hard-limit enforcement, and hash-chained audit logging on top of the single authoritative <see cref="HostResourceRegistry"/>.
/// </summary>
public sealed class RailroadHostService : IDisposable
{
    private readonly HostResourceRegistry _registry;
    private readonly PrivateLabSecurityPolicy _security;
    private readonly HashChainedAuditLog _auditLog;
    private readonly string _auditLogPath;
    private readonly HostFencingLedger _fencingLedger;
    private readonly FileCommandLedger _commandLedger;
    private readonly HardLimitRegistry _hardLimits = new();
    private readonly HardLimitScpiGuard _hardLimitGuard;
    private readonly ConcurrentDictionary<Guid, RemoteReservationGrant> _v2Leases = new();
    private readonly ConcurrentDictionary<string, Guid> _activeLeaseByResource = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CanonicalCommandDispatcher> _dispatchers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Create a v2 railroad host coordinator bound to the canonical registry and durable ledgers.</summary>
    /// <param name="registry">Single authoritative resource registry.</param>
    /// <param name="security">Private-lab RBAC, token, and raw-SCPI policy.</param>
    /// <param name="auditLogPath">Path to the append-only SHA-256 hash-chained audit log.</param>
    /// <param name="fencingLedgerPath">Path to the durable host epoch and generation ledger.</param>
    /// <param name="commandLedgerPath">Path to the durable single-writer command receipt ledger.</param>
    public RailroadHostService(
        HostResourceRegistry registry,
        PrivateLabSecurityPolicy security,
        string auditLogPath,
        string fencingLedgerPath,
        string commandLedgerPath)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        ArgumentException.ThrowIfNullOrWhiteSpace(auditLogPath);
        _auditLogPath = Path.GetFullPath(auditLogPath);
        _auditLog = new HashChainedAuditLog(_auditLogPath);
        _fencingLedger = new HostFencingLedger(fencingLedgerPath);
        _commandLedger = new FileCommandLedger(commandLedgerPath);
        _hardLimitGuard = _hardLimits.ToScpiGuard(enforceOnSimulation: true, requireRegisteredLimitsForPhysical: false);
    }

    /// <summary>Active private-lab security policy enforced by this host service.</summary>
    public PrivateLabSecurityPolicy Security => _security;

    /// <summary>Fail-safe instrument hard-limit registry enforced prior to remote command dispatch (tighten-only once registered).</summary>
    public HardLimitRegistry HardLimits => _hardLimits;

    /// <summary>Authenticate a request token, or synthesize a loopback-local operator principal when no principals are configured on loopback.</summary>
    /// <param name="rawToken">Bearer token extracted from <c>X-BenchForge-Token</c> or <c>Authorization</c> header.</param>
    /// <param name="isLoopbackRequest">True when the HTTP connection originated from loopback (<c>127.0.0.1</c> / <c>::1</c>).</param>
    /// <returns>Railroad outcome containing the authenticated <see cref="LabPrincipal"/> or <see cref="BenchErrorKind.Unauthorized"/>.</returns>
    public BenchResult<LabPrincipal> Authenticate(string? rawToken, bool isLoopbackRequest)
    {
        var tokenResult = _security.AuthenticateToken(rawToken);
        if (tokenResult.IsSuccess)
            return tokenResult;

        if (isLoopbackRequest && !_security.AllowPrivateLanBinding && string.IsNullOrWhiteSpace(rawToken))
        {
            return BenchResult<LabPrincipal>.Ok(new LabPrincipal(
                "loopback-local",
                LabRole.BenchAdmin,
                string.Empty,
                ["*"],
                AllowRawScpi: _security.AllowRawScpiGlobal));
        }

        _auditLog.Append("anonymous", "Authenticate", null, $"Denied:{tokenResult.Error.Code}");
        return tokenResult;
    }

    /// <summary>Return the machine-readable bench manifest for human and AI-agent discovery.</summary>
    /// <param name="principal">Authenticated caller principal.</param>
    /// <returns>Railroad result containing <see cref="RemoteBenchManifest"/>.</returns>
    public BenchResult<RemoteBenchManifest> Describe(LabPrincipal principal)
    {
        var authz = _security.Authorize(principal, LabHostAction.Observe);
        if (authz.IsFailure)
            return BenchResult<RemoteBenchManifest>.Fail(authz.Error);

        var instruments = _registry.List()
            .Where(i => principal.CanAccessResource(i.ResourceId))
            .ToArray();

        return BenchResult<RemoteBenchManifest>.Ok(new RemoteBenchManifest(
            Environment.MachineName,
            "2.0",
            _security.AllowRawScpiGlobal && principal.AllowRawScpi,
            instruments));
    }

    /// <summary>Reserve a resource exclusively under <paramref name="principal"/> with a durable epoch/generation fence.</summary>
    /// <param name="principal">Authenticated caller principal.</param>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <returns>Railroad result containing the principal-bound <see cref="RemoteReservationGrant"/>.</returns>
    public BenchResult<RemoteReservationGrant> Reserve(LabPrincipal principal, string resourceId)
    {
        var authz = _security.Authorize(principal, LabHostAction.Reserve, resourceId);
        if (authz.IsFailure)
        {
            _auditLog.Append(principal.PrincipalId, "Reserve", resourceId, $"Denied:{authz.Error.Code}");
            return BenchResult<RemoteReservationGrant>.Fail(authz.Error);
        }

        var grant = _registry.Reserve(resourceId);
        if (grant is null)
        {
            _auditLog.Append(principal.PrincipalId, "Reserve", resourceId, "Conflict:BusyOrMissing");
            return BenchResult<RemoteReservationGrant>.Fail(new BenchError(
                "BF-HOST-UNAVAILABLE",
                BenchErrorKind.LeaseFenced,
                $"Resource '{resourceId}' is currently reserved, quarantined, or missing.",
                resourceId,
                Remedy: "Inspect /api/v2/bench/describe or await active lease release."));
        }

        long generation = _fencingLedger.NextGeneration(resourceId);
        var fencedGrant = new RemoteReservationGrant(
            grant.ResourceId,
            grant.LeaseId,
            _fencingLedger.Epoch,
            generation,
            principal.PrincipalId);
        _v2Leases[grant.LeaseId] = fencedGrant;
        _activeLeaseByResource[grant.ResourceId] = grant.LeaseId;
        _auditLog.Append(principal.PrincipalId, "Reserve", resourceId, $"Granted:Epoch={fencedGrant.HostEpoch},Gen={generation}");
        return BenchResult<RemoteReservationGrant>.Ok(fencedGrant);
    }

    /// <summary>Release a previously granted v2 lease owned by <paramref name="principal"/>.</summary>
    /// <param name="principal">Authenticated caller principal.</param>
    /// <param name="leaseId">Lease identifier to release.</param>
    /// <returns>Railroad result true when released.</returns>
    public BenchResult<bool> Release(LabPrincipal principal, Guid leaseId)
    {
        if (!_v2Leases.TryGetValue(leaseId, out var grant))
        {
            return BenchResult<bool>.Fail(new BenchError(
                "BF-LEASE-UNKNOWN",
                BenchErrorKind.ResourceNotFound,
                $"Lease '{leaseId}' is not active.",
                Remedy: "Check active reservations before releasing."));
        }

        if (!string.Equals(grant.PrincipalId, principal.PrincipalId, StringComparison.Ordinal) &&
            principal.Role < LabRole.RecoveryAdmin)
        {
            _auditLog.Append(principal.PrincipalId, "Release", grant.ResourceId, "Denied:LeaseOwnerMismatch");
            return BenchResult<bool>.Fail(new BenchError(
                "BF-SEC-LEASE-OWNER-MISMATCH",
                BenchErrorKind.LeaseFenced,
                $"Lease belongs to '{grant.PrincipalId}', not '{principal.PrincipalId}'.",
                grant.ResourceId,
                Remedy: "Only the owning principal or a RecoveryAdmin may release this lease."));
        }

        try
        {
            bool released = _registry.Release(leaseId);
            if (released)
            {
                _v2Leases.TryRemove(leaseId, out _);
                _activeLeaseByResource.TryRemove(grant.ResourceId, out _);
            }
            _auditLog.Append(principal.PrincipalId, "Release", grant.ResourceId, released ? "Released" : "NotFound");
            return BenchResult<bool>.Ok(released);
        }
        catch (InvalidOperationException ex)
        {
            _auditLog.Append(principal.PrincipalId, "Release", grant.ResourceId, "RecoveryRequired");
            return BenchResult<bool>.Fail(new BenchError(
                "BF-LEASE-RECOVERY-REQUIRED",
                BenchErrorKind.RecoveryRequired,
                ex.Message,
                grant.ResourceId,
                Confidence: PhysicalStateConfidence.UnknownRequiresRecovery,
                RequiresRecovery: true,
                Remedy: "Verify physical safe state and reconcile before releasing ownership."));
        }
    }

    /// <summary>Submit a canonical fenced command through RBAC, lease-owner binding, raw-SCPI policy, and <see cref="CanonicalCommandDispatcher"/>.</summary>
    /// <param name="principal">Authenticated caller principal.</param>
    /// <param name="request">Canonical remote command request.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>Railroad result containing the durable <see cref="CommandReceipt"/> or structured <see cref="BenchError"/>.</returns>
    public async Task<BenchResult<CommandReceipt>> SubmitCommandAsync(
        LabPrincipal principal,
        RemoteCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.TryParse<InstrumentOperationKind>(request.OperationKind, ignoreCase: true, out var opKind))
            return BenchResult<CommandReceipt>.Fail(BenchError.Validation("BF-CMD-OPKIND", $"Unknown OperationKind '{request.OperationKind}'.", request.ResourceId));
        if (!Enum.TryParse<CommandSafetyClass>(request.SafetyClass, ignoreCase: true, out var safetyClass))
            return BenchResult<CommandReceipt>.Fail(BenchError.Validation("BF-CMD-SAFETY", $"Unknown SafetyClass '{request.SafetyClass}'.", request.ResourceId));

        string? leaseOwner = _v2Leases.TryGetValue(request.LeaseId, out var grant) ? grant.PrincipalId : null;
        var action = request.IsRawScpi ? LabHostAction.RawScpi : LabHostAction.Actuate;
        var authz = _security.Authorize(principal, action, request.ResourceId, leaseOwner);
        if (authz.IsFailure)
        {
            _auditLog.Append(principal.PrincipalId, action.ToString(), request.ResourceId, $"Denied:{authz.Error.Code}");
            return BenchResult<CommandReceipt>.Fail(authz.Error);
        }

        // Enforce registered fail-safe hard maximums BEFORE any bytes reach the instrument transport.
        var limitDecision = await _hardLimitGuard.EvaluateAsync(
            new InstrumentOperationContext(request.ResourceId, request.CommandText, opKind, 1, null, IsPhysicalHardware: true),
            cancellationToken).ConfigureAwait(false);
        if (!limitDecision.Allowed)
        {
            _auditLog.Append(principal.PrincipalId, $"HardLimit:{opKind}", request.ResourceId, $"Denied:{limitDecision.RuleId}");
            return BenchResult<CommandReceipt>.Fail(BenchError.EnvelopeViolation(
                limitDecision.RuleId,
                limitDecision.Reason ?? $"Command on '{request.ResourceId}' violated registered fail-safe hard limits.",
                request.ResourceId,
                $"Inspect GET /api/v2/resources/{request.ResourceId}/limits and stay within registered maximums."));
        }

        var leaseFence = new LeaseFence(request.LeaseId, request.HostEpoch, request.Generation);
        var envelope = new CommandEnvelope(
            CommandEnvelope.CurrentSchemaVersion,
            request.CommandId,
            request.CorrelationId,
            request.ResourceId,
            leaseFence,
            opKind,
            safetyClass,
            request.CommandText,
            request.ExpectsResponse,
            request.MaxSafeRetries);

        var dispatcher = _dispatchers.GetOrAdd(request.ResourceId, id => new CanonicalCommandDispatcher(
            id,
            new RegistryBackedAsyncSession(_registry, id, () => _activeLeaseByResource.GetValueOrDefault(id)),
            _commandLedger,
            ValidateFence));

        var receipt = await dispatcher.SubmitAsync(envelope, cancellationToken).ConfigureAwait(false);
        _auditLog.Append(
            principal.PrincipalId,
            $"Command:{opKind}",
            request.ResourceId,
            $"State={receipt.State},Fault={receipt.FaultKind},Attempts={receipt.AttemptCount}");

        return receipt.IsTransportSuccess
            ? BenchResult<CommandReceipt>.Ok(receipt)
            : BenchResult<CommandReceipt>.Fail(BenchError.FromReceipt(receipt));
    }

    /// <summary>
    /// Register or monotonically tighten fail-safe hard maximums for <paramref name="resourceId"/>.
    /// Attempting to raise an already-registered maximum or remove a limit is denied and audited.
    /// </summary>
    /// <param name="principal">Authenticated caller principal (must hold <see cref="LabHostAction.Actuate"/> permission).</param>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <param name="dto">Hard limit bounds to register or tighten.</param>
    /// <returns>Railroad result containing the active effective <see cref="RemoteHardLimitsDto"/>.</returns>
    public BenchResult<RemoteHardLimitsDto> RegisterOrTightenLimits(
        LabPrincipal principal,
        string resourceId,
        RemoteHardLimitsDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var authz = _security.Authorize(principal, LabHostAction.Actuate, resourceId);
        if (authz.IsFailure)
        {
            _auditLog.Append(principal.PrincipalId, "RegisterHardLimits", resourceId, $"Denied:{authz.Error.Code}");
            return BenchResult<RemoteHardLimitsDto>.Fail(authz.Error);
        }

        var limits = FromDto(dto);
        var reg = _hardLimits.TryRegister(resourceId, limits);
        if (reg.IsFailure)
        {
            _auditLog.Append(principal.PrincipalId, "RegisterHardLimits", resourceId, $"Denied:{reg.Error.Code}");
            return BenchResult<RemoteHardLimitsDto>.Fail(reg.Error);
        }

        _auditLog.Append(principal.PrincipalId, "RegisterHardLimits", resourceId, $"Active:MaxV={reg.Value.MaxVoltage?.Volts},MaxI={reg.Value.MaxCurrent?.Amperes},MaxP={reg.Value.MaxPower?.Watts}");
        return BenchResult<RemoteHardLimitsDto>.Ok(ToDto(reg.Value));
    }

    /// <summary>Retrieve the currently registered fail-safe hard limits for <paramref name="resourceId"/>.</summary>
    /// <param name="principal">Authenticated caller principal.</param>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <returns>Railroad result containing active <see cref="RemoteHardLimitsDto"/> or <see cref="BenchErrorKind.ResourceNotFound"/>.</returns>
    public BenchResult<RemoteHardLimitsDto> GetHardLimits(LabPrincipal principal, string resourceId)
    {
        var authz = _security.Authorize(principal, LabHostAction.Observe, resourceId);
        if (authz.IsFailure)
            return BenchResult<RemoteHardLimitsDto>.Fail(authz.Error);

        var limits = _hardLimits.TryGet(resourceId);
        if (limits is null)
        {
            return BenchResult<RemoteHardLimitsDto>.Fail(new BenchError(
                "BF-HARDLIMIT-NONE",
                BenchErrorKind.ResourceNotFound,
                $"No hard limits are currently registered on '{resourceId}'.",
                resourceId,
                Remedy: $"PUT /api/v2/resources/{resourceId}/limits to register final fail-safe maximums."));
        }
        return BenchResult<RemoteHardLimitsDto>.Ok(ToDto(limits));
    }

    /// <summary>Explicitly reconcile quarantined command ledger records for a resource (requires <see cref="LabRole.RecoveryAdmin"/>).</summary>
    /// <param name="principal">Authenticated caller principal.</param>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <param name="reconciliationNote">Audit explanation of physical safe-state verification.</param>
    /// <returns>Railroad result containing the number of reconciled records.</returns>
    public BenchResult<int> Reconcile(LabPrincipal principal, string resourceId, string reconciliationNote)
    {
        var authz = _security.Authorize(principal, LabHostAction.Reconcile, resourceId);
        if (authz.IsFailure)
        {
            _auditLog.Append(principal.PrincipalId, "Reconcile", resourceId, $"Denied:{authz.Error.Code}");
            return BenchResult<int>.Fail(authz.Error);
        }
        if (string.IsNullOrWhiteSpace(reconciliationNote))
            return BenchResult<int>.Fail(BenchError.Validation("BF-RECONCILE-NOTE", "ReconciliationNote is required.", resourceId));

        int count = _commandLedger.ReconcileResource(resourceId, reconciliationNote);
        _auditLog.Append(principal.PrincipalId, "Reconcile", resourceId, $"Reconciled:{count}");
        return BenchResult<int>.Ok(count);
    }

    /// <summary>Verify the entire SHA-256 hash chain of the host's append-only security audit log.</summary>
    /// <param name="principal">Authenticated caller principal.</param>
    /// <returns>Railroad result containing <see cref="AuditChainStatus"/> or a tamper-detection failure.</returns>
    public BenchResult<AuditChainStatus> VerifyAuditChain(LabPrincipal principal)
    {
        var authz = _security.Authorize(principal, LabHostAction.Observe);
        if (authz.IsFailure)
            return BenchResult<AuditChainStatus>.Fail(authz.Error);

        return HashChainedAuditLog.VerifyChain(_auditLogPath)
            .Map(v => new AuditChainStatus(v.EntryCount, v.HeadSha256, DateTimeOffset.UtcNow));
    }

    /// <summary>Dispose all per-resource dispatchers and durable ledger file locks.</summary>
    public void Dispose()
    {
        foreach (var d in _dispatchers.Values)
            d.Dispose();
        _commandLedger.Dispose();
        _fencingLedger.Dispose();
    }

    private static InstrumentHardLimits FromDto(RemoteHardLimitsDto dto) => new(
        MaxVoltage: dto.MaxVoltageV is { } maxV ? ElectricPotential.FromVolts(maxV) : null,
        MinVoltage: dto.MinVoltageV is { } minV ? ElectricPotential.FromVolts(minV) : null,
        MaxCurrent: dto.MaxCurrentA is { } maxI ? ElectricCurrent.FromAmperes(maxI) : null,
        MinCurrent: dto.MinCurrentA is { } minI ? ElectricCurrent.FromAmperes(minI) : null,
        MaxPower: dto.MaxPowerW is { } maxP ? Power.FromWatts(maxP) : null,
        MinFrequency: dto.MinFrequencyHz is { } minF ? Frequency.FromHertz(minF) : null,
        MaxFrequency: dto.MaxFrequencyHz is { } maxF ? Frequency.FromHertz(maxF) : null,
        MinTemperature: dto.MinTemperatureC is { } minT ? Temperature.FromDegreesCelsius(minT) : null,
        MaxTemperature: dto.MaxTemperatureC is { } maxT ? Temperature.FromDegreesCelsius(maxT) : null,
        MinResistance: dto.MinResistanceOhm is { } minR ? ElectricResistance.FromOhms(minR) : null,
        MaxResistance: dto.MaxResistanceOhm is { } maxR ? ElectricResistance.FromOhms(maxR) : null,
        PolicyNote: dto.PolicyNote);

    private static RemoteHardLimitsDto ToDto(InstrumentHardLimits limits) => new(
        MaxVoltageV: limits.MaxVoltage?.Volts,
        MinVoltageV: limits.MinVoltage?.Volts,
        MaxCurrentA: limits.MaxCurrent?.Amperes,
        MinCurrentA: limits.MinCurrent?.Amperes,
        MaxPowerW: limits.MaxPower?.Watts,
        MinFrequencyHz: limits.MinFrequency?.Hertz,
        MaxFrequencyHz: limits.MaxFrequency?.Hertz,
        MinTemperatureC: limits.MinTemperature?.DegreesCelsius,
        MaxTemperatureC: limits.MaxTemperature?.DegreesCelsius,
        MinResistanceOhm: limits.MinResistance?.Ohms,
        MaxResistanceOhm: limits.MaxResistance?.Ohms,
        PolicyNote: limits.PolicyNote);

    private bool ValidateFence(string resourceId, LeaseFence fence) =>
        _v2Leases.TryGetValue(fence.LeaseId, out var active) &&
        string.Equals(active.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) &&
        active.HostEpoch == fence.HostEpoch &&
        active.Generation == fence.Generation;

    private sealed class RegistryBackedAsyncSession(HostResourceRegistry registry, string resourceId, Func<Guid> leaseAccessor) : IAsyncScpiSession
    {
        public ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = registry.Query(resourceId, leaseAccessor(), command);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(registry.Query(resourceId, leaseAccessor(), command));
        }
    }
}

/// <summary>ASP.NET Core endpoint registration for the railroad-oriented <c>/api/v2/*</c> host API.</summary>
public static class RailroadV2Endpoints
{
    /// <summary>Map all <c>/api/v2/*</c> railroad endpoints enforcing <see cref="PrivateLabSecurityPolicy"/> and <see cref="HashChainedAuditLog"/>.</summary>
    /// <param name="app">Target web application.</param>
    /// <returns>The same <see cref="WebApplication"/> for chaining.</returns>
    public static WebApplication MapRailroadV2Api(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v2/bench/describe", (HttpContext http, RailroadHostService host) =>
            WithAuth(http, host, p => host.Describe(p)));

        app.MapPost("/api/v2/reservations", (HttpContext http, ReservationRequest request, RailroadHostService host) =>
            WithAuth(http, host, p => host.Reserve(p, request.ResourceId)));

        app.MapDelete("/api/v2/reservations/{leaseId:guid}", (HttpContext http, Guid leaseId, RailroadHostService host) =>
            WithAuth(http, host, p => host.Release(p, leaseId)));

        app.MapPost("/api/v2/commands", async (HttpContext http, RemoteCommandRequest request, RailroadHostService host, CancellationToken ct) =>
        {
            var principal = AuthenticateHttp(http, host);
            if (principal.IsFailure)
                return ToHttpResult(BenchResult<CommandReceipt>.Fail(principal.Error));
            var res = await host.SubmitCommandAsync(principal.Value, request, ct).ConfigureAwait(false);
            return ToHttpResult(res);
        });

        app.MapPut("/api/v2/resources/{resourceId}/limits", (HttpContext http, string resourceId, RemoteHardLimitsDto request, RailroadHostService host) =>
            WithAuth(http, host, p => host.RegisterOrTightenLimits(p, resourceId, request)));

        app.MapGet("/api/v2/resources/{resourceId}/limits", (HttpContext http, string resourceId, RailroadHostService host) =>
            WithAuth(http, host, p => host.GetHardLimits(p, resourceId)));

        app.MapPost("/api/v2/resources/{resourceId}/reconcile", (HttpContext http, string resourceId, ReconcileRequest request, RailroadHostService host) =>
            WithAuth(http, host, p => host.Reconcile(p, resourceId, request.ReconciliationNote)));

        app.MapGet("/api/v2/audit/verify", (HttpContext http, RailroadHostService host) =>
            WithAuth(http, host, p => host.VerifyAuditChain(p)));

        return app;
    }

    private static IResult WithAuth<T>(HttpContext http, RailroadHostService host, Func<LabPrincipal, BenchResult<T>> action)
    {
        var principal = AuthenticateHttp(http, host);
        if (principal.IsFailure)
            return ToHttpResult(BenchResult<T>.Fail(principal.Error));
        return ToHttpResult(action(principal.Value));
    }

    private static BenchResult<LabPrincipal> AuthenticateHttp(HttpContext http, RailroadHostService host)
    {
        string? token = http.Request.Headers["X-BenchForge-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            var authHeader = http.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = authHeader["Bearer ".Length..].Trim();
        }
        bool isLoopback = http.Connection.RemoteIpAddress is null || System.Net.IPAddress.IsLoopback(http.Connection.RemoteIpAddress);
        return host.Authenticate(token, isLoopback);
    }

    private static IResult ToHttpResult<T>(BenchResult<T> result)
    {
        if (result.IsSuccess)
            return Microsoft.AspNetCore.Http.Results.Ok(result.ToWire());

        int statusCode = result.Error.Kind switch
        {
            BenchErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            BenchErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            BenchErrorKind.ResourceNotFound => StatusCodes.Status404NotFound,
            BenchErrorKind.LeaseFenced or BenchErrorKind.RecoveryRequired or BenchErrorKind.QueueSaturated => StatusCodes.Status409Conflict,
            BenchErrorKind.Timeout => StatusCodes.Status504GatewayTimeout,
            BenchErrorKind.Validation or BenchErrorKind.EnvelopeViolation or BenchErrorKind.GuardDenied => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        return Microsoft.AspNetCore.Http.Results.Json(result.ToWire(), statusCode: statusCode);
    }
}
