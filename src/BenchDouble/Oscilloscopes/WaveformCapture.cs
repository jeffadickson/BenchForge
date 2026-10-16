using System.Buffers.Binary;
using UnitsNet;

namespace BenchDouble.Oscilloscopes;

/// <summary>Strict IEEE 488.2 definite-length binary block reader for bounded waveform transfers.</summary>
public static class ScpiBinaryBlock
{
    /// <summary>Decode #&lt;digits&gt;&lt;length&gt;&lt;payload&gt; with an optional trailing LF or CRLF. Indefinite blocks are rejected.</summary>
    /// <param name="frame">Complete SCPI binary response, including definite-length header.</param>
    /// <param name="maxPayloadBytes">Maximum accepted binary payload size in bytes.</param>
    /// <returns>Decoded payload bytes without SCPI framing.</returns>
    /// <exception cref="FormatException">The binary block framing is invalid.</exception>
    /// <exception cref="InvalidDataException">The declared payload exceeds the configured limit.</exception>
    /// <exception cref="EndOfStreamException">The declared payload is incomplete.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> frame, int maxPayloadBytes = 64 * 1024 * 1024)
    {
        if (maxPayloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        if (frame.Length < 2 || frame[0] != (byte)'#' || frame[1] < (byte)'1' || frame[1] > (byte)'9')
            throw new FormatException("Expected a definite-length SCPI binary block.");
        int digits = frame[1] - (byte)'0';
        if (frame.Length < 2 + digits) throw new FormatException("Truncated block length.");
        long length = 0;
        for (int i = 2; i < 2 + digits; i++)
        {
            if (frame[i] < (byte)'0' || frame[i] > (byte)'9') throw new FormatException("Nondecimal block length.");
            length = checked(length * 10 + frame[i] - (byte)'0');
        }
        if (length > maxPayloadBytes) throw new InvalidDataException("Waveform exceeds configured size limit.");
        long end = 2L + digits + length;
        if (end > frame.Length) throw new EndOfStreamException("Truncated binary payload.");
        var trailing = frame[(int)end..];
        if (!(trailing.IsEmpty || trailing.SequenceEqual("\n"u8) || trailing.SequenceEqual("\r\n"u8)))
            throw new FormatException("Unexpected bytes after binary block.");
        return frame.Slice(2 + digits, (int)length).ToArray();
    }
}

/// <summary>Oscilloscope preamble for linearly scaled integer ADC samples.</summary>
/// <param name="XIncrementSeconds">Duration between samples, in seconds.</param>
/// <param name="XOriginSeconds">Trigger-relative origin of the waveform time axis, in seconds.</param>
/// <param name="XReference">Sample index corresponding to the origin.</param>
/// <param name="YIncrementVolts">Voltage per ADC code, in volts.</param>
/// <param name="YOriginVolts">Voltage offset of ADC reference, in volts.</param>
/// <param name="YReference">ADC reference code.</param>
public sealed record WaveformPreamble(double XIncrementSeconds, double XOriginSeconds, double XReference,
    double YIncrementVolts, double YOriginVolts, double YReference)
{
    /// <summary>Validate that all scale factors are finite and increments are positive.</summary>
    public void Validate()
    {
        if (!double.IsFinite(XIncrementSeconds) || XIncrementSeconds <= 0 ||
            !double.IsFinite(YIncrementVolts) || YIncrementVolts <= 0 ||
            !double.IsFinite(XOriginSeconds) || !double.IsFinite(XReference) ||
            !double.IsFinite(YOriginVolts) || !double.IsFinite(YReference))
            throw new ArgumentOutOfRangeException(nameof(WaveformPreamble), "Invalid waveform preamble.");
    }
}

/// <summary>One immutable, unit-aware acquisition with its original binary sample payload.</summary>
public sealed class WaveformCapture
{
    /// <summary>Acquisition identifier supplied by the caller or host.</summary>
    public Guid AcquisitionId { get; }
    /// <summary>Channel number.</summary>
    public int Channel { get; }
    /// <summary>Scaling metadata retained for exact conversion and persistence.</summary>
    public WaveformPreamble Preamble { get; }
    /// <summary>Original signed 16-bit ADC samples, copied on construction.</summary>
    public ReadOnlyMemory<byte> RawSamples => _samples.ToArray();
    private readonly byte[] _samples;
    private readonly bool _littleEndian;

    /// <summary>Create a capture, rejecting invalid channel, metadata or sample framing.</summary>
    /// <param name="acquisitionId">Nonempty acquisition identity.</param>
    /// <param name="channel">One-based oscilloscope channel.</param>
    /// <param name="preamble">Validated time and voltage scaling metadata.</param>
    /// <param name="rawSamples">Signed 16-bit ADC samples, copied into the capture.</param>
    /// <param name="littleEndian">Whether sample bytes are encoded little-endian.</param>
    public WaveformCapture(Guid acquisitionId, int channel, WaveformPreamble preamble, ReadOnlySpan<byte> rawSamples, bool littleEndian)
    {
        if (acquisitionId == Guid.Empty) throw new ArgumentException("An acquisition ID is required.", nameof(acquisitionId));
        if (channel < 1) throw new ArgumentOutOfRangeException(nameof(channel));
        Preamble = preamble ?? throw new ArgumentNullException(nameof(preamble));
        preamble.Validate();
        if (rawSamples.Length % 2 != 0) throw new FormatException("16-bit samples require an even byte count.");
        AcquisitionId = acquisitionId;
        Channel = channel;
        _samples = rawSamples.ToArray();
        _littleEndian = littleEndian;
    }

    /// <summary>Return the trigger-relative sample timestamp.</summary>
    /// <param name="index">Zero-based sample index.</param>
    /// <returns>Trigger-relative time as a UnitsNet duration.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample index is invalid.</exception>
    public Duration TimeAt(int index)
    {
        ValidateIndex(index);
        return Duration.FromSeconds((index - Preamble.XReference) * Preamble.XIncrementSeconds + Preamble.XOriginSeconds);
    }

    /// <summary>Convert one signed 16-bit ADC sample to physical voltage.</summary>
    /// <param name="index">Zero-based sample index.</param>
    /// <returns>Scaled voltage as a UnitsNet electric potential.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample index is invalid.</exception>
    public ElectricPotential VoltageAt(int index)
    {
        ValidateIndex(index);
        var span = _samples.AsSpan(index * 2, 2);
        short code = _littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(span) : BinaryPrimitives.ReadInt16BigEndian(span);
        return ElectricPotential.FromVolts((code - Preamble.YReference) * Preamble.YIncrementVolts + Preamble.YOriginVolts);
    }

    /// <summary>Number of ADC samples in this capture.</summary>
    public int SampleCount => _samples.Length / 2;

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)SampleCount) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
