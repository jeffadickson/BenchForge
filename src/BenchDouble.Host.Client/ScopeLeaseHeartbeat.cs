using System.Net;

namespace BenchDouble.Host.Client;

/// <summary>
/// Automatically renews a host-owned reservation and reports lost connectivity.
/// This client-side helper does not prove instrument readiness or physical safe state.
/// </summary>
public sealed class ScopeLeaseHeartbeat : IAsyncDisposable
{
    private readonly HttpClient _client;
    private readonly string _resourceId;
    private readonly Guid _leaseId;
    private readonly TimeSpan _period;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private Task? _disposal;
    private readonly object _lifecycle = new();

    /// <summary>Latest observed renewal status.</summary>
    public ScopeHeartbeatStatus Status { get; private set; } = ScopeHeartbeatStatus.NotStarted;
    /// <summary>Raised after a renewal succeeds or fails. Handler exceptions are isolated.</summary>
    public event Action<ScopeHeartbeatStatus>? StatusChanged;

    /// <summary>Configure periodic lease renewal without transferring ownership of the HTTP client.</summary>
    /// <param name="client">Caller-owned HTTP transport used for renewal requests.</param>
    /// <param name="resourceId">Host resource identifier without path separators.</param>
    /// <param name="leaseId">Nonempty exclusive lease identifier.</param>
    /// <param name="period">Renewal interval greater than zero and at most one minute; defaults to 20 seconds.</param>
    public ScopeLeaseHeartbeat(HttpClient client, string resourceId, Guid leaseId, TimeSpan? period = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(resourceId) || resourceId.Length > 80 || resourceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))) throw new ArgumentException("Invalid resource.", nameof(resourceId));
        if (leaseId == Guid.Empty) throw new ArgumentException("Lease ID is required.", nameof(leaseId));
        _resourceId = resourceId;
        _leaseId = leaseId;
        _period = period ?? TimeSpan.FromSeconds(20);
        if (_period <= TimeSpan.Zero || _period > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(period));
    }

    /// <summary>Begin renewal immediately, then periodically; callers should stop using the instrument after status Lost.</summary>
    public void Start()
    {
        lock (_lifecycle)
        {
            if (_loop is not null || _disposal is not null) throw new InvalidOperationException("Heartbeat already started or stopped.");
            _loop = Task.Run(() => RunAsync(_stop.Token));
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_period);
        try
        {
            do
            {
                token.ThrowIfCancellationRequested();
                ScopeHeartbeatStatus status;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post,
                        $"api/v1/scopes/{Uri.EscapeDataString(_resourceId)}/reserve/{_leaseId:D}/renew");
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(_period.TotalMilliseconds, 1000, 10000)));
                    using var response = await _client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    status = response.IsSuccessStatusCode ? ScopeHeartbeatStatus.Healthy :
                        response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                            ? ScopeHeartbeatStatus.Lost : ScopeHeartbeatStatus.Degraded;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (HttpRequestException) { status = ScopeHeartbeatStatus.Degraded; }
                catch (TaskCanceledException) { status = ScopeHeartbeatStatus.Degraded; }
                Status = status;
                foreach (var handler in StatusChanged?.GetInvocationList() ?? [])
                    try { ((Action<ScopeHeartbeatStatus>)handler)(status); } catch { /* isolate each observer */ }
                if (status == ScopeHeartbeatStatus.Lost) break;
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    /// <summary>Stop the background renewal loop; does not release the host lease.</summary>
    /// <returns>Completion after cancellation and shutdown of the renewal loop.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_lifecycle) return new(_disposal ??= Task.Run(StopAsync));
    }

    private async Task StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _stop.Dispose();
    }
}

/// <summary>Client's view of renewal outcomes, not authoritative device health.</summary>
public enum ScopeHeartbeatStatus
{
    /// <summary>Renewal has not started.</summary>
    NotStarted,
    /// <summary>The latest lease renewal succeeded.</summary>
    Healthy,
    /// <summary>Renewal failed transiently; instrument ownership is not confirmed.</summary>
    Degraded,
    /// <summary>The host rejected the lease or could not find it; stop instrument use.</summary>
    Lost
}
