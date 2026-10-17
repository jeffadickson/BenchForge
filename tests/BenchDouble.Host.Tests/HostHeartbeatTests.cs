using BenchDouble.Host.Server.Heartbeat;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Owner heartbeat uses monotonic time and canonical lease fencing independently of driver I/O.</summary>
public sealed partial class HostShutdownTests
{
    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public DateTimeOffset WallClock = DateTimeOffset.UtcNow;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public override DateTimeOffset GetUtcNow() => WallClock;
        public void Advance(TimeSpan amount) => Interlocked.Add(ref _timestamp, amount.Ticks);
    }

    /// <summary>Heartbeat policy rejects unsafe timing relationships before lease acquisition.</summary>
    [Fact]
    public void Heartbeat_budgets_are_finite_and_ordered()
    {
        HostHeartbeatPolicy.Default.Validate();
        foreach (var policy in new[] {
            new HostHeartbeatPolicy(TimeSpan.Zero,TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(4)),
            new HostHeartbeatPolicy(TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(3),TimeSpan.FromSeconds(6)),
            new HostHeartbeatPolicy(TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(3)),
            new HostHeartbeatPolicy(TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(2),TimeSpan.FromHours(2)) })
            Assert.Throws<ArgumentOutOfRangeException>(policy.Validate);
    }
}
