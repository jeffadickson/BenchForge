using System.Net.Sockets;
using System.Text;
using BenchForge.Async;

namespace BenchForge.Transports;

/// <summary>
/// Configuration for a pure-C# IEEE 488.2 / LXI raw-socket SCPI-over-TCP session (typically port 5025).
/// Requires no vendor VISA runtime on Linux, macOS, or Windows.
/// </summary>
/// <param name="Host">Instrument hostname or IP address.</param>
/// <param name="Port">TCP port number (1..65535; standard SCPI raw socket port is 5025).</param>
/// <param name="ConnectTimeout">Maximum duration allowed to establish the TCP connection.</param>
/// <param name="IoTimeout">Maximum duration allowed for any single write or query read.</param>
/// <param name="LineTerminator">Line terminator appended to outbound SCPI commands (default LF).</param>
/// <param name="MaxResponseBytes">Hard upper bound on bytes read for one query response to prevent runaway memory growth.</param>
public sealed record ScpiTcpOptions(
    string Host,
    int Port = 5025,
    TimeSpan? ConnectTimeout = null,
    TimeSpan? IoTimeout = null,
    string LineTerminator = "\n",
    int MaxResponseBytes = 1024 * 1024)
{
    /// <summary>Validate all connection and framing bounds before opening a socket.</summary>
    /// <exception cref="ArgumentException">Host or line terminator is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Port, timeout, or response limit is out of bounds.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new ArgumentException("Host must be non-empty.", nameof(Host));
        if (Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), "TCP port must be in 1..65535.");
        if (ConnectTimeout is { } ct && ct <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), "ConnectTimeout must be positive.");
        if (IoTimeout is { } io && io <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(IoTimeout), "IoTimeout must be positive.");
        if (LineTerminator is not ("\n" or "\r\n" or "\r"))
            throw new ArgumentException("LineTerminator must be LF, CRLF, or CR.", nameof(LineTerminator));
        if (MaxResponseBytes is < 64 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxResponseBytes), "MaxResponseBytes must be between 64 bytes and 64 MiB.");
    }
}

/// <summary>
/// Pure-C# SCPI-over-TCP raw socket transport implementing both <see cref="IScpiSession"/> and <see cref="IAsyncScpiSession"/>.
/// Serializes wire access per connection, rejects embedded line-terminator injection, enforces bounded response sizes,
/// and fails closed on disconnect or timeout without silent retries.
/// </summary>
public sealed class ScpiTcpSession : IScpiSession, IAsyncScpiSession, IDisposable, IAsyncDisposable
{
    private readonly ScpiTcpOptions _options;
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _wireGate = new(1, 1);
    private readonly byte[] _readBuffer = new byte[4096];
    private int _readOffset;
    private int _readCount;
    private bool _disposed;
    private bool _faulted;

    private ScpiTcpSession(ScpiTcpOptions options, TcpClient client)
    {
        _options = options;
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
    }

    /// <summary>Remote endpoint description (host:port) for diagnostics.</summary>
    public string Endpoint => $"{_options.Host}:{_options.Port}";

    /// <summary>True when a prior I/O fault, timeout, or cancellation left the wire stream desynchronized.</summary>
    public bool IsFaulted => _faulted;

    /// <summary>Establish a new SCPI-over-TCP connection with bounded connect timeout.</summary>
    /// <param name="options">Validated connection and framing options.</param>
    /// <param name="cancellationToken">Cancellation token observed during connection setup.</param>
    /// <returns>Connected <see cref="ScpiTcpSession"/> owned by the caller.</returns>
    /// <exception cref="TimeoutException">Connection did not complete within <see cref="ScpiTcpOptions.ConnectTimeout"/>.</exception>
    /// <exception cref="SocketException">Remote endpoint refused or failed the TCP connection.</exception>
    public static async Task<ScpiTcpSession> ConnectAsync(ScpiTcpOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var client = new TcpClient();
        var connectTimeout = options.ConnectTimeout ?? TimeSpan.FromSeconds(5);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(connectTimeout);

        try
        {
            await client.ConnectAsync(options.Host, options.Port, cts.Token).ConfigureAwait(false);
            return new ScpiTcpSession(options, client);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Timed out connecting to SCPI endpoint {options.Host}:{options.Port} after {connectTimeout.TotalMilliseconds:F0} ms.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Establish a new SCPI-over-TCP connection synchronously.</summary>
    /// <param name="options">Validated connection and framing options.</param>
    /// <returns>Connected <see cref="ScpiTcpSession"/> owned by the caller.</returns>
    public static ScpiTcpSession Connect(ScpiTcpOptions options) =>
        ConnectAsync(options).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public void Write(string command) =>
        WriteAsync(command).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public string Query(string command) =>
        QueryAsync(command).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public async ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);
        EnsureHealthy();

        await _wireGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureHealthy();
            await WriteFrameAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _wireGate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);
        EnsureHealthy();

        await _wireGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureHealthy();
            await WriteFrameAsync(command, cancellationToken).ConfigureAwait(false);
            return await ReadLineBoundedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _wireGate.Release();
        }
    }

    /// <summary>Close the underlying TCP socket and release local synchronization primitives.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
        _client.Dispose();
        _wireGate.Dispose();
    }

    /// <summary>Asynchronously dispose the underlying TCP socket and synchronization primitives.</summary>
    /// <returns>Completed value task after socket shutdown.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask WriteFrameAsync(string command, CancellationToken cancellationToken)
    {
        var payload = Encoding.ASCII.GetBytes(command + _options.LineTerminator);
        var timeout = _options.IoTimeout ?? TimeSpan.FromSeconds(5);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await _stream.WriteAsync(payload, cts.Token).ConfigureAwait(false);
            await _stream.FlushAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _faulted = true;
            throw new TimeoutException($"SCPI write to {Endpoint} timed out after {timeout.TotalMilliseconds:F0} ms.");
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    private async ValueTask<string> ReadLineBoundedAsync(CancellationToken cancellationToken)
    {
        var timeout = _options.IoTimeout ?? TimeSpan.FromSeconds(5);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        using var line = new MemoryStream(256);
        try
        {
            while (true)
            {
                if (_readOffset >= _readCount)
                {
                    _readOffset = 0;
                    _readCount = await _stream.ReadAsync(_readBuffer, cts.Token).ConfigureAwait(false);
                    if (_readCount == 0)
                    {
                        _faulted = true;
                        throw new IOException($"SCPI endpoint {Endpoint} closed the connection before terminating the response.");
                    }
                }

                while (_readOffset < _readCount)
                {
                    byte b = _readBuffer[_readOffset++];
                    if (b == (byte)'\n')
                    {
                        var bytes = line.ToArray();
                        int len = bytes.Length;
                        if (len > 0 && bytes[len - 1] == (byte)'\r')
                            len--;
                        return Encoding.ASCII.GetString(bytes, 0, len);
                    }

                    if (line.Length >= _options.MaxResponseBytes)
                    {
                        _faulted = true;
                        throw new InvalidDataException($"SCPI response from {Endpoint} exceeded the {_options.MaxResponseBytes}-byte safety limit.");
                    }

                    line.WriteByte(b);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _faulted = true;
            throw new TimeoutException($"SCPI query read from {Endpoint} timed out after {timeout.TotalMilliseconds:F0} ms.");
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    private void EnsureHealthy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted)
            throw new IOException($"SCPI TCP session to {Endpoint} is faulted after an interrupted I/O operation; reconnect explicitly after safe-state verification.");
    }

    private static void ValidateCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("SCPI command must be non-empty.", nameof(command));
        if (command.Contains('\r') || command.Contains('\n') || command.Contains('\0'))
            throw new ArgumentException("SCPI command must not contain CR, LF, or NUL characters.", nameof(command));
    }
}
