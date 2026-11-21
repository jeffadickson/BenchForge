using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchForge.Outcomes;

namespace BenchForge.Host.Server.Security;

/// <summary>Pragmatic least-privilege roles tailored for isolated private laboratory networks.</summary>
public enum LabRole
{
    /// <summary>Read-only monitoring, health, and cached telemetry; cannot reserve or actuate instruments.</summary>
    Observer = 1,

    /// <summary>Can reserve allowed instruments and execute typed/guarded commands and approved procedures.</summary>
    Operator = 2,

    /// <summary>Can perform explicit safe-state reconciliation to clear RecoveryRequired quarantine.</summary>
    RecoveryAdmin = 3,

    /// <summary>Full bench administration including setup changes and security policy management.</summary>
    BenchAdmin = 4
}

/// <summary>Authenticated principal identity and resource permissions for a private-lab host.</summary>
/// <param name="PrincipalId">Stable operator, workstation, or CI agent identifier.</param>
/// <param name="Role">Assigned least-privilege role.</param>
/// <param name="TokenSha256Hex">Uppercase SHA-256 hex digest of the bearer token; raw tokens are never stored.</param>
/// <param name="AllowedResources">Explicit resource allowlist, or <c>["*"]</c> for all bench resources.</param>
/// <param name="AllowRawScpi">Whether this principal may submit raw SCPI strings (false by default).</param>
public sealed record LabPrincipal(
    string PrincipalId,
    LabRole Role,
    string TokenSha256Hex,
    IReadOnlyList<string> AllowedResources,
    bool AllowRawScpi = false)
{
    /// <summary>Hash a raw secret token into its uppercase SHA-256 hex representation.</summary>
    /// <param name="rawToken">Secret bearer token string.</param>
    /// <returns>Uppercase SHA-256 hex digest.</returns>
    public static string HashToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    /// <summary>Check whether this principal is permitted to access <paramref name="resourceId"/>.</summary>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <returns>True when allowed explicitly or via wildcard.</returns>
    public bool CanAccessResource(string resourceId) =>
        AllowedResources.Any(r => r == "*" || string.Equals(r, resourceId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Host operation category evaluated against <see cref="LabRole"/> and principal-bound lease ownership.</summary>
public enum LabHostAction
{
    /// <summary>Read-only status, discovery snapshot, or telemetry query.</summary>
    Observe = 1,

    /// <summary>Reserve or release an instrument lease.</summary>
    Reserve = 2,

    /// <summary>Dispatch a typed or guarded command under an owned lease.</summary>
    Actuate = 3,

    /// <summary>Dispatch an arbitrary raw SCPI string (locked down by default).</summary>
    RawScpi = 4,

    /// <summary>Reconcile a quarantined RecoveryRequired resource.</summary>
    Reconcile = 5,

    /// <summary>Modify bench setup or security configuration.</summary>
    ConfigureBench = 6
}

/// <summary>
/// Pragmatic security policy for private air-gapped or VLAN-isolated lab benches:
/// zero ceremony on loopback, fail-closed on non-loopback bindings, constant-time token verification,
/// principal-bound lease ownership, and raw-SCPI lockdown by default.
/// </summary>
public sealed class PrivateLabSecurityPolicy
{
    private readonly LabPrincipal[] _principals;

    /// <summary>Create a private-lab security policy.</summary>
    /// <param name="principals">Configured authenticated principals (hashed tokens only).</param>
    /// <param name="allowPrivateLanBinding">Must be true before binding any non-loopback address.</param>
    /// <param name="requireTlsOnLan">When true (default), non-loopback bindings require TLS unless <paramref name="allowPlaintextOnIsolatedVlan"/> is explicitly acknowledged.</param>
    /// <param name="allowPlaintextOnIsolatedVlan">Explicit opt-in for air-gapped private lab VLANs without TLS certificates (still requires token auth).</param>
    /// <param name="allowRawScpiGlobal">Global kill-switch for raw SCPI endpoints (false by default).</param>
    public PrivateLabSecurityPolicy(
        IEnumerable<LabPrincipal> principals,
        bool allowPrivateLanBinding = false,
        bool requireTlsOnLan = true,
        bool allowPlaintextOnIsolatedVlan = false,
        bool allowRawScpiGlobal = false)
    {
        ArgumentNullException.ThrowIfNull(principals);
        _principals = principals.ToArray();
        AllowPrivateLanBinding = allowPrivateLanBinding;
        RequireTlsOnLan = requireTlsOnLan;
        AllowPlaintextOnIsolatedVlan = allowPlaintextOnIsolatedVlan;
        AllowRawScpiGlobal = allowRawScpiGlobal;
    }

    /// <summary>True when non-loopback network binding is enabled.</summary>
    public bool AllowPrivateLanBinding { get; }

    /// <summary>True when TLS is required for non-loopback listeners.</summary>
    public bool RequireTlsOnLan { get; }

    /// <summary>True when an operator explicitly acknowledged an isolated air-gapped VLAN without TLS.</summary>
    public bool AllowPlaintextOnIsolatedVlan { get; }

    /// <summary>True when raw SCPI command submission is globally permitted for authorized principals.</summary>
    public bool AllowRawScpiGlobal { get; }

    /// <summary>
    /// Validate a candidate listener URI before Kestrel/gRPC starts, failing closed on unsafe non-loopback exposure.
    /// </summary>
    /// <param name="listenUri">Candidate server listen URI (for example, <c>http://127.0.0.1:5278</c> or <c>https://10.42.0.5:5279</c>).</param>
    /// <returns>Railroad result allowing startup or detailing the exact security violation.</returns>
    public BenchResult ValidateListenerUri(Uri listenUri)
    {
        ArgumentNullException.ThrowIfNull(listenUri);
        bool isLoopback = listenUri.IsLoopback ||
            (IPAddress.TryParse(listenUri.Host, out var ip) && IPAddress.IsLoopback(ip));

        if (isLoopback)
            return BenchResult.Ok();

        if (!AllowPrivateLanBinding)
        {
            return BenchResult.Fail(new BenchError(
                "BF-SEC-LOOPBACK-ONLY",
                BenchErrorKind.Forbidden,
                $"Refusing non-loopback listener '{listenUri}' while AllowPrivateLanBinding is false.",
                Remedy: "Bind to 127.0.0.1/::1 or enable AllowPrivateLanBinding with authenticated principals."));
        }

        if (_principals.Length == 0)
        {
            return BenchResult.Fail(new BenchError(
                "BF-SEC-NO-PRINCIPALS",
                BenchErrorKind.Unauthorized,
                $"Refusing non-loopback listener '{listenUri}' with zero configured LabPrincipals.",
                Remedy: "Configure at least one LabPrincipal token before listening on a private LAN."));
        }

        bool isHttps = string.Equals(listenUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && RequireTlsOnLan && !AllowPlaintextOnIsolatedVlan)
        {
            return BenchResult.Fail(new BenchError(
                "BF-SEC-TLS-REQUIRED",
                BenchErrorKind.Forbidden,
                $"Refusing plaintext non-loopback listener '{listenUri}'.",
                Remedy: "Use https:// with a lab TLS certificate, or explicitly opt into AllowPlaintextOnIsolatedVlan on an air-gapped VLAN."));
        }

        return BenchResult.Ok();
    }

    /// <summary>Authenticate a raw bearer token using constant-time SHA-256 digest comparison.</summary>
    /// <param name="rawBearerToken">Caller-supplied secret token (never logged).</param>
    /// <returns>Railroad result containing the matched <see cref="LabPrincipal"/> or <see cref="BenchErrorKind.Unauthorized"/>.</returns>
    public BenchResult<LabPrincipal> AuthenticateToken(string? rawBearerToken)
    {
        if (string.IsNullOrWhiteSpace(rawBearerToken))
        {
            return BenchResult<LabPrincipal>.Fail(new BenchError(
                "BF-SEC-MISSING-TOKEN",
                BenchErrorKind.Unauthorized,
                "Authentication token is required.",
                Remedy: "Supply a valid X-BenchForge-Token or Authorization Bearer token."));
        }

        byte[] candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawBearerToken));
        foreach (var principal in _principals)
        {
            byte[] storedHash;
            try { storedHash = Convert.FromHexString(principal.TokenSha256Hex); }
            catch { continue; }

            if (storedHash.Length == candidateHash.Length &&
                CryptographicOperations.FixedTimeEquals(candidateHash, storedHash))
            {
                return BenchResult<LabPrincipal>.Ok(principal);
            }
        }

        return BenchResult<LabPrincipal>.Fail(new BenchError(
            "BF-SEC-INVALID-TOKEN",
            BenchErrorKind.Unauthorized,
            "Authentication token was rejected.",
            Remedy: "Verify your scoped lab token matches a configured LabPrincipal."));
    }

    /// <summary>
    /// Authorize an action for <paramref name="principal"/> on <paramref name="resourceId"/>, enforcing RBAC,
    /// resource allowlists, raw-SCPI lockdown, and principal-bound lease ownership.
    /// </summary>
    /// <param name="principal">Authenticated caller principal.</param>
    /// <param name="action">Requested host action.</param>
    /// <param name="resourceId">Optional target resource identifier.</param>
    /// <param name="leaseOwnerPrincipalId">When acting on an existing lease, the principal that owns the lease.</param>
    /// <returns>Railroad result permitting or denying the operation.</returns>
    public BenchResult Authorize(
        LabPrincipal principal,
        LabHostAction action,
        string? resourceId = null,
        string? leaseOwnerPrincipalId = null)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (!string.IsNullOrWhiteSpace(resourceId) && !principal.CanAccessResource(resourceId))
        {
            return BenchResult.Fail(new BenchError(
                "BF-SEC-RESOURCE-DENIED",
                BenchErrorKind.Forbidden,
                $"Principal '{principal.PrincipalId}' is not authorized to access resource '{resourceId}'.",
                resourceId,
                Remedy: "Use a principal scoped to this resource or contact a BenchAdmin."));
        }

        var requiredRole = action switch
        {
            LabHostAction.Observe => LabRole.Observer,
            LabHostAction.Reserve or LabHostAction.Actuate or LabHostAction.RawScpi => LabRole.Operator,
            LabHostAction.Reconcile => LabRole.RecoveryAdmin,
            LabHostAction.ConfigureBench => LabRole.BenchAdmin,
            _ => LabRole.BenchAdmin
        };

        if (principal.Role < requiredRole)
        {
            return BenchResult.Fail(new BenchError(
                "BF-SEC-ROLE-DENIED",
                BenchErrorKind.Forbidden,
                $"Action '{action}' requires role {requiredRole}, but '{principal.PrincipalId}' has role {principal.Role}.",
                resourceId,
                Remedy: $"Authenticate with a {requiredRole} credential."));
        }

        if (action == LabHostAction.RawScpi && (!AllowRawScpiGlobal || !principal.AllowRawScpi))
        {
            return BenchResult.Fail(new BenchError(
                "BF-SEC-RAW-SCPI-LOCKED",
                BenchErrorKind.Forbidden,
                $"Raw SCPI commands are locked down for '{principal.PrincipalId}'.",
                resourceId,
                Remedy: "Use typed/guarded instrument commands instead of raw SCPI."));
        }

        if (!string.IsNullOrWhiteSpace(leaseOwnerPrincipalId) &&
            action is LabHostAction.Actuate or LabHostAction.RawScpi &&
            !string.Equals(leaseOwnerPrincipalId, principal.PrincipalId, StringComparison.Ordinal))
        {
            return BenchResult.Fail(new BenchError(
                "BF-SEC-LEASE-OWNER-MISMATCH",
                BenchErrorKind.LeaseFenced,
                $"Lease belongs to principal '{leaseOwnerPrincipalId}', not '{principal.PrincipalId}'.",
                resourceId,
                Remedy: "Operate only under leases granted to your own authenticated principal."));
        }

        return BenchResult.Ok();
    }
}

/// <summary>One tamper-evident entry in an append-only <see cref="HashChainedAuditLog"/>.</summary>
/// <param name="Sequence">Monotonic one-based sequence number.</param>
/// <param name="TimestampUtc">UTC event timestamp.</param>
/// <param name="PrincipalId">Authenticated operator or automation principal.</param>
/// <param name="Action">Logical security or safety action.</param>
/// <param name="ResourceId">Optional target resource identifier.</param>
/// <param name="Outcome">Allowed, Denied, Reconciled, or Faulted summary (never raw secrets).</param>
/// <param name="PrevSha256">SHA-256 hex digest of the preceding entry (64 zeroes for entry 1).</param>
/// <param name="EntrySha256">SHA-256 hex digest chaining <paramref name="PrevSha256"/> and this entry's fields.</param>
public sealed record AuditLogEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string PrincipalId,
    string Action,
    string? ResourceId,
    string Outcome,
    string PrevSha256,
    string EntrySha256);

/// <summary>
/// Lightweight append-only SHA-256 hash-chained security and safety audit log for flight-hardware test benches.
/// Detects post-hoc deletion, reordering, or tampering via <see cref="VerifyChain"/>.
/// </summary>
public sealed class HashChainedAuditLog
{
    /// <summary>Genesis previous-hash value used for sequence 1.</summary>
    public static readonly string GenesisHash = new('0', 64);

    private readonly string _path;
    private readonly Lock _gate = new();
    private long _sequence;
    private string _headHash;

    /// <summary>Open or create a hash-chained JSONL audit log at <paramref name="path"/>.</summary>
    /// <param name="path">Local filesystem path for the JSONL audit log.</param>
    public HashChainedAuditLog(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Audit log path is required.", nameof(path));
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var verification = VerifyChain(_path);
        if (verification.IsFailure)
            throw new InvalidDataException($"Existing audit log failed integrity verification: {verification.Error.Message}");

        _sequence = verification.Value.EntryCount;
        _headHash = verification.Value.HeadSha256;
    }

    /// <summary>Latest SHA-256 hash in the audit chain, suitable for embedding in <see cref="Flight.FlightEvidencePackage"/>.</summary>
    public string HeadSha256 { get { using (_gate.EnterScope()) return _headHash; } }

    /// <summary>Append a new hash-chained audit entry and flush it to disk.</summary>
    /// <param name="principalId">Authenticated caller identity.</param>
    /// <param name="action">Security or safety action performed.</param>
    /// <param name="resourceId">Optional target resource identifier.</param>
    /// <param name="outcome">Safe non-secret outcome summary.</param>
    /// <returns>The persisted <see cref="AuditLogEntry"/>.</returns>
    public AuditLogEntry Append(string principalId, string action, string? resourceId, string outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

        using (_gate.EnterScope())
        {
            long nextSeq = _sequence + 1;
            var now = DateTimeOffset.UtcNow;
            var hash = ComputeEntryHash(nextSeq, now, principalId, action, resourceId, outcome, _headHash);
            var entry = new AuditLogEntry(nextSeq, now, principalId, action, resourceId, outcome, _headHash, hash);

            using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                writer.WriteLine(JsonSerializer.Serialize(entry));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            _sequence = nextSeq;
            _headHash = hash;
            return entry;
        }
    }

    /// <summary>Verify the full SHA-256 hash chain of a JSONL audit log file from genesis to head.</summary>
    /// <param name="path">Audit log file path.</param>
    /// <returns>Railroad result containing entry count and head SHA-256, or a structured integrity failure.</returns>
    public static BenchResult<(long EntryCount, string HeadSha256)> VerifyChain(string path)
    {
        if (!File.Exists(path))
            return BenchResult<(long, string)>.Ok((0, GenesisHash));

        long expectedSeq = 0;
        string expectedPrev = GenesisHash;

        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            AuditLogEntry? entry;
            try { entry = JsonSerializer.Deserialize<AuditLogEntry>(line); }
            catch (Exception ex)
            {
                return BenchResult<(long, string)>.Fail(new BenchError(
                    "BF-AUDIT-CORRUPT",
                    BenchErrorKind.PersistenceFault,
                    $"Audit log line {expectedSeq + 1} is not valid JSON: {ex.Message}",
                    Remedy: "Quarantine the tampered audit log and investigate before trusting test records."));
            }

            expectedSeq++;
            if (entry is null || entry.Sequence != expectedSeq || !string.Equals(entry.PrevSha256, expectedPrev, StringComparison.Ordinal))
            {
                return BenchResult<(long, string)>.Fail(new BenchError(
                    "BF-AUDIT-CHAIN-BROKEN",
                    BenchErrorKind.PersistenceFault,
                    $"Audit chain discontinuity detected at sequence {expectedSeq}.",
                    Remedy: "Audit trail was truncated, reordered, or modified; preserve file for QA investigation."));
            }

            string recomputed = ComputeEntryHash(
                entry.Sequence,
                entry.TimestampUtc,
                entry.PrincipalId,
                entry.Action,
                entry.ResourceId,
                entry.Outcome,
                entry.PrevSha256);

            if (!string.Equals(recomputed, entry.EntrySha256, StringComparison.Ordinal))
            {
                return BenchResult<(long, string)>.Fail(new BenchError(
                    "BF-AUDIT-TAMPERED",
                    BenchErrorKind.PersistenceFault,
                    $"Audit entry {expectedSeq} hash mismatch; entry contents were altered.",
                    Remedy: "Do not accept altered flight-test audit records."));
            }

            expectedPrev = entry.EntrySha256;
        }

        return BenchResult<(long, string)>.Ok((expectedSeq, expectedPrev));
    }

    private static string ComputeEntryHash(
        long sequence,
        DateTimeOffset timestampUtc,
        string principalId,
        string action,
        string? resourceId,
        string outcome,
        string prevSha256)
    {
        var payload = string.Join(
            "\u001f",
            sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            timestampUtc.ToUniversalTime().ToString("O"),
            principalId,
            action,
            resourceId ?? string.Empty,
            outcome,
            prevSha256);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
