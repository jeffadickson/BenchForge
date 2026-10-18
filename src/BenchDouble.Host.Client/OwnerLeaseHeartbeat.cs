using System.Net;
using System.Net.Http.Json;
using BenchDouble.Host.Contracts;

namespace BenchDouble.Host.Client;

/// <summary>Renew canonical ownership independently of instrument requests; never opens, retries or releases a driver.</summary>
public sealed class OwnerLeaseHeartbeat : IAsyncDisposable
{
    private readonly HttpClient _client;
    private readonly string _resource;
    private readonly Guid _lease;
    private readonly Guid _peer;
    private readonly OwnerHeartbeatSnapshot _initial;
    private readonly SemaphoreSlim _renewGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private Task? _loop;
    private Task? _dispose;
    private long _sequence;
    private volatile string _status = "NotStarted";

    /// <summary>NotStarted, Healthy, Degraded, Lost or Stopped; health is not device readiness.</summary>
    public string Status => _status;
    /// <summary>Latest validated host acknowledgment, or null before the first successful renewal.</summary>
    public OwnerHeartbeatSnapshot? LastAcknowledgment { get; private set; }
    /// <summary>Owner-health notifications; observer exceptions cannot stop renewal.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Create a helper from a freshly inspected lease-scoped host snapshot.</summary>
    /// <param name="client">Caller-owned HTTP client with the host base address.</param>
    /// <param name="resourceId">Canonical resource ID.</param>
    /// <param name="leaseId">Current control capability; never used as a log correlation.</param>
    /// <param name="snapshot">Host epoch, generation and prior peer sequence from owner-health inspection.</param>
    /// <exception cref="ArgumentException">Identity, age, timing budgets or the loss policy are invalid or expired.</exception>
    public OwnerLeaseHeartbeat(HttpClient client, string resourceId, Guid leaseId, OwnerHeartbeatSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(client); ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(resourceId) || resourceId.Length > 80 || resourceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.')) ||
            leaseId == Guid.Empty || snapshot.HostEpoch == Guid.Empty || snapshot.OwnerGeneration <= 0 || snapshot.Sequence < 0 || snapshot.Sequence == long.MaxValue ||
            !double.IsFinite(snapshot.AgeMilliseconds) || snapshot.AgeMilliseconds < 0 ||
            !double.IsFinite(snapshot.ExpireMilliseconds) || snapshot.ExpireMilliseconds > 3600000 || snapshot.ExpireMilliseconds < snapshot.IntervalMilliseconds * 4 || snapshot.AgeMilliseconds >= snapshot.ExpireMilliseconds || snapshot.LossPolicy != "FenceAndRetain" ||
            !double.IsFinite(snapshot.IntervalMilliseconds) || snapshot.IntervalMilliseconds is < 10 or > 60000 || snapshot.State is not ("Healthy" or "Suspect") || snapshot.Sequence > 0 && snapshot.PeerEpoch == Guid.Empty)
            throw new ArgumentException("Invalid or expired heartbeat setup.");
        _client = client; _resource = resourceId; _lease = leaseId; _initial = snapshot;
        _peer = snapshot.PeerEpoch == Guid.Empty ? Guid.NewGuid() : snapshot.PeerEpoch; _sequence = snapshot.Sequence;
    }

    /// <summary>Start one loop with an immediate renewal, followed by the host-recommended interval.</summary>
    /// <exception cref="InvalidOperationException">Already started or stopped.</exception>
    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null || _dispose is not null) throw new InvalidOperationException("Heartbeat already started or stopped.");
            _loop = Task.Run(RunAsync);
        }
    }

    /// <summary>Renew once with serialized sequence allocation and a finite HTTP timeout.</summary>
    /// <param name="cancellationToken">Cancels this renewal; does not release ownership.</param>
    /// <returns>Current client health; terminal rejection becomes Lost and cannot renew again.</returns>
    public async Task<string> RenewAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _renewGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_status is "Lost" or "Stopped") return _status;
            linked.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(_initial.IntervalMilliseconds, 1000, 10000)));
            var sequence = checked(++_sequence);
            using var response = await _client.PostAsJsonAsync($"api/v1/setup/ivi/{Uri.EscapeDataString(_resource)}/heartbeat",
                new { leaseId = _lease, hostEpoch = _initial.HostEpoch, ownerGeneration = _initial.OwnerGeneration, peerEpoch = _peer, sequence }, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Set(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict ? "Lost" : "Degraded");
            var ack = await response.Content.ReadFromJsonAsync<OwnerHeartbeatSnapshot>(linked.Token).ConfigureAwait(false);
            if (ack is null || ack.HostEpoch != _initial.HostEpoch || ack.OwnerGeneration != _initial.OwnerGeneration || ack.PeerEpoch != _peer || ack.Sequence != sequence || ack.State != "Healthy" ||
                !double.IsFinite(ack.AgeMilliseconds) || ack.AgeMilliseconds < 0 || ack.AgeMilliseconds >= ack.ExpireMilliseconds ||
                ack.IntervalMilliseconds != _initial.IntervalMilliseconds || ack.ExpireMilliseconds != _initial.ExpireMilliseconds || ack.LossPolicy != "FenceAndRetain")
                return Set("Lost");
            LastAcknowledgment = ack; return Set("Healthy");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _stop.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or NotSupportedException) { return Set("Degraded"); }
        catch (OverflowException) { return Set("Lost"); }
        finally { _renewGate.Release(); }
    }

    private string Set(string status)
    {
        _status = status;
        foreach (var handler in StatusChanged?.GetInvocationList() ?? [])
            try { ((Action<string>)handler)(status); } catch { /* Observers cannot change owner health. */ }
        return status;
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (await RenewAsync(_stop.Token).ConfigureAwait(false) == "Lost") return;
                await Task.Delay(TimeSpan.FromMilliseconds(_initial.IntervalMilliseconds), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    /// <summary>Stop renewal once; does not dispose the HTTP client, release the lease or imply safe hardware state.</summary>
    /// <returns>Completion after the loop and admitted renewal stop.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_dispose ??= Task.Run(StopAsync));
    }

    private async Task StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        await _renewGate.WaitAsync().ConfigureAwait(false);
        try { Set("Stopped"); } finally { _renewGate.Release(); }
        // Keep cancellation state available to later calls, which must remain cancelled.
    }
}
