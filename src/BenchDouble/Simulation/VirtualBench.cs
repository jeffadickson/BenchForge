using System.Buffers.Binary;
using BenchDouble.Oscilloscopes;
using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>Shared hardware-free source, meter and digitizer model.</summary>
/// <remarks>All consumers observe one immutable source snapshot per acquisition.</remarks>
/// <param name="signal">Shared, caller-owned virtual signal bus.</param>
public sealed class VirtualBench(VirtualSignalBus signal)
{
    private readonly VirtualSignalBus _signal = signal ?? throw new ArgumentNullException(nameof(signal));

    /// <summary>Read DC and true RMS voltage from the current simulated source state.</summary>
    /// <param name="samples">Samples over one source period.</param>
    /// <returns>DC and RMS voltage quantities.</returns>
    public (ElectricPotential Dc, ElectricPotential Rms) MeasureVoltage(int samples = 1024) =>
        _signal.Snapshot().MeasureVoltage(samples);

    /// <summary>Capture a signed 16-bit waveform using a fixed volts-per-code scale.</summary>
    /// <param name="channel">One-based channel identifier.</param>
    /// <param name="sampleCount">Number of samples to capture.</param>
    /// <param name="interval">Sample interval.</param>
    /// <param name="voltsPerCode">ADC scale; values exceeding signed 16-bit range saturate.</param>
    /// <param name="acquisitionId">Optional externally assigned acquisition identity.</param>
    /// <returns>A self-contained waveform with UnitsNet scaling metadata.</returns>
    public WaveformCapture Capture(int channel, int sampleCount, Duration interval,
        double voltsPerCode = 0.001, Guid? acquisitionId = null)
    {
        ValidateCapture(channel, sampleCount, interval, voltsPerCode);
        return CaptureSnapshot(_signal.Snapshot(), channel, sampleCount, interval,
            voltsPerCode, acquisitionId ?? Guid.NewGuid(), Duration.FromSeconds(0));
    }

    /// <summary>Validate a waveform request before touching mutable source state.</summary>
    /// <param name="channel">One-based channel.</param>
    /// <param name="sampleCount">Number of samples, 1 to 1,000,000.</param>
    /// <param name="interval">Positive sample interval.</param>
    /// <param name="voltsPerCode">Positive ADC volts per code.</param>
    public static void ValidateCapture(int channel, int sampleCount, Duration interval, double voltsPerCode)
    {
        if (channel < 1) throw new ArgumentOutOfRangeException(nameof(channel));
        if (sampleCount < 1 || sampleCount > 1_000_000) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (!double.IsFinite(interval.Seconds) || interval.Seconds <= 0) throw new ArgumentOutOfRangeException(nameof(interval));
        if (!double.IsFinite(voltsPerCode) || voltsPerCode <= 0) throw new ArgumentOutOfRangeException(nameof(voltsPerCode));
    }

    /// <summary>Digitize an immutable signal snapshot with a shared acquisition identity.</summary>
    /// <param name="snapshot">Immutable source snapshot.</param>
    /// <param name="channel">One-based channel.</param>
    /// <param name="sampleCount">Number of signed 16-bit samples.</param>
    /// <param name="interval">Positive sample interval.</param>
    /// <param name="voltsPerCode">Positive ADC volts per code.</param>
    /// <param name="acquisitionId">Nonempty acquisition ID.</param>
    /// <param name="start">Virtual time origin for the first sample.</param>
    /// <returns>Independent waveform capture with fixed scaling.</returns>
    public static WaveformCapture CaptureSnapshot(VirtualSignalState snapshot, int channel,
        int sampleCount, Duration interval, double voltsPerCode, Guid acquisitionId, Duration start)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateCapture(channel, sampleCount, interval, voltsPerCode);
        if (acquisitionId == Guid.Empty) throw new ArgumentException("Empty acquisition ID.", nameof(acquisitionId));
        if (!double.IsFinite(start.Seconds)) throw new ArgumentOutOfRangeException(nameof(start));
        var bytes = new byte[checked(sampleCount * 2)];
        for (int index = 0; index < sampleCount; index++)
        {
            double volts = snapshot.VoltageAt(Duration.FromSeconds(start.Seconds + index * interval.Seconds)).Volts;
            double code = Math.Round(volts / voltsPerCode, MidpointRounding.AwayFromZero);
            short clipped = (short)Math.Clamp(code, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2, 2), clipped);
        }
        return new WaveformCapture(acquisitionId, channel,
            new WaveformPreamble(interval.Seconds, start.Seconds, 0, voltsPerCode, 0, 0),
            bytes, littleEndian: true);
    }
}
