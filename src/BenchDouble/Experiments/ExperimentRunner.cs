using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BenchDouble.Experiments;

/// <summary>Terminal or active experiment lifecycle state.</summary>
public enum ExperimentStatus { Created, Running, Completed, Cancelled, Failed, RecoveryRequired }

/// <summary>A typed measurement row with independent field values and units.</summary>
public sealed record ExperimentSample(DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, string>? Units = null);

/// <summary>Immutable result of a completed or interrupted experiment run.</summary>
public sealed record ExperimentRun(Guid Id, string Name, ExperimentStatus Status,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, Exception? Error = null);

/// <summary>Sink for experiment data; implementations can persist to CSV, SQL, or streams.</summary>
public interface IExperimentResultSink
{
    ValueTask WriteAsync(Guid experimentId, ExperimentSample sample, CancellationToken cancellationToken);
}

/// <summary>Thread-safe memory sink suitable for tests and prototype applications.</summary>
public sealed class InMemoryExperimentResultSink : IExperimentResultSink
{
    private readonly ConcurrentQueue<(Guid Id, ExperimentSample Sample)> _samples = new();
    public IReadOnlyList<(Guid Id, ExperimentSample Sample)> Samples => _samples.ToArray();

    public ValueTask WriteAsync(Guid experimentId, ExperimentSample sample, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _samples.Enqueue((experimentId, sample));
        return ValueTask.CompletedTask;
    }
}

/// <summary>Run-scoped facilities supplied to a procedure.</summary>
public sealed class ExperimentContext
{
    internal ExperimentContext(Guid id, IExperimentResultSink results, ILogger logger)
    {
        RunId = id;
        Results = results;
        Logger = logger;
    }
    public Guid RunId { get; }
    public IExperimentResultSink Results { get; }
    public ILogger Logger { get; }
    public ValueTask EmitAsync(ExperimentSample sample, CancellationToken token = default)
        => Results.WriteAsync(RunId, sample, token);
}

/// <summary>
/// Lifecycle contract for a user-written experiment.
/// Shutdown must make best-effort hardware cleanup even after failed execution.
/// </summary>
public interface IExperimentProcedure
{
    string Name { get; }
    Task StartupAsync(ExperimentContext context, CancellationToken cancellationToken);
    Task ExecuteAsync(ExperimentContext context, CancellationToken cancellationToken);
    Task ShutdownAsync(ExperimentContext context, CancellationToken cancellationToken);
}

/// <summary>Convenient base for experiments without special startup/teardown.</summary>
public abstract class ExperimentProcedure : IExperimentProcedure
{
    public abstract string Name { get; }
    public virtual Task StartupAsync(ExperimentContext context, CancellationToken token) => Task.CompletedTask;
    public abstract Task ExecuteAsync(ExperimentContext context, CancellationToken token);
    public virtual Task ShutdownAsync(ExperimentContext context, CancellationToken token) => Task.CompletedTask;
}

/// <summary>
/// Limits how long the runner waits for best-effort shutdown. A timeout means
/// physical state is unknown; cancellation does not imply instrument abort.
/// </summary>
public sealed record ExperimentRunnerOptions
{
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public void Validate()
    {
        if (ShutdownTimeout <= TimeSpan.Zero || ShutdownTimeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout),
                "Shutdown timeout must be finite and positive.");
    }
}

/// <summary>
/// Executes startup/execute/shutdown with exactly one active procedure per runner.
/// A separate runner can execute independently. No implicit hardware fallback.
/// </summary>
public sealed class ExperimentRunner
{
    private readonly IExperimentResultSink _sink;
    private readonly ILogger<ExperimentRunner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ExperimentRunnerOptions _options;
    private bool _recoveryRequired;

    /// <summary>True after an unconfirmed shutdown; this runner rejects subsequent runs.</summary>
    public bool RequiresRecovery => _recoveryRequired;

    public ExperimentRunner(IExperimentResultSink sink, ILogger<ExperimentRunner> logger,
        ExperimentRunnerOptions? options = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new ExperimentRunnerOptions();
        _options.Validate();
    }

    public async Task<ExperimentRun> RunAsync(IExperimentProcedure procedure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_recoveryRequired)
                throw new InvalidOperationException(
                    "Runner is blocked: previous shutdown did not confirm completion. " +
                    "Perform external equipment recovery before creating a new runner.");
            var id = Guid.NewGuid();
            var started = DateTimeOffset.UtcNow;
            var context = new ExperimentContext(id, _sink, _logger);
            using var scope = _logger.BeginScope(new Dictionary<string, object> { ["ExperimentId"] = id, ["Procedure"] = procedure.Name });
            Exception? error = null;
            var status = ExperimentStatus.Completed;
            _logger.LogInformation("Experiment {ExperimentId} started: {Procedure}", id, procedure.Name);
            try
            {
                await procedure.StartupAsync(context, cancellationToken).ConfigureAwait(false);
                await procedure.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                error = exception;
                status = ExperimentStatus.Cancelled;
                _logger.LogWarning("Experiment {ExperimentId} cancelled", id);
            }
            catch (Exception exception)
            {
                error = exception;
                status = ExperimentStatus.Failed;
                _logger.LogError(exception, "Experiment {ExperimentId} failed", id);
            }
            finally
            {
                // Use an independent, bounded cleanup policy. Cancellation of
                // the original run cannot suppress best-effort shutdown.
                // A timeout/fault never establishes a physical safe state.
                using var cleanupCts = new CancellationTokenSource();
                Task? cleanup = null;
                try
                {
                    cleanup = procedure.ShutdownAsync(context, cleanupCts.Token);
                    await cleanup.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false);
                }
                catch (TimeoutException timeout)
                {
                    cleanupCts.Cancel();
                    // If the driver ignores cancellation, shutdown may still
                    // be executing. Observe later faults; do not falsely
                    // report Completed, Failed or safe-state confirmation.
                    if (cleanup is not null)
                    {
                        _ = cleanup.ContinueWith(
                            task => { _ = task.Exception; },
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted |
                                TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                    _logger.LogCritical(timeout,
                        "Experiment {ExperimentId} cleanup timed out; physical state unknown", id);
                    error = error is null ? timeout : new AggregateException(error, timeout);
                    status = ExperimentStatus.RecoveryRequired;
                }
                catch (Exception shutdownError)
                {
                    _logger.LogCritical(shutdownError,
                        "Experiment {ExperimentId} cleanup failed; physical state unknown", id);
                    error = error is null ? shutdownError : new AggregateException(error, shutdownError);
                    status = ExperimentStatus.RecoveryRequired;
                }
            }
            if (status == ExperimentStatus.RecoveryRequired)
                _recoveryRequired = true;
            _logger.LogInformation("Experiment {ExperimentId} finished with status {Status}", id, status);
            return new ExperimentRun(id, procedure.Name, status, started, DateTimeOffset.UtcNow, error);
        }
        finally
        {
            _gate.Release();
        }
    }
}
