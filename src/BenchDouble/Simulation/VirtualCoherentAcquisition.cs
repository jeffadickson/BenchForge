using BenchDouble.Oscilloscopes;
using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>One immutable virtual acquisition shared by source, meters, and scope.</summary>
/// <param name="AcquisitionId">Correlation ID also stored on the waveform.</param>
/// <param name="Measurement">Source voltage, current, revision and virtual time.</param>
/// <param name="SourceState">Immutable physical state used to generate the waveform.</param>
/// <param name="Waveform">Independent signed 16-bit waveform.</param>
public sealed record VirtualCoherentAcquisition(Guid AcquisitionId, VirtualSmuMeasurement Measurement,
    VirtualSignalState SourceState, WaveformCapture Waveform)
{
    /// <summary>Voltage-meter projection of the acquired source state.</summary>
    public ElectricPotential Voltmeter => Measurement.Voltage;
    /// <summary>Current-meter projection of the acquired source state.</summary>
    public ElectricCurrent Ammeter => Measurement.Current;
    /// <summary>Source revision at the acquisition boundary.</summary>
    public long SourceRevision => Measurement.Revision;
}

/// <summary>Run-scoped acquisition facade composed through constructor injection.</summary>
/// <param name="source">Shared source-measure controller.</param>
public sealed class VirtualCoherentAcquisitionService(VirtualSourceMeasureUnit source)
{
    private readonly VirtualSourceMeasureUnit _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Capture simultaneous virtual source, meter and waveform values.</summary>
    /// <param name="channel">One-based oscilloscope channel.</param>
    /// <param name="sampleCount">Requested waveform sample count.</param>
    /// <param name="interval">Positive waveform sample interval.</param>
    /// <param name="voltsPerCode">Positive ADC volts per code.</param>
    /// <param name="acquisitionId">Optional nonempty correlation ID.</param>
    /// <returns>Immutable acquisition with one source revision and timestamp.</returns>
    public VirtualCoherentAcquisition Acquire(int channel, int sampleCount, Duration interval,
        double voltsPerCode = 0.001, Guid? acquisitionId = null) =>
        _source.AcquireCoherently(channel, sampleCount, interval, voltsPerCode, acquisitionId);
}
