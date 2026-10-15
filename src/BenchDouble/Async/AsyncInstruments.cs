using System.Globalization;

namespace BenchDouble.Async;

/// <summary>Async SCPI transport contract. Implementations must obey CancellationToken cooperatively.</summary>
public interface IAsyncScpiSession
{
    ValueTask WriteAsync(string command, CancellationToken cancellationToken = default);
    ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Serializes accesses to one SCPI connection, including a query's write/read pair.
/// A cancelled waiter does not issue a command. Cancellation during I/O does not
/// guarantee that an instrument has aborted; callers must perform safe-state recovery.
/// </summary>
public sealed class SerializedAsyncScpiSession : IAsyncScpiSession, IDisposable
{
    private readonly IAsyncScpiSession _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public SerializedAsyncScpiSession(IAsyncScpiSession inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _inner.WriteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _inner.QueryAsync(command, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _disposed = true;
        // Dispose only our synchronization primitive; underlying session is caller-owned.
        // Caller must ensure no operations are in flight when disposing.
        _gate.Dispose();
    }
}

/// <summary>
/// Compatibility adapter for existing synchronous mocks/simulators. Synchronous
/// I/O still blocks the calling thread: this does NOT magically become true async I/O.
/// </summary>
public sealed class SynchronousScpiAdapter : IAsyncScpiSession
{
    private readonly IScpiSession _session;
    public SynchronousScpiAdapter(IScpiSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    public ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _session.Write(command);
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_session.Query(command));
    }
}

/// <summary>Asynchronous DMM class facade with explicit acquisition operation.</summary>
public sealed class AsyncDigitalMultimeter(IAsyncScpiSession session)
{
    public ValueTask ConfigureDcVoltageAsync(CancellationToken token = default) =>
        session.WriteAsync("CONF:VOLT:DC", token);

    public async ValueTask<double> MeasureDcVoltageAsync(CancellationToken token = default)
    {
        var response = await session.QueryAsync("MEAS:VOLT:DC?", token).ConfigureAwait(false);
        return double.Parse(response, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

/// <summary>Asynchronous function-generator facade for the current supported sine profile.</summary>
public sealed class AsyncFunctionGenerator(IAsyncScpiSession session)
{
    public async ValueTask ConfigureSineAsync(int channel, double frequencyHz, double amplitudeVpp,
        CancellationToken token = default)
    {
        if (channel is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        if (!double.IsFinite(amplitudeVpp) || amplitudeVpp <= 0) throw new ArgumentOutOfRangeException(nameof(amplitudeVpp));
        await session.WriteAsync($"SOUR{channel}:FUNC SIN", token).ConfigureAwait(false);
        await session.WriteAsync($"SOUR{channel}:FREQ {frequencyHz.ToString("G17", CultureInfo.InvariantCulture)}", token).ConfigureAwait(false);
        await session.WriteAsync($"SOUR{channel}:VOLT {amplitudeVpp.ToString("G17", CultureInfo.InvariantCulture)}", token).ConfigureAwait(false);
    }

    public ValueTask SetOutputAsync(int channel, bool enabled, CancellationToken token = default)
    {
        if (channel is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        return session.WriteAsync($"OUTP{channel} {(enabled ? "ON" : "OFF")}", token);
    }
}
