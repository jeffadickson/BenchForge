using System.Net.Http.Json;
using BenchForge.Contracts;
using BenchForge.Host.Contracts;
using BenchForge.Outcomes;

namespace BenchForge.Host.Client;

/// <summary>
/// Railroad-oriented HTTP client for <c>/api/v2/*</c> BenchForge lab hosts.
/// Never throws on operational host rejections; returns structured <see cref="BenchResult{T}"/> with
/// <see cref="PhysicalStateConfidence"/> and actionable <see cref="BenchError.Remedy"/> hints.
/// </summary>
public sealed class RailroadHostClient
{
    private readonly HttpClient _http;
    private readonly Uri? _endpoint;
    private readonly string? _bearerToken;

    /// <summary>Create a railroad host client over a caller-owned <see cref="HttpClient"/>.</summary>
    /// <param name="http">Caller-owned HTTP client (never mutated or disposed by this instance).</param>
    /// <param name="endpoint">Optional base endpoint URI when <paramref name="http"/> has no <c>BaseAddress</c>.</param>
    /// <param name="bearerToken">Optional private-lab bearer token sent in <c>X-BenchForge-Token</c> (never logged).</param>
    public RailroadHostClient(HttpClient http, Uri? endpoint = null, string? bearerToken = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _endpoint = endpoint;
        _bearerToken = bearerToken;
    }

    /// <summary>Fetch the machine-readable bench inventory manifest from <c>GET /api/v2/bench/describe</c>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing <see cref="RemoteBenchManifest"/>.</returns>
    public Task<BenchResult<RemoteBenchManifest>> DescribeAsync(CancellationToken cancellationToken = default) =>
        SendAsync<RemoteBenchManifest>(HttpMethod.Get, "/api/v2/bench/describe", null, cancellationToken);

    /// <summary>Reserve an instrument resource exclusively with a principal-bound lease fence.</summary>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing <see cref="RemoteReservationGrant"/>.</returns>
    public Task<BenchResult<RemoteReservationGrant>> ReserveAsync(string resourceId, CancellationToken cancellationToken = default) =>
        SendAsync<RemoteReservationGrant>(HttpMethod.Post, "/api/v2/reservations", new ReservationRequest(resourceId), cancellationToken);

    /// <summary>Release an owned v2 lease.</summary>
    /// <param name="leaseId">Lease identifier to release.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result indicating whether the lease was released.</returns>
    public Task<BenchResult<bool>> ReleaseAsync(Guid leaseId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"/api/v2/reservations/{leaseId}", null, cancellationToken);

    /// <summary>Submit a canonical fenced command to <c>POST /api/v2/commands</c>.</summary>
    /// <param name="request">Versioned command request bound to an owned lease fence.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing the durable <see cref="CommandReceipt"/>.</returns>
    public Task<BenchResult<CommandReceipt>> SubmitCommandAsync(RemoteCommandRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CommandReceipt>(HttpMethod.Post, "/api/v2/commands", request, cancellationToken);

    /// <summary>Reconcile quarantined command records for a resource after physical safe-state verification.</summary>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <param name="reconciliationNote">Audit note confirming physical safety.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing the count of reconciled records.</returns>
    public Task<BenchResult<int>> ReconcileAsync(string resourceId, string reconciliationNote, CancellationToken cancellationToken = default) =>
        SendAsync<int>(HttpMethod.Post, $"/api/v2/resources/{Uri.EscapeDataString(resourceId)}/reconcile", new ReconcileRequest(reconciliationNote), cancellationToken);

    /// <summary>Verify the remote host's append-only SHA-256 audit log chain.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing <see cref="AuditChainStatus"/>.</returns>
    public Task<BenchResult<AuditChainStatus>> VerifyAuditAsync(CancellationToken cancellationToken = default) =>
        SendAsync<AuditChainStatus>(HttpMethod.Get, "/api/v2/audit/verify", null, cancellationToken);

    /// <summary>Register or monotonically tighten fail-safe hard maximums on a hosted instrument via <c>PUT /api/v2/resources/{resourceId}/limits</c>.</summary>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <param name="limits">Hard limit ceilings to register or tighten.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing the active effective <see cref="RemoteHardLimitsDto"/> or a forbidden relaxation error.</returns>
    public Task<BenchResult<RemoteHardLimitsDto>> RegisterOrTightenLimitsAsync(
        string resourceId,
        RemoteHardLimitsDto limits,
        CancellationToken cancellationToken = default) =>
        SendAsync<RemoteHardLimitsDto>(HttpMethod.Put, $"/api/v2/resources/{Uri.EscapeDataString(resourceId)}/limits", limits, cancellationToken);

    /// <summary>Query active fail-safe hard limits registered on a hosted instrument via <c>GET /api/v2/resources/{resourceId}/limits</c>.</summary>
    /// <param name="resourceId">Canonical target resource identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing the registered <see cref="RemoteHardLimitsDto"/>.</returns>
    public Task<BenchResult<RemoteHardLimitsDto>> GetHardLimitsAsync(string resourceId, CancellationToken cancellationToken = default) =>
        SendAsync<RemoteHardLimitsDto>(HttpMethod.Get, $"/api/v2/resources/{Uri.EscapeDataString(resourceId)}/limits", null, cancellationToken);

    private string Address(string path) => _endpoint is null ? path : new Uri(_endpoint, path).AbsoluteUri;

    private async Task<BenchResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, Address(path));
        if (!string.IsNullOrWhiteSpace(_bearerToken))
            message.Headers.TryAddWithoutValidation("X-BenchForge-Token", _bearerToken);
        if (body is not null)
            message.Content = JsonContent.Create(body);

        try
        {
            using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
            BenchWireResult<T>? wire = null;
            try
            {
                wire = await response.Content.ReadFromJsonAsync<BenchWireResult<T>>(cancellationToken: ct).ConfigureAwait(false);
            }
            catch
            {
                // Fall back to structured HTTP error below when server did not emit a JSON wire envelope.
            }

            if (wire is not null)
                return wire.ToResult();

            return BenchResult<T>.Fail(new BenchError(
                $"BF-HTTP-{(int)response.StatusCode}",
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? BenchErrorKind.Unauthorized
                    : response.StatusCode == System.Net.HttpStatusCode.Forbidden ? BenchErrorKind.Forbidden
                    : BenchErrorKind.ProtocolFault,
                $"Host returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) without a railroad envelope.",
                Confidence: method == HttpMethod.Get ? PhysicalStateConfidence.Unchanged : PhysicalStateConfidence.UnknownRequiresRecovery,
                Remedy: "Verify host URL, TLS/VLAN configuration, and host health endpoint."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return BenchResult<T>.Fail(new BenchError(
                "BF-HTTP-TIMEOUT",
                BenchErrorKind.Timeout,
                $"HTTP request {method} {path} timed out.",
                Confidence: method == HttpMethod.Get ? PhysicalStateConfidence.Unchanged : PhysicalStateConfidence.UnknownRequiresRecovery,
                RequiresRecovery: method != HttpMethod.Get,
                Remedy: "Do not blindly retry mutating requests; check host status and command receipt by CommandId."));
        }
        catch (OperationCanceledException)
        {
            return BenchResult<T>.Fail(new BenchError(
                "BF-HTTP-CANCELLED",
                method == HttpMethod.Get ? BenchErrorKind.CancelledBeforeDispatch : BenchErrorKind.UncertainPhysicalState,
                $"HTTP request {method} {path} was cancelled.",
                Confidence: method == HttpMethod.Get ? PhysicalStateConfidence.Unchanged : PhysicalStateConfidence.UnknownRequiresRecovery,
                RequiresRecovery: method != HttpMethod.Get));
        }
        catch (Exception ex)
        {
            return BenchResult<T>.Fail(new BenchError(
                "BF-HTTP-TRANSPORT",
                BenchErrorKind.TransportFault,
                $"Host transport error on {method} {path}: {ex.Message}",
                Confidence: method == HttpMethod.Get ? PhysicalStateConfidence.Unchanged : PhysicalStateConfidence.UnknownRequiresRecovery,
                RequiresRecovery: method != HttpMethod.Get,
                Remedy: "Check private lab network connectivity and host process health."));
        }
    }
}
