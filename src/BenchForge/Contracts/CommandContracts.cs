using System.Security.Cryptography;
using System.Text;

namespace BenchForge.Contracts;

/// <summary>Retry safety classification declared on every command envelope; unsafe operations never auto-retry.</summary>
public enum CommandSafetyClass
{
    /// <summary>Side-effecting, pulsed, triggered, or destructive read command; must never be automatically retried.</summary>
    NonIdempotent = 0,

    /// <summary>Pure status/configuration query with no destructive buffer pop; eligible for bounded retry only before uncertainty.</summary>
    ReadOnlyIdempotent = 1,

    /// <summary>Absolute setpoint write explicitly declared safe to converge within a bounded retry budget.</summary>
    AbsoluteSetpoint = 2
}

/// <summary>Canonical durable lifecycle state of an instrument command; transport completion is distinct from physical verification.</summary>
public enum CommandExecutionState
{
    /// <summary>Command envelope validated, authorized, and durably accepted before dispatch.</summary>
    Accepted = 0,

    /// <summary>Command entered transport dispatch; a crash or drop in this state leaves physical state uncertain.</summary>
    Dispatched = 1,

    /// <summary>Transport I/O completed without error; does NOT by itself establish physical settling or device verification.</summary>
    TransportCompleted = 2,

    /// <summary>Device readback or explicit physical check confirmed the requested physical state.</summary>
    PhysicallyVerified = 3,

    /// <summary>Command was cancelled while still queued prior to any transport dispatch.</summary>
    CancelledBeforeDispatch = 4,

    /// <summary>Command failed validation, fencing, guard evaluation, or persistence before any transport dispatch.</summary>
    FailedBeforeDispatch = 5,

    /// <summary>Transport dropped, timed out, or was cancelled after dispatch; physical state is unknown and must not be silently retried.</summary>
    Uncertain = 6,

    /// <summary>Resource requires explicit operator or recovery reconciliation before accepting further actuation.</summary>
    RecoveryRequired = 7
}

/// <summary>Distinct typed failure category surfaced to callers and correlated diagnostics.</summary>
public enum CommandFaultKind
{
    /// <summary>No fault occurred.</summary>
    None = 0,

    /// <summary>Envelope field, schema version, payload, or command syntax validation failed before dispatch.</summary>
    ValidationFault = 1,

    /// <summary>Lease identity, fencing epoch, or generation was missing, stale, or mismatched.</summary>
    LeaseFault = 2,

    /// <summary>A safety interlock or contract guard denied dispatch.</summary>
    GuardDeniedFault = 3,

    /// <summary>Same command identifier was replayed with a different payload digest or target resource.</summary>
    IdempotencyConflictFault = 4,

    /// <summary>Durable command journal could not persist the state transition (for example, disk full or I/O error).</summary>
    PersistenceFault = 5,

    /// <summary>Bounded dispatch queue reached capacity and rejected the request before dispatch.</summary>
    QueueSaturatedFault = 6,

    /// <summary>Transport connection dropped or failed during I/O.</summary>
    TransportFault = 7,

    /// <summary>Operation exceeded its configured timeout budget.</summary>
    TimeoutFault = 8,

    /// <summary>Device returned a malformed or unexpected protocol response.</summary>
    ProtocolFault = 9,

    /// <summary>Operation was cancelled before or during execution.</summary>
    CancellationFault = 10,

    /// <summary>Post-command cleanup or safe-state recovery failed; ownership must remain fenced.</summary>
    CleanupFault = 11
}

/// <summary>Monotonic lease token identifying an exclusive owner's fencing epoch and resource generation.</summary>
/// <param name="LeaseId">Opaque unique lease identifier granted by the authoritative host.</param>
/// <param name="HostEpoch">Durable host restart epoch that invalidates pre-restart leases.</param>
/// <param name="Generation">Per-resource monotonic lease generation.</param>
public readonly record struct LeaseFence(Guid LeaseId, long HostEpoch, long Generation)
{
    /// <summary>True when the lease identifier is non-empty and both fencing counters are strictly positive.</summary>
    public bool IsValid => LeaseId != Guid.Empty && HostEpoch > 0 && Generation > 0;
}

/// <summary>
/// Versioned canonical command envelope validated and durably recorded before any instrument I/O.
/// </summary>
/// <param name="SchemaVersion">Canonical envelope schema version; must equal <see cref="CurrentSchemaVersion"/>.</param>
/// <param name="CommandId">Caller-assigned idempotency key unique within the target resource.</param>
/// <param name="CorrelationId">Correlation token propagated across logs, receipts, and diagnostics.</param>
/// <param name="ResourceId">Canonical target instrument resource identifier.</param>
/// <param name="Lease">Authoritative fencing lease held by the caller.</param>
/// <param name="OperationKind">Safety classification evaluated by pre-dispatch guards.</param>
/// <param name="SafetyClass">Explicit retry safety classification; defaults to <see cref="CommandSafetyClass.NonIdempotent"/>.</param>
/// <param name="CommandText">SCPI or canonical command payload to dispatch.</param>
/// <param name="ExpectsResponse">True for query commands returning a response string; false for write-only commands.</param>
/// <param name="MaxSafeRetries">Maximum retry attempts allowed only for safe classes; must be zero for <see cref="CommandSafetyClass.NonIdempotent"/>.</param>
/// <param name="Timeout">Optional bounded timeout for transport execution.</param>
public sealed record CommandEnvelope(
    int SchemaVersion,
    Guid CommandId,
    string CorrelationId,
    string ResourceId,
    LeaseFence Lease,
    InstrumentOperationKind OperationKind,
    CommandSafetyClass SafetyClass,
    string CommandText,
    bool ExpectsResponse = false,
    int MaxSafeRetries = 0,
    TimeSpan? Timeout = null)
{
    /// <summary>Current supported command envelope schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Maximum allowed character length for a single command text payload.</summary>
    public const int MaxCommandTextLength = 4096;

    /// <summary>Compute a deterministic SHA-256 hex digest of the canonical payload fields for idempotency checks.</summary>
    /// <returns>Uppercase hexadecimal SHA-256 digest.</returns>
    public string ComputePayloadDigest()
    {
        var canonical = string.Join(
            "\u001f",
            SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ResourceId ?? string.Empty,
            OperationKind.ToString(),
            SafetyClass.ToString(),
            ExpectsResponse ? "Q" : "W",
            CommandText ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>
/// Immutable durable receipt recording a command's exact lifecycle state, typed fault, and verification boundary.
/// </summary>
/// <param name="CommandId">Idempotency key of the command.</param>
/// <param name="CorrelationId">Correlation token for cross-layer diagnostics.</param>
/// <param name="ResourceId">Target instrument resource identifier.</param>
/// <param name="Lease">Fencing lease under which the command was accepted.</param>
/// <param name="PayloadDigest">SHA-256 digest of the accepted payload.</param>
/// <param name="State">Current durable execution state.</param>
/// <param name="FaultKind">Distinct typed fault category, or <see cref="CommandFaultKind.None"/>.</param>
/// <param name="AttemptCount">Number of actual transport dispatch attempts executed (0 when rejected/cancelled pre-dispatch).</param>
/// <param name="Response">Raw query response when <see cref="State"/> is <see cref="CommandExecutionState.TransportCompleted"/> or <see cref="CommandExecutionState.PhysicallyVerified"/>.</param>
/// <param name="DiagnosticMessage">Safe, non-secret diagnostic summary.</param>
/// <param name="UpdatedUtc">UTC timestamp of the latest durable state transition.</param>
public sealed record CommandReceipt(
    Guid CommandId,
    string CorrelationId,
    string ResourceId,
    LeaseFence Lease,
    string PayloadDigest,
    CommandExecutionState State,
    CommandFaultKind FaultKind,
    int AttemptCount,
    string? Response,
    string? DiagnosticMessage,
    DateTimeOffset UpdatedUtc)
{
    /// <summary>True only when transport I/O succeeded or physical verification was explicitly confirmed.</summary>
    public bool IsTransportSuccess =>
        State is CommandExecutionState.TransportCompleted or CommandExecutionState.PhysicallyVerified;

    /// <summary>True only when physical state verification was explicitly performed and confirmed.</summary>
    public bool IsPhysicallyVerified => State == CommandExecutionState.PhysicallyVerified;

    /// <summary>True when physical state is unknown or recovery reconciliation is required before further actuation.</summary>
    public bool RequiresRecovery =>
        State is CommandExecutionState.Uncertain or CommandExecutionState.RecoveryRequired;
}

/// <summary>Durable storage contract for command acceptance, dispatch transitions, idempotency lookup, and crash recovery.</summary>
public interface ICommandLedger
{
    /// <summary>Look up an existing durable receipt by resource and command identifier.</summary>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <param name="commandId">Command idempotency identifier.</param>
    /// <returns>The persisted receipt when present; otherwise null.</returns>
    CommandReceipt? TryGet(string resourceId, Guid commandId);

    /// <summary>List all receipts currently recorded for a resource.</summary>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <returns>Immutable snapshot of receipts ordered by update time.</returns>
    IReadOnlyList<CommandReceipt> ListForResource(string resourceId);

    /// <summary>Durably record or advance a command receipt before proceeding to the next execution stage.</summary>
    /// <param name="receipt">Receipt to persist atomically.</param>
    void Record(CommandReceipt receipt);

    /// <summary>
    /// Return true if the resource has any unresolved <see cref="CommandExecutionState.Dispatched"/>,
    /// <see cref="CommandExecutionState.Uncertain"/>, or <see cref="CommandExecutionState.RecoveryRequired"/> command.
    /// </summary>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <returns>True when the resource must remain quarantined pending explicit reconciliation.</returns>
    bool RequiresRecovery(string resourceId);

    /// <summary>
    /// Explicitly reconcile all quarantined receipts for a resource after operator or verified safe-state recovery.
    /// </summary>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <param name="reconciliationNote">Audit note explaining how physical safety was confirmed.</param>
    /// <returns>Number of quarantined records reconciled.</returns>
    int ReconcileResource(string resourceId, string reconciliationNote);
}

/// <summary>Thread-safe in-memory <see cref="ICommandLedger"/> suitable for simulation and deterministic tests.</summary>
public sealed class InMemoryCommandLedger : ICommandLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<(string ResourceId, Guid CommandId), CommandReceipt> _receipts = new();
    private readonly HashSet<string> _reconciledQuarantine = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional hook invoked on every <see cref="Record"/> call to simulate persistence faults (for example, disk full).</summary>
    public Action<CommandReceipt>? FaultInjector { get; set; }

    /// <inheritdoc/>
    public CommandReceipt? TryGet(string resourceId, Guid commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        lock (_gate)
            return _receipts.TryGetValue((resourceId, commandId), out var receipt) ? receipt : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CommandReceipt> ListForResource(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        lock (_gate)
        {
            return _receipts.Values
                .Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.UpdatedUtc)
                .ToArray();
        }
    }

    /// <inheritdoc/>
    public void Record(CommandReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        FaultInjector?.Invoke(receipt);
        lock (_gate)
        {
            _receipts[(receipt.ResourceId, receipt.CommandId)] = receipt;
            if (receipt.State is CommandExecutionState.Dispatched or CommandExecutionState.Uncertain or CommandExecutionState.RecoveryRequired)
                _reconciledQuarantine.Remove(receipt.ResourceId);
        }
    }

    /// <inheritdoc/>
    public bool RequiresRecovery(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        lock (_gate)
        {
            if (_reconciledQuarantine.Contains(resourceId))
                return false;
            return _receipts.Values.Any(r =>
                string.Equals(r.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) &&
                r.State is CommandExecutionState.Dispatched or CommandExecutionState.Uncertain or CommandExecutionState.RecoveryRequired);
        }
    }

    /// <inheritdoc/>
    public int ReconcileResource(string resourceId, string reconciliationNote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationNote);
        lock (_gate)
        {
            int count = _receipts.Values.Count(r =>
                string.Equals(r.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) &&
                r.State is CommandExecutionState.Dispatched or CommandExecutionState.Uncertain or CommandExecutionState.RecoveryRequired);
            _reconciledQuarantine.Add(resourceId);
            return count;
        }
    }
}

/// <summary>
/// Authoritative lease validator invoked before accepting or dispatching any command envelope.
/// </summary>
/// <param name="resourceId">Canonical target resource identifier.</param>
/// <param name="lease">Caller-supplied lease fence.</param>
/// <returns>True only when <paramref name="lease"/> is the current active exclusive fence for <paramref name="resourceId"/>.</returns>
public delegate bool LeaseFenceValidator(string resourceId, LeaseFence lease);

/// <summary>
/// Canonical command execution coordinator enforcing envelope validation, lease fencing, pre-dispatch safety guards,
/// durable acceptance, at-most-once idempotency, bounded queueing, explicit retry safety classes, and fail-closed uncertainty.
/// </summary>
public sealed class CanonicalCommandDispatcher : IDisposable
{
    private readonly string _resourceId;
    private readonly BenchForge.Async.IAsyncScpiSession _session;
    private readonly ICommandLedger _ledger;
    private readonly LeaseFenceValidator _validateLease;
    private readonly IInstrumentOperationGuard? _guard;
    private readonly bool _isPhysicalHardware;
    private readonly int _maxQueueDepth;
    private readonly TimeSpan _defaultTimeout;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly object _inflightGate = new();
    private readonly Dictionary<Guid, (string PayloadDigest, Task<CommandReceipt> Execution)> _inflight = new();
    private int _queuedOrRunning;
    private bool _disposed;

    /// <summary>Create a canonical command dispatcher bound to one instrument resource and its authoritative ledger.</summary>
    /// <param name="resourceId">Canonical target instrument resource identifier.</param>
    /// <param name="session">Underlying asynchronous SCPI transport session (caller-owned).</param>
    /// <param name="ledger">Durable command ledger recording acceptance and dispatch state transitions.</param>
    /// <param name="validateLease">Authoritative fencing validator checking lease ID, host epoch, and resource generation.</param>
    /// <param name="guard">Optional pre-dispatch safety or contract guard evaluated before transport I/O.</param>
    /// <param name="isPhysicalHardware">True when connected to physical lab equipment; false for simulation/mocks.</param>
    /// <param name="maxQueueDepth">Maximum number of concurrent queued + running commands allowed before rejecting with <see cref="CommandFaultKind.QueueSaturatedFault"/>.</param>
    /// <param name="defaultTimeout">Default per-attempt transport timeout when the envelope omits one.</param>
    public CanonicalCommandDispatcher(
        string resourceId,
        BenchForge.Async.IAsyncScpiSession session,
        ICommandLedger ledger,
        LeaseFenceValidator validateLease,
        IInstrumentOperationGuard? guard = null,
        bool isPhysicalHardware = false,
        int maxQueueDepth = 32,
        TimeSpan? defaultTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(validateLease);
        if (maxQueueDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(maxQueueDepth), "Queue depth must be at least 1.");
        if (defaultTimeout is { } dt && dt <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(defaultTimeout), "Default timeout must be positive.");

        _resourceId = resourceId;
        _session = session;
        _ledger = ledger;
        _validateLease = validateLease;
        _guard = guard;
        _isPhysicalHardware = isPhysicalHardware;
        _maxQueueDepth = maxQueueDepth;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Submit a versioned command envelope through validation, fencing, deduplication, durable acceptance,
    /// guard checks, and bounded transport execution.
    /// </summary>
    /// <param name="envelope">Versioned command envelope.</param>
    /// <param name="cancellationToken">Cancellation token; cancelling before dispatch returns <see cref="CommandExecutionState.CancelledBeforeDispatch"/>, while cancelling after dispatch records <see cref="CommandExecutionState.Uncertain"/>.</param>
    /// <returns>Durable receipt describing the exact command outcome and any typed fault.</returns>
    public Task<CommandReceipt> SubmitAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(envelope);

        var digest = envelope.ComputePayloadDigest();

        // 1. Validate envelope fields before any ledger mutation or instrument I/O (BD-CMD-001).
        if (ValidateEnvelope(envelope) is { } validationError)
            return Task.FromResult(Rejected(envelope, digest, CommandFaultKind.ValidationFault, validationError));

        // 2. Enforce authoritative lease fence before accepting or dispatching (BD-CMD-001 / BD-CMD-008).
        if (!_validateLease(envelope.ResourceId, envelope.Lease))
            return Task.FromResult(Rejected(envelope, digest, CommandFaultKind.LeaseFault, "Lease is missing, expired, or fenced."));

        // 3. Enforce single-flight & durable idempotency by CommandId (BD-CMD-003).
        lock (_inflightGate)
        {
            if (_inflight.TryGetValue(envelope.CommandId, out var running))
            {
                if (!string.Equals(running.PayloadDigest, digest, StringComparison.Ordinal))
                {
                    return Task.FromResult(Rejected(
                        envelope,
                        digest,
                        CommandFaultKind.IdempotencyConflictFault,
                        "CommandId is already in flight with a different payload."));
                }
                return running.Execution;
            }

            if (_ledger.TryGet(envelope.ResourceId, envelope.CommandId) is { } existing)
            {
                if (!string.Equals(existing.PayloadDigest, digest, StringComparison.Ordinal))
                {
                    return Task.FromResult(Rejected(
                        envelope,
                        digest,
                        CommandFaultKind.IdempotencyConflictFault,
                        "CommandId was already recorded with a different payload."));
                }
                return Task.FromResult(existing);
            }

            if (IsQuarantinedLocked(envelope.ResourceId) && envelope.OperationKind != InstrumentOperationKind.Recovery)
            {
                return Task.FromResult(new CommandReceipt(
                    envelope.CommandId,
                    envelope.CorrelationId,
                    envelope.ResourceId,
                    envelope.Lease,
                    digest,
                    CommandExecutionState.RecoveryRequired,
                    CommandFaultKind.LeaseFault,
                    0,
                    null,
                    "Resource is quarantined from an uncertain or unrecovered command; reconcile before actuating.",
                    DateTimeOffset.UtcNow));
            }

            // 4. Enforce bounded queue depth (BD-CMD-008).
            if (_queuedOrRunning >= _maxQueueDepth)
            {
                return Task.FromResult(Rejected(
                    envelope,
                    digest,
                    CommandFaultKind.QueueSaturatedFault,
                    $"Command queue saturated at {_maxQueueDepth} pending/running operations."));
            }

            _queuedOrRunning++;
            var executionTask = ExecuteWithCleanupAsync(envelope, digest, cancellationToken);
            if (!executionTask.IsCompleted)
                _inflight[envelope.CommandId] = (digest, executionTask);
            return executionTask;
        }
    }

    private async Task<CommandReceipt> ExecuteWithCleanupAsync(CommandEnvelope envelope, string digest, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteCoreAsync(envelope, digest, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_inflightGate)
            {
                _inflight.Remove(envelope.CommandId);
                _queuedOrRunning--;
            }
        }
    }

    private bool IsQuarantinedLocked(string resourceId)
    {
        if (!_ledger.RequiresRecovery(resourceId))
            return false;
        if (_inflight.Count == 0)
            return true;

        // An actively executing command tracked in _inflight is Dispatched in the ledger, not a crash orphan.
        return _ledger.ListForResource(resourceId).Any(r =>
            r.State is CommandExecutionState.Uncertain or CommandExecutionState.RecoveryRequired ||
            (r.State == CommandExecutionState.Dispatched && !_inflight.ContainsKey(r.CommandId)));
    }

    /// <summary>
    /// Confirm physical verification of a previously transport-completed command after explicit device readback (BD-CMD-005).
    /// </summary>
    /// <param name="commandId">Identifier of the command whose physical effect was independently verified.</param>
    /// <param name="verificationSummary">Audit description of the physical verification check (for example, readback value).</param>
    /// <returns>Updated receipt in <see cref="CommandExecutionState.PhysicallyVerified"/> state.</returns>
    /// <exception cref="InvalidOperationException">The command does not exist or is not in <see cref="CommandExecutionState.TransportCompleted"/> state.</exception>
    public CommandReceipt ConfirmPhysicalVerification(Guid commandId, string verificationSummary)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationSummary);

        var existing = _ledger.TryGet(_resourceId, commandId)
            ?? throw new InvalidOperationException($"Command '{commandId}' is not recorded for resource '{_resourceId}'.");
        if (existing.State != CommandExecutionState.TransportCompleted)
            throw new InvalidOperationException($"Cannot physically verify command in state {existing.State}.");

        var verified = existing with
        {
            State = CommandExecutionState.PhysicallyVerified,
            DiagnosticMessage = verificationSummary,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        _ledger.Record(verified);
        return verified;
    }

    /// <summary>Release local synchronization resources without disposing the caller-owned session or ledger.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _executionGate.Dispose();
    }

    private async Task<CommandReceipt> ExecuteCoreAsync(CommandEnvelope envelope, string digest, CancellationToken cancellationToken)
    {
        // Pre-dispatch guard evaluation (SharpContracts / safety interlock seam).
        if (_guard is not null)
        {
            var context = new InstrumentOperationContext(
                envelope.ResourceId,
                envelope.CommandText,
                envelope.OperationKind,
                1,
                null,
                _isPhysicalHardware);
            var decision = await _guard.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                return Rejected(
                    envelope,
                    digest,
                    CommandFaultKind.GuardDeniedFault,
                    $"Guard '{decision.RuleId}' denied command: {decision.Reason}");
            }
        }

        // Durable acceptance MUST succeed before entering the dispatch queue (BD-CMD-002).
        var accepted = new CommandReceipt(
            envelope.CommandId,
            envelope.CorrelationId,
            envelope.ResourceId,
            envelope.Lease,
            digest,
            CommandExecutionState.Accepted,
            CommandFaultKind.None,
            0,
            null,
            "Accepted into durable command ledger.",
            DateTimeOffset.UtcNow);
        try
        {
            _ledger.Record(accepted);
        }
        catch (Exception ex)
        {
            return Rejected(
                envelope,
                digest,
                CommandFaultKind.PersistenceFault,
                $"Durable command acceptance failed before dispatch: {ex.Message}");
        }

        // Wait for exclusive transport slot; cancellation while queued is cleanly CancelledBeforeDispatch (BD-CMD-008).
        try
        {
            await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var cancelled = accepted with
            {
                State = CommandExecutionState.CancelledBeforeDispatch,
                FaultKind = CommandFaultKind.CancellationFault,
                DiagnosticMessage = "Cancelled while queued before transport dispatch.",
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            TryRecord(cancelled);
            return cancelled;
        }

        try
        {
            // Re-check lease fence and recovery quarantine immediately prior to wire dispatch.
            if (!_validateLease(envelope.ResourceId, envelope.Lease))
            {
                var fenced = accepted with
                {
                    State = CommandExecutionState.FailedBeforeDispatch,
                    FaultKind = CommandFaultKind.LeaseFault,
                    DiagnosticMessage = "Lease expired or fenced while waiting in queue.",
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                TryRecord(fenced);
                return fenced;
            }

            int maxAttempts = envelope.SafetyClass == CommandSafetyClass.NonIdempotent
                ? 1
                : checked(1 + envelope.MaxSafeRetries);
            var timeout = envelope.Timeout ?? _defaultTimeout;
            int attempt = 0;

            while (attempt < maxAttempts)
            {
                attempt++;

                // Record durable Dispatched marker BEFORE touching wire I/O (BD-CMD-002 / BD-CMD-004).
                var dispatched = accepted with
                {
                    State = CommandExecutionState.Dispatched,
                    AttemptCount = attempt,
                    DiagnosticMessage = $"Dispatched attempt {attempt}/{maxAttempts}.",
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                try
                {
                    _ledger.Record(dispatched);
                }
                catch (Exception ex)
                {
                    var persistFail = accepted with
                    {
                        State = attempt == 1 ? CommandExecutionState.FailedBeforeDispatch : CommandExecutionState.Uncertain,
                        FaultKind = CommandFaultKind.PersistenceFault,
                        AttemptCount = attempt - 1,
                        DiagnosticMessage = $"Failed to persist dispatch transition: {ex.Message}",
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    return persistFail;
                }

                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(timeout);

                try
                {
                    string? response = null;
                    if (envelope.ExpectsResponse)
                        response = await _session.QueryAsync(envelope.CommandText, attemptCts.Token).ConfigureAwait(false);
                    else
                        await _session.WriteAsync(envelope.CommandText, attemptCts.Token).ConfigureAwait(false);

                    // Transport completion is NEVER labelled PhysicallyVerified (BD-CMD-005).
                    var completed = dispatched with
                    {
                        State = CommandExecutionState.TransportCompleted,
                        FaultKind = CommandFaultKind.None,
                        Response = response,
                        DiagnosticMessage = "Transport I/O completed; physical state not yet independently verified.",
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    try
                    {
                        _ledger.Record(completed);
                        return completed;
                    }
                    catch (Exception ex)
                    {
                        // Crash/failure between physical effect and ledger update MUST NOT claim success (BD-CMD-004).
                        return dispatched with
                        {
                            State = CommandExecutionState.Uncertain,
                            FaultKind = CommandFaultKind.PersistenceFault,
                            Response = null,
                            DiagnosticMessage = $"Transport finished but durable completion failed; state is uncertain: {ex.Message}",
                            UpdatedUtc = DateTimeOffset.UtcNow
                        };
                    }
                }
                catch (FormatException ex)
                {
                    var protoFault = dispatched with
                    {
                        State = CommandExecutionState.Uncertain,
                        FaultKind = CommandFaultKind.ProtocolFault,
                        DiagnosticMessage = $"Protocol error after dispatch: {ex.Message}",
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    TryRecord(protoFault);
                    return protoFault;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout after dispatch: retry ONLY if explicitly safe AND within bounded budget (BD-CMD-006).
                    if (attempt < maxAttempts && envelope.SafetyClass != CommandSafetyClass.NonIdempotent)
                        continue;

                    var timedOut = dispatched with
                    {
                        State = CommandExecutionState.Uncertain,
                        FaultKind = CommandFaultKind.TimeoutFault,
                        DiagnosticMessage = $"Timed out after {timeout.TotalMilliseconds:F0} ms following dispatch; physical state is uncertain.",
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    TryRecord(timedOut);
                    return timedOut;
                }
                catch (OperationCanceledException)
                {
                    // Caller cancelled after wire dispatch: NEVER report Completed or CancelledBeforeDispatch (BD-CMD-008).
                    var uncertainCancel = dispatched with
                    {
                        State = CommandExecutionState.Uncertain,
                        FaultKind = CommandFaultKind.CancellationFault,
                        DiagnosticMessage = "Cancelled while command was in flight; physical state is uncertain.",
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    TryRecord(uncertainCancel);
                    return uncertainCancel;
                }
                catch (Exception ex)
                {
                    if (attempt < maxAttempts && envelope.SafetyClass != CommandSafetyClass.NonIdempotent)
                        continue;

                    var transportFault = dispatched with
                    {
                        State = CommandExecutionState.Uncertain,
                        FaultKind = CommandFaultKind.TransportFault,
                        DiagnosticMessage = $"Transport fault after dispatch: {ex.Message}",
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    TryRecord(transportFault);
                    return transportFault;
                }
            }

            // Unreachable defensive fallback.
            var fallback = accepted with
            {
                State = CommandExecutionState.Uncertain,
                FaultKind = CommandFaultKind.TransportFault,
                AttemptCount = attempt,
                DiagnosticMessage = "Retry budget exhausted without confirmation.",
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            TryRecord(fallback);
            return fallback;
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private string? ValidateEnvelope(CommandEnvelope envelope)
    {
        if (envelope.SchemaVersion != CommandEnvelope.CurrentSchemaVersion)
            return $"Unsupported envelope schema version {envelope.SchemaVersion}.";
        if (envelope.CommandId == Guid.Empty)
            return "CommandId must be a non-empty GUID.";
        if (string.IsNullOrWhiteSpace(envelope.CorrelationId) || envelope.CorrelationId.Length > 128)
            return "CorrelationId must be 1..128 non-whitespace characters.";
        if (!string.Equals(envelope.ResourceId, _resourceId, StringComparison.OrdinalIgnoreCase))
            return $"Envelope resource '{envelope.ResourceId}' does not match dispatcher resource '{_resourceId}'.";
        if (!envelope.Lease.IsValid)
            return "Lease fence must include a non-empty LeaseId and positive HostEpoch/Generation.";
        if (string.IsNullOrWhiteSpace(envelope.CommandText) || envelope.CommandText.Length > CommandEnvelope.MaxCommandTextLength)
            return $"CommandText must be 1..{CommandEnvelope.MaxCommandTextLength} non-whitespace characters.";
        if (envelope.CommandText.Contains('\r') || envelope.CommandText.Contains('\n') || envelope.CommandText.Contains('\0'))
            return "CommandText must not contain embedded line terminators or NUL bytes.";
        if (envelope.MaxSafeRetries < 0 || envelope.MaxSafeRetries > 5)
            return "MaxSafeRetries must be between 0 and 5.";
        if (envelope.SafetyClass == CommandSafetyClass.NonIdempotent && envelope.MaxSafeRetries > 0)
            return "NonIdempotent commands must declare MaxSafeRetries == 0.";
        if (envelope.OperationKind == InstrumentOperationKind.OutputControl &&
            envelope.SafetyClass != CommandSafetyClass.NonIdempotent)
            return "OutputControl commands must use CommandSafetyClass.NonIdempotent.";
        if (envelope.Timeout is { } t && (t <= TimeSpan.Zero || t > TimeSpan.FromMinutes(10)))
            return "Timeout must be positive and at most 10 minutes.";
        return null;
    }

    private static CommandReceipt Rejected(CommandEnvelope envelope, string digest, CommandFaultKind fault, string message) =>
        new(
            envelope.CommandId,
            envelope.CorrelationId ?? string.Empty,
            envelope.ResourceId ?? string.Empty,
            envelope.Lease,
            digest,
            CommandExecutionState.FailedBeforeDispatch,
            fault,
            0,
            null,
            message,
            DateTimeOffset.UtcNow);

    private void TryRecord(CommandReceipt receipt)
    {
        try { _ledger.Record(receipt); }
        catch { /* Original fault receipt is returned to caller even if secondary persistence fails. */ }
    }
}
