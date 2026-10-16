using System.Globalization;

namespace BenchDouble.Async;

/// <summary>Async SCPI transport contract. Implementations must obey CancellationToken cooperatively.</summary>
public interface IAsyncScpiSession
{
    /// <summary>Write one SCPI command asynchronously.</summary>
    /// <param name="command">Command text sent to the instrument.</param>
    /// <param name="cancellationToken">Cancellation propagated to the transport.</param>
    /// <returns>Completion of the command write.</returns>
    ValueTask WriteAsync(string command, CancellationToken cancellationToken = default);
    /// <summary>Execute a SCPI query and receive its response.</summary>
    /// <param name="command">SCPI query text.</param>
    /// <param name="cancellationToken">Cancellation propagated to the transport.</param>
    /// <returns>Raw response from the instrument.</returns>
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

    /// <summary>Wrap a caller-owned transport with exclusive per-command synchronization.</summary>
    /// <param name="inner">Underlying SCPI session; its lifetime remains caller-owned.</param>
    public SerializedAsyncScpiSession(IAsyncScpiSession inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Serialize a SCPI write against other operations on this session.</summary>
    /// <param name="command">SCPI command to write.</param>
    /// <param name="cancellationToken">Cancellation while waiting or writing.</param>
    /// <returns>Completion of the serialized command write.</returns>
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

    /// <summary>Serialize the complete SCPI query operation.</summary>
    /// <param name="command">SCPI query to execute.</param>
    /// <param name="cancellationToken">Cancellation while waiting or querying.</param>
    /// <returns>Response text from the underlying session.</returns>
    public async ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _inner.QueryAsync(command, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>Release the local synchronization primitive without disposing the caller-owned transport.</summary>
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
    /// <summary>Adapt a caller-owned synchronous SCPI session without creating a background thread.</summary>
    /// <param name="session">Underlying synchronous session; calls may block the caller.</param>
    public SynchronousScpiAdapter(IScpiSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Forward a write to the synchronous session after checking cancellation.</summary>
    /// <param name="command">SCPI command to write.</param>
    /// <param name="cancellationToken">Cancellation checked before synchronous dispatch.</param>
    /// <returns>Completed task after the synchronous write.</returns>
    public ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _session.Write(command);
        return ValueTask.CompletedTask;
    }

    /// <summary>Forward a query to the synchronous session after checking cancellation.</summary>
    /// <param name="command">SCPI query text.</param>
    /// <param name="cancellationToken">Cancellation checked before synchronous dispatch.</param>
    /// <returns>Completed task containing the synchronous response.</returns>
    public ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_session.Query(command));
    }
}

/// <summary>Asynchronous DMM class facade with explicit acquisition operation.</summary>
/// <param name="session">Caller-owned asynchronous SCPI session.</param>
public sealed class AsyncDigitalMultimeter(IAsyncScpiSession session)
{
    /// <summary>Configure the multimeter for DC voltage measurement.</summary>
    /// <param name="token">Cancellation forwarded to the session.</param>
    /// <returns>Completion of the configuration command.</returns>
    public ValueTask ConfigureDcVoltageAsync(CancellationToken token = default) =>
        session.WriteAsync("CONF:VOLT:DC", token);

    /// <summary>Read a DC voltage measurement from the multimeter.</summary>
    /// <param name="token">Cancellation forwarded to the session.</param>
    /// <returns>Measured DC voltage in volts.</returns>
    public async ValueTask<double> MeasureDcVoltageAsync(CancellationToken token = default)
    {
        var response = await session.QueryAsync("MEAS:VOLT:DC?", token).ConfigureAwait(false);
        return double.Parse(response, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

/// <summary>Asynchronous function-generator facade for the current supported sine profile.</summary>
/// <param name="session">Caller-owned asynchronous SCPI session.</param>
public sealed class AsyncFunctionGenerator(IAsyncScpiSession session)
{
    /// <summary>Configure the sine waveform on the selected output channel.</summary>
    /// <param name="channel">One-based output channel, 1 or 2.</param>
    /// <param name="frequencyHz">Positive finite frequency in hertz.</param>
    /// <param name="amplitudeVpp">Positive finite peak-to-peak voltage.</param>
    /// <param name="token">Cancellation forwarded to each SCPI write.</param>
    /// <returns>Completion of all three configuration commands.</returns>
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

    /// <summary>Enable or disable the selected generator output.</summary>
    /// <param name="channel">One-based output channel, 1 or 2.</param>
    /// <param name="enabled">True to enable output.</param>
    /// <param name="token">Cancellation forwarded to the session.</param>
    /// <returns>Completion of the output command.</returns>
    public ValueTask SetOutputAsync(int channel, bool enabled, CancellationToken token = default)
    {
        if (channel is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        return session.WriteAsync($"OUTP{channel} {(enabled ? "ON" : "OFF")}", token);
    }
}
