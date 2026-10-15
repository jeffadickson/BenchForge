using System.Collections.Concurrent;

namespace BenchDouble.Async;

/// <summary>
/// Coordinates exclusive in-process access by canonical resource identity.
/// All instrument factories in the process must share one manager instance.
/// This does not provide cross-process or distributed exclusivity.
/// </summary>
public sealed class InstrumentReservationManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<InstrumentLease> ReserveAsync(
        string resource, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Instrument resource name is required.", nameof(resource));
        string key = resource.Trim();
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new InstrumentLease(key, gate);
    }
}

/// <summary>
/// An exclusive reservation. Disposal releases exactly once, including during
/// exceptions or cancellation. Does not automatically reset physical hardware.
/// </summary>
public sealed class InstrumentLease : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _gate;
    private int _released;

    internal InstrumentLease(string resource, SemaphoreSlim gate)
    {
        Resource = resource;
        _gate = gate;
    }

    public string Resource { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _gate.Release();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
