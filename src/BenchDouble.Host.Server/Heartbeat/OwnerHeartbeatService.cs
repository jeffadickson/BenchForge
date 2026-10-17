namespace BenchDouble.Host.Server.Heartbeat;

/// <summary>Observe owner silence independently of device I/O. Expiry fences and retains; no driver recovery is inferred.</summary>
/// <param name="registry">Canonical ownership authority.</param>
/// <param name="policy">Validated host heartbeat interval.</param>
public sealed class OwnerHeartbeatService(HostResourceRegistry registry, HostHeartbeatPolicy policy) : BackgroundService
{
    /// <summary>Evaluate health on a real-time periodic timer until the host stops.</summary>
    /// <param name="stoppingToken">Host service cancellation; does not release instruments.</param>
    /// <returns>Completion after periodic monitoring stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        policy.Validate();
        using var timer = new PeriodicTimer(policy.Interval);
        try
        {
            do { registry.ObserveOwnerHeartbeats(); }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
