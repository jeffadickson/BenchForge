using System.Buffers;
using System.Globalization;
using System.Text;
using BenchDouble.Async;
using BenchDouble.Simulation;
using UnitsNet;

namespace BenchDouble.Oscilloscopes;

/// <summary>Binary-capable transport; callers must serialize command and response ownership.</summary>
public interface IAsyncBinaryScpiSession : IAsyncScpiSession
{
    /// <summary>Return a readable response stream for a binary query; caller owns disposal.</summary>
    ValueTask<Stream> QueryBinaryAsync(string command, CancellationToken cancellationToken = default);
}

/// <summary>Bounded streaming reader for IEEE 488.2 definite-length responses.</summary>
public static class StreamingScpiBinaryBlock
{
    /// <summary>Read one definite-length block, without buffering an unbounded response.</summary>
    public static async ValueTask<byte[]> ReadAsync(Stream stream, int maxPayloadBytes = 64 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxPayloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        var prefix = new byte[2];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        if (prefix[0] != (byte)'#' || prefix[1] < (byte)'1' || prefix[1] > (byte)'9')
            throw new FormatException("Expected definite-length SCPI block.");
        var digits = new byte[prefix[1] - (byte)'0'];
        await stream.ReadExactlyAsync(digits, cancellationToken).ConfigureAwait(false);
        long count = 0;
        foreach (byte digit in digits)
        {
            if (digit < (byte)'0' || digit > (byte)'9') throw new FormatException("Invalid SCPI block length.");
            count = checked(count * 10 + digit - (byte)'0');
        }
        if (count > maxPayloadBytes) throw new InvalidDataException("Block exceeds size limit.");
        var payload = new byte[(int)count];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload; // Terminator remains in the stream; session must own framing/cleanup.
    }
}

/// <summary>Portable simulation-oriented oscilloscope commands, not a certified vendor driver.</summary>
public sealed class AsyncOscilloscope
{
    private readonly IAsyncBinaryScpiSession _session;
    private readonly int _channelCount;

    /// <summary>Create a scope façade with a positive channel count.</summary>
    /// <param name="session">Caller-owned binary-capable SCPI session; command serialization is external.</param>
    /// <param name="channelCount">Positive number of supported one-based channels.</param>
    public AsyncOscilloscope(IAsyncBinaryScpiSession session, int channelCount = 2)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (channelCount < 1) throw new ArgumentOutOfRangeException(nameof(channelCount));
        _channelCount = channelCount;
    }

    /// <summary>Configure a simulated horizontal sample interval.</summary>
    /// <param name="sampleInterval">Positive, finite UnitsNet duration between ADC samples.</param>
    /// <param name="cancellationToken">Cancellation forwarded to the transport.</param>
    /// <returns>Completion of the timebase command write.</returns>
    public ValueTask ConfigureTimebaseAsync(Duration sampleInterval, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(sampleInterval.Seconds) || sampleInterval.Seconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleInterval));
        return _session.WriteAsync("TIM:SCAL " + sampleInterval.Seconds.ToString("G17", CultureInfo.InvariantCulture), cancellationToken);
    }

    /// <summary>Request one acquisition. Completion must be checked separately.</summary>
    /// <param name="cancellationToken">Cancellation forwarded to the transport.</param>
    /// <returns>Completion of the single-acquisition command write, not proof of capture completion.</returns>
    public ValueTask SingleAsync(CancellationToken cancellationToken = default) =>
        _session.WriteAsync("SING", cancellationToken);

    /// <summary>Acquire a waveform from a selected channel using signed 16-bit binary samples.</summary>
    /// <param name="channel">One-based channel identifier.</param>
    /// <param name="acquisitionId">Nonempty identity assigned to the captured waveform.</param>
    /// <param name="cancellationToken">Cancellation forwarded to all transport operations.</param>
    /// <returns>Captured signed 16-bit samples with UnitsNet scaling metadata.</returns>
    public async ValueTask<WaveformCapture> ReadWaveformAsync(int channel, Guid acquisitionId,
        CancellationToken cancellationToken = default)
    {
        if (channel < 1 || channel > _channelCount) throw new ArgumentOutOfRangeException(nameof(channel));
        cancellationToken.ThrowIfCancellationRequested();
        await _session.WriteAsync("WAV:SOUR CHAN" + channel.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        var preambleText = await _session.QueryAsync("WAV:PRE?", cancellationToken).ConfigureAwait(false);
        var parts = preambleText.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 6) throw new FormatException("Expected six simulation preamble values.");
        var values = parts.Select(x => double.Parse(x, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        var preamble = new WaveformPreamble(values[0], values[1], values[2], values[3], values[4], values[5]);
        preamble.Validate();
        await using var stream = await _session.QueryBinaryAsync("WAV:DATA?", cancellationToken).ConfigureAwait(false);
        var payload = await StreamingScpiBinaryBlock.ReadAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new WaveformCapture(acquisitionId, channel, preamble, payload, littleEndian: true);
    }
}

/// <summary>Deterministic simulation-only oscilloscope session with acquisition state.</summary>
public sealed class SimulatedOscilloscopeSession : IAsyncBinaryScpiSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _samples;
    private readonly int _channels;
    private int _selectedChannel = 1;
    private bool _acquired;
    private double _interval = 1e-6;
    private readonly VirtualSignalBus? _signal;
    private VirtualSignalState? _acquisitionState;

    /// <summary>Create a mock with bounded sample count and channel count.</summary>
    /// <param name="channels">Number of simulated input channels.</param>
    /// <param name="samples">Samples per simulated acquisition, from 1 through 1,000,000.</param>
    /// <param name="signal">Optional shared virtual source sampled at acquisition time.</param>
    public SimulatedOscilloscopeSession(int channels = 2, int samples = 1024, VirtualSignalBus? signal = null)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        if (samples < 1 || samples > 1_000_000) throw new ArgumentOutOfRangeException(nameof(samples));
        _channels = channels;
        _samples = samples;
        _signal = signal;
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (command == "SING") { _acquisitionState = _signal?.Snapshot(); _acquired = true; return; }
            if (command.StartsWith("WAV:SOUR CHAN", StringComparison.Ordinal))
            {
                var number = command["WAV:SOUR CHAN".Length..];
                if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int channel) || channel < 1 || channel > _channels)
                    throw new ArgumentOutOfRangeException(nameof(command));
                _selectedChannel = channel;
                return;
            }
            if (command.StartsWith("TIM:SCAL ", StringComparison.Ordinal))
            {
                if (!double.TryParse(command["TIM:SCAL ".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double interval)
                    || !double.IsFinite(interval) || interval <= 0) throw new ArgumentOutOfRangeException(nameof(command));
                _interval = interval;
                _acquired = false;
                return;
            }
            throw new NotSupportedException("Unsupported simulated oscilloscope command.");
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    public async ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (command != "WAV:PRE?") throw new NotSupportedException("Unsupported query.");
            if (!_acquired) throw new InvalidOperationException("No completed acquisition.");
            return string.Join(",", new[] { _interval, 0d, 0d, 0.001d, 0d, 0d }
                .Select(x => x.ToString("G17", CultureInfo.InvariantCulture)));
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    public async ValueTask<Stream> QueryBinaryAsync(string command, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (command != "WAV:DATA?") throw new NotSupportedException("Unsupported binary query.");
            if (!_acquired) throw new InvalidOperationException("No completed acquisition.");
            byte[] payload = new byte[_samples * 2];
            for (int i = 0; i < _samples; i++)
            {
                double volts = _acquisitionState is { } snapshot
                    ? snapshot.VoltageAt(Duration.FromSeconds(i * _interval)).Volts
                    : Math.Sin(2 * Math.PI * i / 64.0 + (_selectedChannel - 1) * Math.PI / 2);
                short code = (short)Math.Clamp(Math.Round(1000 * volts, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(i * 2, 2), code);
            }
            string header = "#" + payload.Length.ToString(CultureInfo.InvariantCulture).Length.ToString(CultureInfo.InvariantCulture)
                + payload.Length.ToString(CultureInfo.InvariantCulture);
            var bytes = Encoding.ASCII.GetBytes(header).Concat(payload).ToArray();
            return new MemoryStream(bytes, writable: false);
        }
        finally { _gate.Release(); }
    }
}
