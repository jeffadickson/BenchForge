using BenchDouble.Oscilloscopes;

namespace BenchDouble;

/// <summary>Request to acquire one simulated scope waveform under an exclusive host lease.</summary>
/// <param name="LeaseId">Exclusive host lease identifier.</param>
/// <param name="Channel">One-based oscilloscope channel.</param>
public sealed record HostedScopeAcquisition(Guid LeaseId, int Channel);

/// <summary>Bounded scope response; this prototype returns only a preview, not full binary data.</summary>
/// <param name="AcquisitionId">Unique waveform acquisition identifier.</param>
/// <param name="Channel">One-based channel identifier.</param>
/// <param name="SampleCount">Total sample count in the acquired waveform.</param>
/// <param name="Preamble">Time and voltage scaling metadata.</param>
/// <param name="PreviewVolts">Bounded voltage preview in volts, not the full binary waveform.</param>
public sealed record HostedScopeResult(Guid AcquisitionId, int Channel, int SampleCount,
    WaveformPreamble Preamble, double[] PreviewVolts);
