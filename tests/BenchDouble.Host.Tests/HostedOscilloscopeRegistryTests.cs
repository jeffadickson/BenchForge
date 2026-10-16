using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Host-owned oscilloscope reservation and acquisition acceptance tests.</summary>
public sealed class HostedOscilloscopeRegistryTests
{
    /// <summary>Verify exclusive lease controls scope acquisition.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task ExclusiveLeaseControlsScopeAcquisition()
    {
        var host = new HostedOscilloscopeRegistry();
        var lease = host.Reserve("SCOPE1");
        Assert.NotNull(lease);
        Assert.Null(host.Reserve("SCOPE1"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await host.AcquireAsync("SCOPE1", Guid.NewGuid(), 1));
        var capture = await host.AcquireAsync("SCOPE1", lease!.Value, 2);
        Assert.Equal(2, capture.Channel);
        Assert.Equal(128, capture.SampleCount);
        Assert.True(host.Release("SCOPE1", lease.Value));
        Assert.False(host.Release("SCOPE1", lease.Value));
        Assert.NotNull(host.Reserve("SCOPE1"));
    }
    /// <summary>Verify concurrent requests on same lease are serialized.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>

    [Fact]
    public async Task ConcurrentRequestsOnSameLeaseAreSerialized()
    {
        var host = new HostedOscilloscopeRegistry();
        var lease = host.Reserve("SCOPE1")!.Value;
        var tasks = Enumerable.Range(0, 24)
            .Select(i => host.AcquireAsync("SCOPE1", lease, i % 2 + 1).AsTask());
        var captures = await Task.WhenAll(tasks);
        for (int i = 0; i < captures.Length; i++)
        {
            Assert.Equal(i % 2 + 1, captures[i].Channel);
            Assert.Equal(i % 2 == 0 ? 0 : 1, captures[i].VoltageAt(0).Volts, 12);
        }
        Assert.True(host.Release("SCOPE1", lease));
    }
    /// <summary>Verify unknown resource cannot be acquired.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>

    [Fact]
    public async Task UnknownResourceCannotBeAcquired()
    {
        var host = new HostedOscilloscopeRegistry();
        Assert.Null(host.Reserve("MISSING"));
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await host.AcquireAsync("MISSING", Guid.NewGuid(), 1));
    }
}
