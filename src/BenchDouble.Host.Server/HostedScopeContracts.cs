using BenchDouble.Oscilloscopes;

namespace BenchDouble;

/// <summary>Request to acquire one simulated scope waveform under an exclusive host lease.</summary>
public sealed record HostedScopeAcquisition(Guid LeaseId, int Channel);

/// <summary>Bounded scope response; this prototype returns only a preview, not full binary data.</summary>
public sealed record HostedScopeResult(Guid AcquisitionId, int Channel, int SampleCount,
    WaveformPreamble Preamble, double[] PreviewVolts);
