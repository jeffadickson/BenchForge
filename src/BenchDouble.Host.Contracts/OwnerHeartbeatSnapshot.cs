namespace BenchDouble.Host.Contracts;

/// <summary>Cached control-owner health; neither process responsiveness nor renewal proves device readiness.</summary>
/// <param name="HostEpoch">Random process epoch; changes after every host restart.</param>
/// <param name="OwnerGeneration">Monotonically allocated generation within this process.</param>
/// <param name="PeerEpoch">Client epoch bound on first renewal; empty before any renewal.</param>
/// <param name="Sequence">Last accepted strictly increasing client sequence.</param>
/// <param name="State">Healthy, Suspect or Expired; expiry never reverses for this lease.</param>
/// <param name="AgeMilliseconds">Age from the last coherent monotonic observation, in milliseconds; retained expiry snapshots keep the last observed age.</param>
/// <param name="IntervalMilliseconds">Recommended client renewal interval in milliseconds.</param>
/// <param name="ExpireMilliseconds">Expiry budget in milliseconds.</param>
/// <param name="LossPolicy">FenceAndRetain; no automatic hardware action or ownership reassignment.</param>
public sealed record OwnerHeartbeatSnapshot(Guid HostEpoch, long OwnerGeneration, Guid PeerEpoch, long Sequence,
    string State, double AgeMilliseconds, double IntervalMilliseconds, double ExpireMilliseconds, string LossPolicy);
