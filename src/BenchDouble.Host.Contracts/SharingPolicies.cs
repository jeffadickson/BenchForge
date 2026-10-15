namespace BenchDouble.Host.Contracts;

/// <summary>
/// Supported host-level access arrangements. This is metadata only in the
/// current prototype; future services must enforce the policy server-side.
/// </summary>
public enum InstrumentAccessPolicy
{
    ExclusiveControl,
    SharedReadOnly,
    ExclusiveControlWithObservers,
    HostSampledPublishSubscribe
}

/// <summary>
/// Stable sample identity allows multiple clients to observe the same host
/// measurement instead of independently disturbing an instrument.
/// </summary>
public sealed record HostedMeasurement(
    string ResourceId,
    long Sequence,
    DateTimeOffset Timestamp,
    double Value,
    string Unit,
    string Quality);

public sealed record InstrumentSharingCapabilities(
    string ResourceId,
    InstrumentAccessPolicy AccessPolicy,
    TimeSpan? MaximumSampleAge = null,
    bool SupportsSubscriptions = false);
