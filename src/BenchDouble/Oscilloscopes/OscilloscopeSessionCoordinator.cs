using System.Runtime.CompilerServices;
using BenchDouble.Async;

namespace BenchDouble.Oscilloscopes;

/// <summary>
/// Serializes a complete multi-command instrument operation, not merely individual SCPI calls.
/// All users of one physical session must share one coordinator. A local lock is not a
/// distributed host lease or a substitute for hardware fencing.
/// </summary>
public sealed class OscilloscopeSessionCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IAsyncBinaryScpiSession _transport;
    private bool _poisoned;

    /// <summary>Construct a coordinator around the sole underlying transport.</summary>
    /// <param name="transport">Caller-owned transport; all users must share this coordinator.</param>
    public OscilloscopeSessionCoordinator(IAsyncBinaryScpiSession transport) =>
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>
    /// Execute one exclusive command transaction. After an exception following dispatch,
    /// block subsequent operations until an operator-approved transport recovery.
    /// Cancellation while waiting never dispatches commands.
    /// </summary>
    /// <typeparam name="T">Result of the exclusive instrument transaction.</typeparam>
    /// <param name="operation">Entire multi-command transaction, including response reads.</param>
    /// <param name="cancellationToken">Cancellation while waiting or executing; failure after dispatch requires recovery.</param>
    /// <returns>Result of the serialized instrument transaction.</returns>
    public async ValueTask<T> ExecuteAsync<T>(
        Func<IAsyncBinaryScpiSession, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_poisoned) throw new InvalidOperationException("Instrument session requires recovery.");
            // Any error after entering an operation leaves command/response framing uncertain.
            try { return await operation(_transport, cancellationToken).ConfigureAwait(false); }
            catch { _poisoned = true; throw; }
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Mark a session recovered only after a trusted caller has closed/reopened and
    /// verified the underlying transport. This method performs no recovery itself.
    /// </summary>
    /// <param name="cancellationToken">Cancellation while waiting for exclusive coordinator access.</param>
    /// <returns>Completion after clearing the recovery-required state.</returns>
    public async ValueTask ConfirmExternalRecoveryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _poisoned = false; }
        finally { _gate.Release(); }
    }

    /// <summary>Whether a failed transaction has blocked subsequent use.</summary>
    /// <param name="cancellationToken">Cancellation while waiting for exclusive coordinator access.</param>
    /// <returns>True when a prior transaction failed and the session is blocked.</returns>
    public async ValueTask<bool> RequiresRecoveryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _poisoned; }
        finally { _gate.Release(); }
    }
}

/// <summary>
/// An oscilloscope facade whose waveform-selection, preamble and data transfer
/// run under one coordinator lease. Never expose the raw transport to other clients.
/// </summary>
public sealed class CoordinatedOscilloscope
{
    private readonly OscilloscopeSessionCoordinator _coordinator;
    private readonly int _channelCount;

    /// <summary>Create a coordinator-backed facade for a given channel count.</summary>
    /// <param name="coordinator">Exclusive transaction coordinator shared by all session users.</param>
    /// <param name="channelCount">Positive count of one-based oscilloscope channels.</param>
    public CoordinatedOscilloscope(OscilloscopeSessionCoordinator coordinator, int channelCount = 2)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        if (channelCount < 1) throw new ArgumentOutOfRangeException(nameof(channelCount));
        _channelCount = channelCount;
    }

    /// <summary>Acquire a single waveform under an exclusive transaction.</summary>
    /// <param name="channel">One-based acquisition channel.</param>
    /// <param name="acquisitionId">Nonempty identifier associated with the capture.</param>
    /// <param name="cancellationToken">Cancellation while waiting or acquiring.</param>
    /// <returns>Unit-aware waveform captured within one exclusive transaction.</returns>
    public ValueTask<WaveformCapture> AcquireAsync(int channel, Guid acquisitionId,
        CancellationToken cancellationToken = default)
    {
        if (channel < 1 || channel > _channelCount) throw new ArgumentOutOfRangeException(nameof(channel));
        if (acquisitionId == Guid.Empty) throw new ArgumentException("An acquisition ID is required.", nameof(acquisitionId));
        return _coordinator.ExecuteAsync(async (session, token) =>
        {
            await session.WriteAsync("SING", token).ConfigureAwait(false);
            await session.WriteAsync("WAV:SOUR CHAN" + channel.ToString(System.Globalization.CultureInfo.InvariantCulture), token).ConfigureAwait(false);
            var preambleText = await session.QueryAsync("WAV:PRE?", token).ConfigureAwait(false);
            var parts = preambleText.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 6) throw new FormatException("Expected six simulation preamble fields.");
            var numbers = parts.Select(x => double.Parse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var preamble = new WaveformPreamble(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);
            preamble.Validate();
            await using var stream = await session.QueryBinaryAsync("WAV:DATA?", token).ConfigureAwait(false);
            var payload = await StreamingScpiBinaryBlock.ReadAsync(stream, cancellationToken: token).ConfigureAwait(false);
            return new WaveformCapture(acquisitionId, channel, preamble, payload, littleEndian: true);
        }, cancellationToken);
    }
}
