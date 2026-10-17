using Xunit;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using BenchDouble.Host.Client;
using BenchDouble.Host.Server.Setup;

namespace BenchDouble.Host.Tests;

/// <summary>Local lifecycle, routing and discovery regression checks.</summary>
public sealed class LocalHardeningTests
{
    /// <summary>Invalid route components and timer budgets fail before starting a loop.</summary>
    [Fact]
    public void HeartbeatRejectsInvalidConfiguration()
    {
        using var client = new HttpClient();
        foreach (var id in new[] { "meter/1", "meter\\1", "meter\n", "meter?token=x", new string('x', 81) })
            Assert.Throws<ArgumentException>(() => new ScopeLeaseHeartbeat(client, id, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScopeLeaseHeartbeat(client, "meter", Guid.NewGuid(), TimeSpan.MaxValue));
    }

    /// <summary>Concurrent and repeated stop requests share completion and prevent restart.</summary>
    /// <returns>Completion after all stop requests.</returns>
    [Fact]
    public async Task HeartbeatDisposalIsShared()
    {
        using var client = new HttpClient();
        var heartbeat = new ScopeLeaseHeartbeat(client, "meter", Guid.NewGuid());
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => heartbeat.DisposeAsync().AsTask()));
        await heartbeat.DisposeAsync();
        Assert.Throws<InvalidOperationException>(heartbeat.Start);
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    /// <summary>A throwing observer cannot suppress other observers or renewal.</summary>
    /// <returns>Completion after a healthy notification.</returns>
    [Fact]
    public async Task HeartbeatObserversAreIndependent()
    {
        using var client = new HttpClient(new SuccessHandler()) { BaseAddress = new Uri("http://localhost/") };
        await using var heartbeat = new ScopeLeaseHeartbeat(client, "meter", Guid.NewGuid(), TimeSpan.FromMilliseconds(5));
        var notified = new TaskCompletionSource<ScopeHeartbeatStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.StatusChanged += _ => throw new InvalidOperationException("observer");
        heartbeat.StatusChanged += status => notified.TrySetResult(status);
        heartbeat.Start();
        Assert.Equal(ScopeHeartbeatStatus.Healthy, await notified.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

// CASES
}
