using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Host lease expiry, renewal and fencing regression tests.</summary>
public sealed class HostedOscilloscopeLeaseTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan duration) => Now += duration;
    }
    /// <summary>Verify expired lease cannot acquire and new lease gets new generation.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task ExpiredLeaseCannotAcquireAndNewLeaseGetsNewGeneration()
    {
        var clock = new ManualClock();
        var host = new HostedOscilloscopeRegistry(clock, TimeSpan.FromSeconds(10));
        var old = host.Reserve("SCOPE1")!.Value;
        Assert.Equal(1, host.Generation("SCOPE1"));
        clock.Advance(TimeSpan.FromSeconds(11));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await host.AcquireAsync("SCOPE1", old, 1));
        var current = host.Reserve("SCOPE1")!.Value;
        Assert.NotEqual(old, current);
        Assert.Equal(2, host.Generation("SCOPE1"));
        Assert.False(host.Release("SCOPE1", old));
        Assert.False(host.Renew("SCOPE1", old));
        Assert.Equal(128, (await host.AcquireAsync("SCOPE1", current, 1)).SampleCount);
    }
    /// <summary>Verify renewal extends valid lease but not expired lease.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task RenewalExtendsValidLeaseButNotExpiredLease()
    {
        var clock = new ManualClock();
        var host = new HostedOscilloscopeRegistry(clock, TimeSpan.FromSeconds(10));
        var lease = host.Reserve("SCOPE1")!.Value;
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.True(host.Renew("SCOPE1", lease));
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(128, (await host.AcquireAsync("SCOPE1", lease, 1)).SampleCount);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(host.Renew("SCOPE1", lease));
    }
}
