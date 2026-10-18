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

    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    /// <summary>A transport awaiting cancellation cannot hang the periodic renewal loop.</summary>
    /// <returns>Completion after a bounded degraded outcome.</returns>
    [Fact]
    public async Task HeartbeatBoundsHungTransport()
    {
        using var client = new HttpClient(new WaitingHandler()) { BaseAddress = new Uri("http://localhost/"), Timeout = Timeout.InfiniteTimeSpan };
        await using var heartbeat = new ScopeLeaseHeartbeat(client, "meter", Guid.NewGuid(), TimeSpan.FromMilliseconds(5));
        var notified = new TaskCompletionSource<ScopeHeartbeatStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.StatusChanged += status => notified.TrySetResult(status);
        heartbeat.Start();
        Assert.Equal(ScopeHeartbeatStatus.Degraded, await notified.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Role matching rejects trailing newlines and nulls before route creation.</summary>
    [Fact]
    public void RoutingRejectsControlCharacters()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var directory = InstrumentHostDirectory.Load(NullLogger.Instance, baseDirectory: folder);
            Assert.Throws<InvalidDataException>(() => directory.Resolve("meter\n"));
            Assert.Throws<InvalidDataException>(() => directory.Resolve(null!));
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    /// <summary>Short renewal scheduling does not impose an unrealistic transport deadline.</summary>
    /// <returns>Completion after a delayed successful heartbeat.</returns>
    [Fact]
    public async Task ShortHeartbeatPeriodAllowsTransportLatency()
    {
        using var client = new HttpClient(new DelayedHandler()) { BaseAddress = new Uri("http://localhost/") };
        await using var heartbeat = new ScopeLeaseHeartbeat(client, "meter", Guid.NewGuid(), TimeSpan.FromMilliseconds(5));
        var notified = new TaskCompletionSource<ScopeHeartbeatStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.StatusChanged += status => notified.TrySetResult(status);
        heartbeat.Start();
        Assert.Equal(ScopeHeartbeatStatus.Healthy, await notified.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Oversized numeric versions use the same explicit refusal contract as other invalid files.</summary>
    [Fact]
    public void RoutingRejectsOverflowingSchema()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":999999999999999999999,\"hosts\":{},\"instruments\":{}}");
            Assert.Throws<InvalidDataException>(() => InstrumentHostDirectory.Load(NullLogger.Instance, path));
        }
        finally { File.Delete(path); }
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => throw new Exception("sink");
        public bool IsEnabled(LogLevel level) => throw new Exception("sink");
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) => throw new Exception("sink");
    }

    /// <summary>Logging failures cannot change default routing or replace configuration errors.</summary>
    [Fact]
    public void RoutingSurvivesSinkFailures()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var directory = InstrumentHostDirectory.Load(new ThrowingLogger(), baseDirectory: folder);
            Assert.Equal("meter", directory.Resolve("meter").ResourceId);
            Assert.Throws<InvalidDataException>(() => InstrumentHostDirectory.Load(new ThrowingLogger(), Path.Combine(folder, "missing.json")));
        }
        finally { Directory.Delete(folder, true); }
    }

// CASES
}
