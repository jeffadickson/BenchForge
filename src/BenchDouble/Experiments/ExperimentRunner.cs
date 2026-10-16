using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BenchDouble.Experiments;

/// <summary>Terminal or active experiment lifecycle state.</summary>
public enum ExperimentStatus
{
    /// <summary>Procedure has not started.</summary>
    Created,
    /// <summary>Procedure is executing.</summary>
    Running,
    /// <summary>Procedure and shutdown completed successfully.</summary>
    Completed,
    /// <summary>Execution was cancelled and shutdown completed.</summary>
    Cancelled,
    /// <summary>Execution failed but shutdown completed.</summary>
    Failed,
    /// <summary>Shutdown did not confirm completion; physical state is unknown.</summary>
    RecoveryRequired
}

/// <summary>A typed measurement row with independent field values and units.</summary>
/// <summary>Represent a timestamped row of named measurements.</summary>
/// <param name="Timestamp">UTC timestamp associated with the sample.</param>
/// <param name="Values">Measurement values keyed by field name.</param>
/// <param name="Units">Optional display units keyed by field name.</param>
public sealed record ExperimentSample(DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, string>? Units = null);

/// <summary>Immutable result of a completed or interrupted experiment run.</summary>
/// <summary>Represent an experiment's terminal outcome and timing.</summary>
/// <param name="Id">Unique experiment run identifier.</param>
/// <param name="Name">Procedure name.</param>
/// <param name="Status">Final lifecycle status.</param>
/// <param name="StartedAt">UTC run start timestamp.</param>
/// <param name="FinishedAt">UTC run completion timestamp.</param>
/// <param name="Error">Optional execution or cleanup exception.</param>
public sealed record ExperimentRun(Guid Id, string Name, ExperimentStatus Status,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, Exception? Error = null);

/// <summary>Sink for experiment data; implementations can persist to CSV, SQL, or streams.</summary>
public interface IExperimentResultSink
{
    /// <summary>Persist one sample for a specific experiment.</summary>
    /// <param name="experimentId">Experiment run identifier.</param>
    /// <param name="sample">Measurement row to write.</param>
    /// <param name="cancellationToken">Cancellation while writing the sample.</param>
    /// <returns>Completion of the sink write.</returns>
    ValueTask WriteAsync(Guid experimentId, ExperimentSample sample, CancellationToken cancellationToken);
}

/// <summary>Thread-safe memory sink suitable for tests and prototype applications.</summary>
public sealed class InMemoryExperimentResultSink : IExperimentResultSink
{
    private readonly ConcurrentQueue<(Guid Id, ExperimentSample Sample)> _samples = new();
    /// <summary>Get a stable snapshot of all in-memory experiment samples.</summary>
    public IReadOnlyList<(Guid Id, ExperimentSample Sample)> Samples => _samples.ToArray();

    /// <summary>Enqueue a measurement row in the in-memory sink.</summary>
    /// <param name="experimentId">Experiment run identifier.</param>
    /// <param name="sample">Measurement row to enqueue.</param>
    /// <param name="cancellationToken">Cancellation before enqueue.</param>
    /// <returns>Completion of the in-memory enqueue.</returns>
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
    /// <summary>Get the current experiment run identifier.</summary>
    public Guid RunId { get; }
    /// <summary>Get the result sink shared by this experiment run.</summary>
    public IExperimentResultSink Results { get; }
    /// <summary>Get the structured logger associated with the experiment run.</summary>
    public ILogger Logger { get; }
    /// <summary>Write a sample through the experiment's result sink.</summary>
    /// <param name="sample">Measurement row to persist.</param>
    /// <param name="token">Cancellation forwarded to the sink.</param>
    /// <returns>Completion of the sample write.</returns>
    public ValueTask EmitAsync(ExperimentSample sample, CancellationToken token = default)
        => Results.WriteAsync(RunId, sample, token);
}

/// <summary>
/// Lifecycle contract for a user-written experiment.
/// Shutdown must make best-effort hardware cleanup even after failed execution.
/// </summary>
public interface IExperimentProcedure
{
    /// <summary>Get the human-readable procedure name.</summary>
    string Name { get; }
    /// <summary>Initialize the procedure before execution.</summary>
    /// <param name="context">Run-scoped result and logging context.</param>
    /// <param name="cancellationToken">Cancellation requested by the caller.</param>
    /// <returns>Completion of procedure startup.</returns>
    Task StartupAsync(ExperimentContext context, CancellationToken cancellationToken);
    /// <summary>Execute the experiment's measurement procedure.</summary>
    /// <param name="context">Run-scoped result and logging context.</param>
    /// <param name="cancellationToken">Cancellation requested by the caller.</param>
    /// <returns>Completion of procedure execution.</returns>
    Task ExecuteAsync(ExperimentContext context, CancellationToken cancellationToken);
    /// <summary>Perform best-effort equipment cleanup.</summary>
    /// <param name="context">Run-scoped result and logging context.</param>
    /// <param name="cancellationToken">Bounded shutdown cancellation token.</param>
    /// <returns>Completion of procedure shutdown.</returns>
    Task ShutdownAsync(ExperimentContext context, CancellationToken cancellationToken);
}

/// <summary>Convenient base for experiments without special startup/teardown.</summary>
public abstract class ExperimentProcedure : IExperimentProcedure
{
    /// <summary>Get the procedure's human-readable name.</summary>
    public abstract string Name { get; }
    /// <summary>Perform optional procedure initialization.</summary>
    /// <param name="context">Run-scoped result and logging context.</param>
    /// <param name="token">Cancellation requested by the runner.</param>
    /// <returns>Completion of startup.</returns>
    public virtual Task StartupAsync(ExperimentContext context, CancellationToken token) => Task.CompletedTask;
    /// <summary>Execute the required experiment procedure.</summary>
    /// <param name="context">Run-scoped result and logging context.</param>
    /// <param name="token">Cancellation requested by the runner.</param>
    /// <returns>Completion of execution.</returns>
    public abstract Task ExecuteAsync(ExperimentContext context, CancellationToken token);
    /// <summary>Perform optional best-effort procedure shutdown.</summary>
    /// <param name="context">Run-scoped result and logging context.</param>
    /// <param name="token">Cancellation requested by the cleanup policy.</param>
    /// <returns>Completion of shutdown.</returns>
    public virtual Task ShutdownAsync(ExperimentContext context, CancellationToken token) => Task.CompletedTask;
}

/// <summary>
/// Limits how long the runner waits for best-effort shutdown. A timeout means
/// physical state is unknown; cancellation does not imply instrument abort.
/// </summary>
public sealed record ExperimentRunnerOptions
{
    /// <summary>Get the maximum duration allowed for best-effort shutdown.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Reject an infinite or nonpositive shutdown timeout.</summary>
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
    public bool RequiresRecovery => Volatile.Read(ref _recoveryRequired);

    /// <summary>Create a serialized experiment runner with structured logging.</summary>
    /// <param name="sink">Result sink receiving emitted samples.</param>
    /// <param name="logger">Structured logger for lifecycle and failures.</param>
    /// <param name="options">Optional shutdown timeout configuration.</param>
    public ExperimentRunner(IExperimentResultSink sink, ILogger<ExperimentRunner> logger,
        ExperimentRunnerOptions? options = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new ExperimentRunnerOptions();
        _options.Validate();
    }

    /// <summary>Execute one procedure under exclusive runner ownership and bounded cleanup.</summary>
    /// <param name="procedure">Experiment procedure to execute.</param>
    /// <param name="cancellationToken">Cancellation requested for startup and execution.</param>
    /// <returns>Immutable run result including status, timestamps, and errors.</returns>
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
                Volatile.Write(ref _recoveryRequired, true);
            _logger.LogInformation("Experiment {ExperimentId} finished with status {Status}", id, status);
            return new ExperimentRun(id, procedure.Name, status, started, DateTimeOffset.UtcNow, error);
        }
        finally
        {
            _gate.Release();
        }
    }
}
