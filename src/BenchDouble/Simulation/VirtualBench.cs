using System.Buffers.Binary;
using BenchDouble.Oscilloscopes;
using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>Shared hardware-free source, meter and digitizer model.</summary>
/// <remarks>All consumers observe one immutable source snapshot per acquisition.</remarks>
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
        if (channel < 1) throw new ArgumentOutOfRangeException(nameof(channel));
        if (sampleCount < 1 || sampleCount > 1_000_000) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (!double.IsFinite(interval.Seconds) || interval.Seconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval));
        if (!double.IsFinite(voltsPerCode) || voltsPerCode <= 0)
            throw new ArgumentOutOfRangeException(nameof(voltsPerCode));
        var snapshot = _signal.Snapshot();
        var bytes = new byte[checked(sampleCount * 2)];
        for (int i = 0; i < sampleCount; i++)
        {
            double volts = snapshot.VoltageAt(Duration.FromSeconds(i * interval.Seconds)).Volts;
            double code = Math.Round(volts / voltsPerCode, MidpointRounding.AwayFromZero);
            short clipped = (short)Math.Clamp(code, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), clipped);
        }
        return new WaveformCapture(acquisitionId ?? Guid.NewGuid(), channel,
            new WaveformPreamble(interval.Seconds, 0, 0, voltsPerCode, 0, 0),
            bytes, littleEndian: true);
    }
}
