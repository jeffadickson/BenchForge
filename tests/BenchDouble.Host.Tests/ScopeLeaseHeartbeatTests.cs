using System.Net;
using BenchDouble.Host.Client;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>HTTP heartbeat status and lifecycle tests using an in-memory handler.</summary>
public sealed class ScopeLeaseHeartbeatTests
{
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, ScopeHeartbeatStatus.Healthy)]
    [InlineData(HttpStatusCode.Forbidden, ScopeHeartbeatStatus.Lost)]
    [InlineData(HttpStatusCode.InternalServerError, ScopeHeartbeatStatus.Degraded)]
    public async Task RenewalReportsHostStatus(HttpStatusCode response, ScopeHeartbeatStatus expected)
    {
        var handler = new Handler(response);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5278/") };
        await using var heartbeat = new ScopeLeaseHeartbeat(client, "SCOPE1", Guid.NewGuid(), TimeSpan.FromMilliseconds(5));
        var observed = new TaskCompletionSource<ScopeHeartbeatStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.StatusChanged += status => observed.TrySetResult(status);
        heartbeat.Start();
        var actual = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expected, actual);
        Assert.True(handler.Calls > 0);
    }
}
