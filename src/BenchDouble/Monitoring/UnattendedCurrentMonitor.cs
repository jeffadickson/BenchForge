using BenchDouble.Simulation;
using UnitsNet;

namespace BenchDouble.Monitoring;

/// <summary>Immutable monitoring event. Notification delivery is not a safety action.</summary>
public sealed record CurrentAlarm(
    string RunId, TimeSpan SimulatedElapsed,
    ElectricCurrent Measured, ElectricCurrent Threshold, string Code);

/// <summary>DI extension point for alert delivery. A successful return does not prove human receipt.</summary>
public interface ICurrentAlarmSink
{
    ValueTask NotifyAsync(CurrentAlarm alarm, CancellationToken ct = default);
}

/// <summary>
/// Threshold monitor with exactly one alarm per armed interval. Its decisions are
/// independent of browser rendering, plotting, network access and notification provider.
/// </summary>
public sealed class UnattendedCurrentMonitor(
    ISimulationClock clock,
    ElectricCurrent threshold,
    ICurrentAlarmSink notifications)
{
    private bool _tripped;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsTripped => _tripped;
    public ElectricCurrent Threshold { get; } =
        double.IsFinite(threshold.Amperes) && threshold.Amperes > 0
            ? threshold : throw new ArgumentOutOfRangeException(nameof(threshold));

    /// <summary>Returns an alarm on the first above-threshold reading; otherwise null.</summary>
    public async ValueTask<CurrentAlarm?> ObserveAsync(
        string runId, ElectricCurrent current, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (!double.IsFinite(current.Amperes))
            throw new ArgumentOutOfRangeException(nameof(current));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_tripped || current.Amperes <= Threshold.Amperes) return null;
            var alarm = new CurrentAlarm(runId, clock.Elapsed, current, Threshold, "OVERCURRENT");
            // Latch alarm BEFORE calling the notifier; retry policy belongs to a durable outbox.
            _tripped = true;
            await notifications.NotifyAsync(alarm, ct).ConfigureAwait(false);
            return alarm;
        }
        finally { _gate.Release(); }
    }
}
