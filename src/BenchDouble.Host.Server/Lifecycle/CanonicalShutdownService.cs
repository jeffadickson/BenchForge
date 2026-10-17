namespace BenchDouble.Host.Server.Lifecycle;

/// <summary>Connect host stopping to canonical admission and bounded cleanup; does not infer physical safety.</summary>
/// <param name="registry">Existing ownership authority.</param>
/// <param name="lifetime">Application stopping signal, independent of individual requests.</param>
/// <param name="policy">Validated host-owned cleanup budget.</param>
public sealed class CanonicalShutdownService(HostResourceRegistry registry, IHostApplicationLifetime lifetime,
    HostShutdownPolicy policy) : IHostedService, IDisposable
{
    private CancellationTokenRegistration _stopping;

    /// <summary>Install the drain signal; does not open or probe instruments.</summary>
    /// <param name="cancellationToken">Startup cancellation; honored before registration.</param>
    /// <returns>Completed registration task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); policy.Validate();
        _stopping = lifetime.ApplicationStopping.Register(registry.BeginDrain);
        return Task.CompletedTask;
    }

    /// <summary>Await the bounded cleanup decision using the independent host policy, even when the caller is cancelled.</summary>
    /// <param name="cancellationToken">Outer host token; deliberately does not cancel device cleanup or erase ownership.</param>
    /// <returns>Completion when the coordinator reports outcomes; synchronous timed-out workers may remain.</returns>
    public async Task StopAsync(CancellationToken cancellationToken) => await registry.ShutdownAsync(policy).ConfigureAwait(false);

    /// <summary>Remove the stopping callback; never closes drivers or clears retained ownership.</summary>
    public void Dispose() => _stopping.Dispose();
}
