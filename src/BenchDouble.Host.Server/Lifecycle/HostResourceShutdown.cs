using System.Diagnostics;
using BenchDouble.Host.Server.Lifecycle;

/// <summary>Canonical admission and bounded shutdown coordination for registered resources.</summary>
public sealed partial class HostResourceRegistry
{
    private bool _draining;

    /// <summary>Whether this registry has permanently stopped accepting new ownership and command dispatch.</summary>
    public bool IsDraining { get { lock (_gate) return _draining; } }

    /// <summary>Stop new canonical grants and dispatch; in-flight operations retain ownership. Idempotent and irreversible.</summary>
    public void BeginDrain()
    {
        lock (_gate)
        {
            if (_draining) return;
            _draining = true;
            _logger.LogInformation(new EventId(7480, "HostAdmissionStopped"), "Canonical host admission stopped");
        }
    }

    private void EnsureAccepting()
    {
        if (_draining) throw new InvalidOperationException("Host is stopping; new instrument work is refused.");
    }
    private int _shutdownWorkPending;

    /// <summary>Cleanup workers still queued or executing, including those retained after a decision timeout.</summary>
    public int ShutdownWorkPending => Volatile.Read(ref _shutdownWorkPending);

    private Task<HostShutdownReport>? _shutdownTask;
    private HostShutdownReport? _shutdownReport;

    /// <summary>Cached shutdown result; null until the bounded coordinator wait finishes. No hardware I/O.</summary>
    public HostShutdownReport? ShutdownReport { get { lock (_gate) return _shutdownReport; } }

    /// <summary>Stop admission and coordinate cleanup once; concurrent callers share the first policy and result.</summary>
    /// <param name="policy">Host-owned wait and worker limits, independent of run/request cancellation.</param>
    /// <returns>Bounded decision report. Timed-out synchronous work remains owned and can still be running.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Policy limits are invalid; admission remains unchanged.</exception>
    public Task<HostShutdownReport> ShutdownAsync(HostShutdownPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        TaskCompletionSource<HostShutdownReport> completion;
        KeyValuePair<Guid, string>[] leases;
        string[] quarantined;
        lock (_gate)
        {
            if (_shutdownTask is not null) return _shutdownTask;
            BeginDrain();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdownTask = completion.Task;
            leases = _byLease.ToArray(); quarantined = _startupQuarantine.ToArray();
            _shutdownWorkPending = leases.Length;
        }
        _ = CompleteShutdownAsync(policy, leases, quarantined, completion);
        return completion.Task;
    }

    private async Task CompleteShutdownAsync(HostShutdownPolicy policy, KeyValuePair<Guid, string>[] leases,
        string[] quarantined, TaskCompletionSource<HostShutdownReport> completion)
    {
        var startedUtc = DateTimeOffset.UtcNow; var started = Stopwatch.GetTimestamp();
        _logger.LogInformation(new EventId(7481, "HostShutdownStarted"),
            "Canonical shutdown started with {ResourceCount} owners, {TimeoutMilliseconds} ms and {MaxConcurrency} workers",
            leases.Length, policy.Timeout.TotalMilliseconds, policy.MaxConcurrency);
        var outcomes = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in quarantined) outcomes[id] = "StartupQuarantine";
        foreach (var lease in leases) outcomes[lease.Value] = "DeadlineExceeded";
        var deadline = new CancellationTokenSource(policy.Timeout);
        var slots = new SemaphoreSlim(policy.MaxConcurrency);
        async Task CloseOne(KeyValuePair<Guid, string> lease)
        {
            var acquired = false;
            try
            {
                await slots.WaitAsync(deadline.Token).ConfigureAwait(false); acquired = true;
                // Synchronous vendor calls never consume the coordinator's thread or ordinary worker pool.
                var outcome = await Task.Factory.StartNew(() =>
                {
                    try
                    {
                        ReleaseManagedOrPlain(lease.Key, shutdown: true, deadline.Token);
                        return "Closed";
                    }
                    catch (OperationCanceledException) { return "DeadlineExceeded"; }
                    catch (NotSupportedException) { return "ProviderMissing"; }
                    catch { return "RecoveryRequired"; }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
                outcomes[lease.Value] = outcome;
                _logger.LogInformation(new EventId(7482, "HostShutdownResourceCompleted"),
                    "Shutdown worker {ResourceId} completed with {Outcome}", lease.Value, outcome);
            }
            catch (OperationCanceledException) { }
            finally { if (acquired) slots.Release(); Interlocked.Decrement(ref _shutdownWorkPending); }
        }
        var workers = Task.WhenAll(leases.Select(CloseOne));
        try { await workers.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* A decision timeout does not cancel synchronous device work. */ }
        catch { /* Unexpected worker faults cannot declare retained ownership safe. */ }
        var items = outcomes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new HostShutdownItem(p.Key, p.Value)).ToArray();
        var report = new HostShutdownReport(startedUtc, Stopwatch.GetElapsedTime(started).TotalMilliseconds, Array.AsReadOnly(items));
        foreach (var item in items.Where(i => i.Outcome == "DeadlineExceeded"))
            _logger.LogWarning(new EventId(7484, "HostShutdownDeadline"), "Shutdown deadline for {ResourceId}; ownership outcome remains unconfirmed", item.ResourceId);
        _logger.LogInformation(new EventId(7483, "HostShutdownDecision"),
            "Shutdown decision in {ElapsedMilliseconds} ms with {PendingWorkers} pending workers and {UnconfirmedResources} unconfirmed resources",
            report.ElapsedMilliseconds, ShutdownWorkPending, items.Count(i => i.Outcome != "Closed"));
        lock (_gate) _shutdownReport = report;
        completion.TrySetResult(report);
        // Keep cancellation and slot objects alive until all detached workers actually finish.
        _ = workers.ContinueWith(t => { _ = t.Exception; deadline.Dispose(); slots.Dispose(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

}
