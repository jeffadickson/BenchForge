using Microsoft.Extensions.Logging;

namespace BenchDouble.Experiments;

/// <summary>Isolate diagnostic failures from procedure execution and mandatory cleanup.</summary>
internal sealed class ExperimentDiagnostics(ILogger<ExperimentRunner> inner) : ILogger<ExperimentRunner>
{
    private long _failures;
    /// <summary>Observed diagnostic sink failures over this runner's lifetime.</summary>
    internal long FailureCount => Interlocked.Read(ref _failures);
    /// <inheritdoc/>
    public bool IsEnabled(LogLevel level)
    {
        try { return inner.IsEnabled(level); }
        catch { Interlocked.Increment(ref _failures); return false; }
    }
    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        try { return inner.BeginScope(state) is { } scope ? new Scope(scope,()=>Interlocked.Increment(ref _failures)) : null; }
        catch { Interlocked.Increment(ref _failures); return null; }
    }
    /// <inheritdoc/>
    public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter)
    {
        try { inner.Log(level,id,state,error,formatter); }
        catch { Interlocked.Increment(ref _failures); }
    }
    private sealed class Scope(IDisposable inner,Action failed) : IDisposable
    {
        private IDisposable? _inner=inner;
        /// <inheritdoc/>
        public void Dispose() { try { Interlocked.Exchange(ref _inner,null)?.Dispose(); } catch { failed(); } }
    }
}
