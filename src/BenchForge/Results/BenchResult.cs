using BenchForge.Contracts;

namespace BenchForge.Outcomes;

/// <summary>Structured error category returned to clients and agents using railroad-oriented error handling.</summary>
public enum BenchErrorKind
{
    /// <summary>Caller supplied an invalid argument, range, unit, or malformed command.</summary>
    Validation = 1,

    /// <summary>Caller identity or authentication token was missing or invalid.</summary>
    Unauthorized = 2,

    /// <summary>Authenticated principal lacks the required role or resource permission.</summary>
    Forbidden = 3,

    /// <summary>Instrument lease is missing, expired, or fenced by a newer owner generation or host epoch.</summary>
    LeaseFenced = 4,

    /// <summary>A pre-dispatch safety interlock or contract guard denied the operation.</summary>
    GuardDenied = 5,

    /// <summary>Requested setpoint or rate violates the flight-hardware safe operating envelope.</summary>
    EnvelopeViolation = 6,

    /// <summary>Instrument calibration is expired or missing when flight calibration enforcement is active.</summary>
    CalibrationExpired = 7,

    /// <summary>Requested instrument role or capability does not exist on this bench.</summary>
    ResourceNotFound = 8,

    /// <summary>Instrument does not support the requested measurement or actuation capability.</summary>
    UnsupportedCapability = 9,

    /// <summary>Bounded command queue reached capacity before dispatch.</summary>
    QueueSaturated = 10,

    /// <summary>Command or measurement exceeded its configured timeout budget.</summary>
    Timeout = 11,

    /// <summary>Transport link failed or closed during I/O.</summary>
    TransportFault = 12,

    /// <summary>Instrument returned a malformed or out-of-protocol response.</summary>
    ProtocolFault = 13,

    /// <summary>Durable journal or evidence store persistence failed.</summary>
    PersistenceFault = 14,

    /// <summary>Operation was cancelled before any bytes reached the instrument.</summary>
    CancelledBeforeDispatch = 15,

    /// <summary>Transport dropped, timed out, or was cancelled after dispatch; physical hardware state is unknown.</summary>
    UncertainPhysicalState = 16,

    /// <summary>Resource is quarantined and requires explicit safe-state reconciliation before actuation.</summary>
    RecoveryRequired = 17,

    /// <summary>Measured quantity fell outside the declared flight-test pass/fail limits.</summary>
    LimitExceeded = 18,

    /// <summary>Compliance trip or hardware protection engaged during actuation.</summary>
    ComplianceTripped = 19,

    /// <summary>Unexpected internal fault.</summary>
    InternalFault = 20
}

/// <summary>Explicit assessment of physical hardware state confidence after an error or command outcome.</summary>
public enum PhysicalStateConfidence
{
    /// <summary>Operation was rejected or cancelled prior to wire dispatch; physical state was not modified by this call.</summary>
    Unchanged = 0,

    /// <summary>Transport I/O succeeded, but independent physical readback has not yet confirmed settling.</summary>
    TransportOnly = 1,

    /// <summary>Independent physical readback verified the requested hardware state.</summary>
    Verified = 2,

    /// <summary>Dispatch may have reached hardware before a fault or cancellation; explicit recovery verification is required.</summary>
    UnknownRequiresRecovery = 3
}

/// <summary>
/// Immutable, JSON-serializable structured error passed along the failure track of <see cref="BenchResult{T}"/>.
/// Designed for both human flight-test engineers and automated AI agents.
/// </summary>
/// <param name="Code">Stable machine-readable error code (for example, <c>BF-ENVELOPE-VOLT</c>).</param>
/// <param name="Kind">Typed failure category.</param>
/// <param name="Message">Safe, non-secret human-readable error description.</param>
/// <param name="ResourceId">Optional canonical instrument role or resource identifier.</param>
/// <param name="CorrelationId">Optional correlation identifier linking client, host, and audit logs.</param>
/// <param name="Confidence">Explicit physical-state confidence assessment.</param>
/// <param name="IsRetryable">True only when retrying the exact operation is declared safe without physical side effects.</param>
/// <param name="RequiresRecovery">True when explicit reconciliation is required before further actuation.</param>
/// <param name="Remedy">Actionable next step for an operator or AI agent.</param>
public sealed record BenchError(
    string Code,
    BenchErrorKind Kind,
    string Message,
    string? ResourceId = null,
    string? CorrelationId = null,
    PhysicalStateConfidence Confidence = PhysicalStateConfidence.Unchanged,
    bool IsRetryable = false,
    bool RequiresRecovery = false,
    string? Remedy = null)
{
    /// <summary>Create a validation error guaranteed to leave physical state unchanged.</summary>
    /// <param name="code">Stable validation error code.</param>
    /// <param name="message">Validation failure explanation.</param>
    /// <param name="resourceId">Optional target resource identifier.</param>
    /// <param name="remedy">Optional corrective guidance.</param>
    /// <returns>Structured validation <see cref="BenchError"/>.</returns>
    public static BenchError Validation(string code, string message, string? resourceId = null, string? remedy = null) =>
        new(code, BenchErrorKind.Validation, message, resourceId, null, PhysicalStateConfidence.Unchanged, false, false, remedy);

    /// <summary>Create a flight-envelope violation error blocked before any wire dispatch.</summary>
    /// <param name="code">Stable envelope rule code.</param>
    /// <param name="message">Limit violation explanation including requested and allowed bounds.</param>
    /// <param name="resourceId">Target instrument resource identifier.</param>
    /// <param name="remedy">Actionable guidance for staying within flight limits.</param>
    /// <returns>Structured envelope violation <see cref="BenchError"/>.</returns>
    public static BenchError EnvelopeViolation(string code, string message, string resourceId, string remedy) =>
        new(code, BenchErrorKind.EnvelopeViolation, message, resourceId, null, PhysicalStateConfidence.Unchanged, false, false, remedy);

    /// <summary>Translate a failed or uncertain <see cref="CommandReceipt"/> into a typed <see cref="BenchError"/>.</summary>
    /// <param name="receipt">Non-successful command receipt returned by <see cref="CanonicalCommandDispatcher"/>.</param>
    /// <returns>Structured <see cref="BenchError"/> preserving physical-state uncertainty and correlation ID.</returns>
    public static BenchError FromReceipt(CommandReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var (kind, confidence, remedy) = receipt.State switch
        {
            CommandExecutionState.CancelledBeforeDispatch => (
                BenchErrorKind.CancelledBeforeDispatch,
                PhysicalStateConfidence.Unchanged,
                "Command was cancelled before dispatch; safe to re-issue when ready."),
            CommandExecutionState.RecoveryRequired => (
                BenchErrorKind.RecoveryRequired,
                PhysicalStateConfidence.UnknownRequiresRecovery,
                $"Inspect hardware state on '{receipt.ResourceId}' and call ReconcileResource before actuating."),
            CommandExecutionState.Uncertain => (
                receipt.FaultKind == CommandFaultKind.TimeoutFault ? BenchErrorKind.Timeout : BenchErrorKind.UncertainPhysicalState,
                PhysicalStateConfidence.UnknownRequiresRecovery,
                $"Do not blindly retry '{receipt.ResourceId}'; verify physical state and reconcile the ledger."),
            _ => receipt.FaultKind switch
            {
                CommandFaultKind.ValidationFault => (BenchErrorKind.Validation, PhysicalStateConfidence.Unchanged, "Fix the command envelope parameters and retry."),
                CommandFaultKind.LeaseFault => (BenchErrorKind.LeaseFenced, PhysicalStateConfidence.Unchanged, "Acquire a fresh exclusive lease before commanding the instrument."),
                CommandFaultKind.GuardDeniedFault => (BenchErrorKind.GuardDenied, PhysicalStateConfidence.Unchanged, "Satisfy the active safety interlock or contract guard before dispatch."),
                CommandFaultKind.QueueSaturatedFault => (BenchErrorKind.QueueSaturated, PhysicalStateConfidence.Unchanged, "Await in-flight operations before submitting additional commands."),
                CommandFaultKind.PersistenceFault => (BenchErrorKind.PersistenceFault, PhysicalStateConfidence.Unchanged, "Check durable journal disk space and permissions before retrying."),
                CommandFaultKind.ProtocolFault => (BenchErrorKind.ProtocolFault, PhysicalStateConfidence.UnknownRequiresRecovery, "Inspect instrument status/error queue and verify protocol framing."),
                CommandFaultKind.TransportFault => (BenchErrorKind.TransportFault, PhysicalStateConfidence.UnknownRequiresRecovery, "Verify instrument network/bus link and reconcile physical state before reconnecting."),
                _ => (BenchErrorKind.InternalFault, PhysicalStateConfidence.UnknownRequiresRecovery, "Inspect correlated host diagnostics.")
            }
        };

        return new BenchError(
            $"BF-CMD-{receipt.FaultKind.ToString().ToUpperInvariant()}",
            kind,
            receipt.DiagnosticMessage ?? $"Command ended in state {receipt.State}.",
            receipt.ResourceId,
            receipt.CorrelationId,
            confidence,
            IsRetryable: false,
            RequiresRecovery: receipt.RequiresRecovery || confidence == PhysicalStateConfidence.UnknownRequiresRecovery,
            Remedy: remedy);
    }
}

/// <summary>Exception thrown only when a caller explicitly unwraps a failed <see cref="BenchResult"/> via <c>GetValueOrThrow</c>.</summary>
public sealed class BenchOperationException : InvalidOperationException
{
    /// <summary>Create an exception carrying the underlying structured <see cref="BenchError"/>.</summary>
    /// <param name="error">Structured error that caused the operation to fail.</param>
    public BenchOperationException(BenchError error)
        : base($"[{error.Code}] ({error.Kind}) {error.Message}")
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    /// <summary>Underlying structured error.</summary>
    public BenchError Error { get; }
}

/// <summary>Railroad-oriented outcome for operations that produce no return payload.</summary>
public readonly record struct BenchResult
{
    private readonly BenchError? _error;

    private BenchResult(bool isSuccess, BenchError? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed and carries a structured <see cref="BenchError"/>.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Structured error when <see cref="IsFailure"/> is true; throws when accessed on a success result.</summary>
    public BenchError Error => _error ?? throw new InvalidOperationException("Cannot access Error on a successful BenchResult.");

    /// <summary>Create a successful void result.</summary>
    /// <returns>Successful <see cref="BenchResult"/>.</returns>
    public static BenchResult Ok() => new(true, null);

    /// <summary>Create a failed void result carrying <paramref name="error"/>.</summary>
    /// <param name="error">Structured error describing the failure.</param>
    /// <returns>Failed <see cref="BenchResult"/>.</returns>
    public static BenchResult Fail(BenchError error) =>
        new(false, error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>Continue on the success track into an async value-producing step, short-circuiting on failure.</summary>
    /// <typeparam name="TNext">Output value type of the next step.</typeparam>
    /// <param name="next">Async continuation invoked only when <see cref="IsSuccess"/> is true.</param>
    /// <returns>Result of <paramref name="next"/> or the propagated error.</returns>
    public Task<BenchResult<TNext>> BindAsync<TNext>(Func<Task<BenchResult<TNext>>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : Task.FromResult(BenchResult<TNext>.Fail(Error));
    }

    /// <summary>Continue on the success track into an async void step, short-circuiting on failure.</summary>
    /// <param name="next">Async continuation invoked only when <see cref="IsSuccess"/> is true.</param>
    /// <returns>Result of <paramref name="next"/> or the propagated error.</returns>
    public Task<BenchResult> BindAsync(Func<Task<BenchResult>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : Task.FromResult(this);
    }

    /// <summary>Exhaustively fold the success or failure track into a single value.</summary>
    /// <typeparam name="TOut">Return type produced by both branches.</typeparam>
    /// <param name="onSuccess">Function invoked on success.</param>
    /// <param name="onFailure">Function invoked on failure.</param>
    /// <returns>Branch result.</returns>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<BenchError, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    /// <summary>Throw <see cref="BenchOperationException"/> if this result is a failure.</summary>
    public void ThrowIfFailure()
    {
        if (IsFailure)
            throw new BenchOperationException(Error);
    }
}

/// <summary>Railroad-oriented outcome carrying either a strongly typed value <typeparamref name="T"/> or a structured <see cref="BenchError"/>.</summary>
/// <typeparam name="T">Success payload type.</typeparam>
public readonly record struct BenchResult<T>
{
    private readonly T? _value;
    private readonly BenchError? _error;

    private BenchResult(bool isSuccess, T? value, BenchError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>True when the operation succeeded and <see cref="Value"/> is populated.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed and <see cref="Error"/> is populated.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Success payload; throws <see cref="BenchOperationException"/> when accessed on failure.</summary>
    public T Value => IsSuccess ? _value! : throw new BenchOperationException(Error);

    /// <summary>Structured error; throws <see cref="InvalidOperationException"/> when accessed on success.</summary>
    public BenchError Error => _error ?? throw new InvalidOperationException("Cannot access Error on a successful BenchResult<T>.");

    /// <summary>Create a successful result containing <paramref name="value"/>.</summary>
    /// <param name="value">Success payload.</param>
    /// <returns>Successful <see cref="BenchResult{T}"/>.</returns>
    public static BenchResult<T> Ok(T value) => new(true, value, null);

    /// <summary>Create a failed result carrying <paramref name="error"/>.</summary>
    /// <param name="error">Structured error describing the failure.</param>
    /// <returns>Failed <see cref="BenchResult{T}"/>.</returns>
    public static BenchResult<T> Fail(BenchError error) =>
        new(false, default, error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>Transform the success value without leaving the railroad.</summary>
    /// <typeparam name="TNext">Mapped value type.</typeparam>
    /// <param name="mapper">Pure mapping function invoked only on success.</param>
    /// <returns>Mapped success result or the original error.</returns>
    public BenchResult<TNext> Map<TNext>(Func<T, TNext> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return IsSuccess ? BenchResult<TNext>.Ok(mapper(_value!)) : BenchResult<TNext>.Fail(Error);
    }

    /// <summary>Chain a fallible synchronous operation on the success track.</summary>
    /// <typeparam name="TNext">Output value type.</typeparam>
    /// <param name="next">Continuation invoked only on success.</param>
    /// <returns>Chained result or propagated error.</returns>
    public BenchResult<TNext> Bind<TNext>(Func<T, BenchResult<TNext>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next(_value!) : BenchResult<TNext>.Fail(Error);
    }

    /// <summary>Chain a fallible asynchronous operation on the success track.</summary>
    /// <typeparam name="TNext">Output value type.</typeparam>
    /// <param name="next">Async continuation invoked only on success.</param>
    /// <returns>Chained result or propagated error.</returns>
    public Task<BenchResult<TNext>> BindAsync<TNext>(Func<T, Task<BenchResult<TNext>>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next(_value!) : Task.FromResult(BenchResult<TNext>.Fail(Error));
    }

    /// <summary>Assert a flight-test predicate on the success value, switching to the failure track if violated.</summary>
    /// <param name="predicate">Condition that must hold for the measurement or value.</param>
    /// <param name="errorFactory">Factory creating the structured <see cref="BenchError"/> when <paramref name="predicate"/> returns false.</param>
    /// <returns>Original success result or newly constructed failure result.</returns>
    public BenchResult<T> Ensure(Func<T, bool> predicate, Func<T, BenchError> errorFactory)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorFactory);
        if (IsFailure)
            return this;
        return predicate(_value!) ? this : Fail(errorFactory(_value!));
    }

    /// <summary>Invoke a side-effect observer on success without altering the result.</summary>
    /// <param name="observer">Observer receiving the success value.</param>
    /// <returns>The unchanged result.</returns>
    public BenchResult<T> Tap(Action<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        if (IsSuccess)
            observer(_value!);
        return this;
    }

    /// <summary>Exhaustively fold the success or failure track into a single value.</summary>
    /// <typeparam name="TOut">Return type produced by both branches.</typeparam>
    /// <param name="onSuccess">Function invoked on success.</param>
    /// <param name="onFailure">Function invoked on failure.</param>
    /// <returns>Branch result.</returns>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<BenchError, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

    /// <summary>Return the value on success or throw <see cref="BenchOperationException"/> on failure.</summary>
    /// <returns>The contained success value.</returns>
    public T GetValueOrThrow() => Value;

    /// <summary>Discard the payload and project into a void <see cref="BenchResult"/>.</summary>
    /// <returns>Success or propagated failure.</returns>
    public BenchResult ToUnit() => IsSuccess ? BenchResult.Ok() : BenchResult.Fail(Error);

    /// <summary>Convert to a JSON-serializable wire envelope suitable for HTTP/gRPC responses and agent tools.</summary>
    /// <returns>Immutable wire envelope carrying either <c>Value</c> or <c>Error</c>.</returns>
    public BenchWireResult<T> ToWire() =>
        IsSuccess ? new BenchWireResult<T>(true, _value, null) : new BenchWireResult<T>(false, default, _error);
}

/// <summary>JSON-serializable railroad DTO for sending typed outcomes over HTTP/gRPC boundaries to clients and agents.</summary>
/// <typeparam name="T">Success payload type.</typeparam>
/// <param name="Success">True when the remote operation succeeded.</param>
/// <param name="Value">Payload when <paramref name="Success"/> is true; otherwise default.</param>
/// <param name="Error">Structured <see cref="BenchError"/> when <paramref name="Success"/> is false; otherwise null.</param>
public sealed record BenchWireResult<T>(bool Success, T? Value, BenchError? Error)
{
    /// <summary>Rehydrate this wire DTO back into a <see cref="BenchResult{T}"/>.</summary>
    /// <returns>Typed railroad result.</returns>
    public BenchResult<T> ToResult() =>
        Success
            ? BenchResult<T>.Ok(Value!)
            : BenchResult<T>.Fail(Error ?? new BenchError("BF-WIRE-EMPTY", BenchErrorKind.ProtocolFault, "Remote response indicated failure without an error payload."));
}
