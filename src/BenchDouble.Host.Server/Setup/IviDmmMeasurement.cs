using BenchDouble.Ivi;

namespace BenchDouble.Host.Server.Setup;

/// <summary>One finite reading and its configuration, captured under the same exclusive dispatch gate.</summary>
/// <param name="ResourceId">Canonical instrument identity.</param>
/// <param name="Revision">Saved setup revision pinned at activation.</param>
/// <param name="CorrelationId">Non-capability activation correlation shared with status and logs; distinguishes sequence restarts.</param>
/// <param name="Value">Measured value in the indicated base SI unit.</param>
/// <param name="Function">Successfully configured function used for this reading.</param>
/// <param name="Unit">V, A or ohm, determined from the function.</param>
/// <param name="Sequence">Monotonic successful-reading number within this activation, beginning at one.</param>
/// <param name="CompletedUtc">Host receipt time after synchronous measurement; not hardware sampling time.</param>
/// <param name="RequestedRange">Requested range in the function's base SI unit; null means provider default, not verified autorange.</param>
/// <param name="RequestedResolution">Requested resolution in the function's base SI unit; not a calibration or accuracy claim.</param>
public sealed record IviDmmMeasurement(string ResourceId, string Revision, Guid CorrelationId, double Value,
    IviMeasurementFunction Function, string Unit, long Sequence, DateTimeOffset CompletedUtc,
    double? RequestedRange, double? RequestedResolution);
