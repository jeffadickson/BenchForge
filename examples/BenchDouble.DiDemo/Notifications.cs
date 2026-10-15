namespace BenchDouble.DiDemo;

/// <summary>Operator-notification boundary; real transports belong in separate, credentialed adapters.</summary>
public interface IOperatorNotificationSink
{
    Task NotifyAsync(string runId, string alarm, CancellationToken cancellationToken = default);
}

/// <summary>
/// Demonstration sink. Emits explicit fake email/SMS events to the console.
/// No SMTP, SMS gateway, network request, or credential access is performed.
/// </summary>
public sealed class SimulatedOperatorNotificationSink : IOperatorNotificationSink
{
    public Task NotifyAsync(string runId, string alarm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"[ALARM] Run {runId}: {alarm}");
        Console.WriteLine("[NOTIFICATION SIMULATION] EMAIL queued for operator@example.invalid (not sent).");
        Console.WriteLine("[NOTIFICATION SIMULATION] SMS queued for test operator (not sent).");
        return Task.CompletedTask;
    }
}
