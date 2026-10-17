using Microsoft.Extensions.Logging;

namespace BenchDouble.Host.Server;

/// <summary>Prevents a diagnostic sink exception from changing instrument ownership or replaying I/O.</summary>
/// <typeparam name="T">Logging category.</typeparam>
/// <param name="inner">Diagnostic sink whose exceptions are counted and isolated.</param>
internal sealed class FailureIsolatedLogger<T>(ILogger<T> inner) : ILogger<T>
{
    private long _failures;
    /// <summary>Observed sink exceptions during this wrapper lifetime.</summary>
    internal long FailureCount => Interlocked.Read(ref _failures);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level)
    {
        try { return inner.IsEnabled(level); }
        catch { Interlocked.Increment(ref _failures); return false; }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        try { return inner.BeginScope(state) is { } scope ? new Scope(scope, () => Interlocked.Increment(ref _failures)) : null; }
        catch { Interlocked.Increment(ref _failures); return null; }
    }

    /// <inheritdoc />
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try { inner.Log(level, id, state, exception, formatter); }
        catch { Interlocked.Increment(ref _failures); }
    }

    private sealed class Scope(IDisposable inner, Action failed) : IDisposable
    {
        public void Dispose() { try { inner.Dispose(); } catch { failed(); } }
    }
}
