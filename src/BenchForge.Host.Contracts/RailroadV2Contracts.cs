namespace BenchForge.Host.Contracts;

/// <summary>Machine-readable remote bench inventory exposed by <c>GET /api/v2/bench/describe</c> for human and AI-agent discovery.</summary>
/// <param name="HostName">Logical bench host identifier.</param>
/// <param name="ProtocolVersion">API protocol version string (<c>2.0</c>).</param>
/// <param name="AllowRawScpi">True when authorized principals may issue raw SCPI strings.</param>
/// <param name="Instruments">Available bench instruments and classes.</param>
public sealed record RemoteBenchManifest(
    string HostName,
    string ProtocolVersion,
    bool AllowRawScpi,
    IReadOnlyList<InstrumentDescriptor> Instruments);

/// <summary>Principal-bound, fenced exclusive lease returned by <c>POST /api/v2/reservations</c>.</summary>
/// <param name="ResourceId">Canonical reserved resource identifier.</param>
/// <param name="LeaseId">Opaque unique lease identifier.</param>
/// <param name="HostEpoch">Durable host restart epoch.</param>
/// <param name="Generation">Monotonic per-resource lease generation.</param>
/// <param name="PrincipalId">Authenticated principal that owns this lease.</param>
public sealed record RemoteReservationGrant(
    string ResourceId,
    Guid LeaseId,
    long HostEpoch,
    long Generation,
    string PrincipalId);

/// <summary>Canonical fenced command request submitted to <c>POST /api/v2/commands</c>.</summary>
/// <param name="CommandId">Caller-assigned idempotency GUID.</param>
/// <param name="CorrelationId">Correlation ID propagated through ledger receipts and audit logs.</param>
/// <param name="ResourceId">Canonical target instrument resource identifier.</param>
/// <param name="LeaseId">Owned exclusive lease identifier.</param>
/// <param name="HostEpoch">Host epoch from the reservation grant.</param>
/// <param name="Generation">Resource generation from the reservation grant.</param>
/// <param name="OperationKind">Safety operation kind (<c>Query</c>, <c>Write</c>, <c>OutputControl</c>, <c>Recovery</c>).</param>
/// <param name="SafetyClass">Retry safety class (<c>NonIdempotent</c>, <c>ReadOnlyIdempotent</c>, <c>AbsoluteSetpoint</c>).</param>
/// <param name="CommandText">SCPI or canonical command text.</param>
/// <param name="ExpectsResponse">True for query commands returning a response string.</param>
/// <param name="MaxSafeRetries">Bounded retry budget (must be 0 for <c>NonIdempotent</c>).</param>
/// <param name="IsRawScpi">True when submitting an arbitrary raw SCPI string subject to raw-SCPI lockdown.</param>
public sealed record RemoteCommandRequest(
    Guid CommandId,
    string CorrelationId,
    string ResourceId,
    Guid LeaseId,
    long HostEpoch,
    long Generation,
    string OperationKind,
    string SafetyClass,
    string CommandText,
    bool ExpectsResponse = false,
    int MaxSafeRetries = 0,
    bool IsRawScpi = false);

/// <summary>Request body for explicit safe-state quarantine reconciliation on <c>POST /api/v2/resources/{resourceId}/reconcile</c>.</summary>
/// <param name="ReconciliationNote">Operator audit note explaining how physical safety was confirmed.</param>
public sealed record ReconcileRequest(string ReconciliationNote);

/// <summary>Live integrity verification summary returned by <c>GET /api/v2/audit/verify</c>.</summary>
/// <param name="EntryCount">Total verified hash-chained audit entries.</param>
/// <param name="HeadSha256">Latest SHA-256 chain head digest.</param>
/// <param name="VerifiedUtc">UTC timestamp when chain verification completed.</param>
public sealed record AuditChainStatus(long EntryCount, string HeadSha256, DateTimeOffset VerifiedUtc);
