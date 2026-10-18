using System.Net;
using System.Net.Http.Json;
using BenchDouble.Host.Client;
using BenchDouble.Host.Contracts;
using BenchDouble.Host.Server.Setup;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Application concurrency and diagnostic-isolation checks for the 300-iteration batch.</summary>
public sealed class ApplicationBatch300Tests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => reply(request, token);
    }
    private sealed class ThrowingLogger : ILogger<BenchSetupService>
    {
        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <inheritdoc/>
        public bool IsEnabled(LogLevel level) => true;
        /// <inheritdoc/>
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState,Exception?,string> formatter) => throw new InvalidOperationException("sink");
    }

    /// <summary>A transient transport cancellation does not permanently stop periodic renewal.</summary>
    /// <returns>Completion after a degraded notification followed by recovery.</returns>
    [Fact] public async Task ScopeRecoversAfterTransportCancellation()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((_,_) => Interlocked.Increment(ref calls) == 1
            ? Task.FromException<HttpResponseMessage>(new OperationCanceledException())
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)))) { BaseAddress = new Uri("http://localhost/") };
        await using var heartbeat = new ScopeLeaseHeartbeat(client, "meter", Guid.NewGuid(), TimeSpan.FromMilliseconds(10));
        var degraded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.StatusChanged += status => { if (status == ScopeHeartbeatStatus.Degraded) degraded.TrySetResult(); if (status == ScopeHeartbeatStatus.Healthy) healthy.TrySetResult(); };
        heartbeat.Start();
        await degraded.Task.WaitAsync(TimeSpan.FromSeconds(5)); await healthy.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ScopeHeartbeatStatus.Healthy, heartbeat.Status);
    }

    /// <summary>Concurrent renewal callers use unique serialized sequences and publish a coherent acknowledgment.</summary>
    /// <returns>Completion after all renewal requests finish.</returns>
    [Fact] public async Task OwnerConcurrentRenewalsPublishCoherentAcknowledgment()
    {
        var initial = new OwnerHeartbeatSnapshot(Guid.NewGuid(), 1, Guid.Empty, 0, "Healthy", 0, 10, 30000, "FenceAndRetain");
        var sequences = new List<long>();
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            var body = await request.Content!.ReadFromJsonAsync<OwnerHeartbeatRequest>(token);
            sequences.Add(body!.Sequence);
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(initial with { PeerEpoch = body.PeerEpoch, Sequence = body.Sequence }) };
        })) { BaseAddress = new Uri("http://localhost/") };
        await using var heartbeat = new OwnerLeaseHeartbeat(client, "meter", Guid.NewGuid(), initial);
        var statuses = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => heartbeat.RenewAsync())));
        Assert.All(statuses, status => Assert.Equal("Healthy", status));
        Assert.Equal(Enumerable.Range(1, 24).Select(i => (long)i), sequences);
        Assert.Equal(24, heartbeat.LastAcknowledgment!.Sequence);
    }

    /// <summary>A broken setup sink cannot hide a committed save or replace a revision conflict.</summary>
    /// <returns>Completion after discovery, validation, save and rejection.</returns>
    [Fact] public async Task SetupLoggingFailuresPreservePersistenceOutcomes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "batch300-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "setup.json");
        try
        {
            var registry = new HostResourceRegistry();
            var setup = new BenchSetupService(registry, new SimulatedSetupDiscoveryProvider(registry), file, new ThrowingLogger());
            await setup.DiscoverAsync();
            var configuration = new BenchSetup(1, [], [], []);
            Assert.Empty(setup.Validate(configuration));
            var saved = setup.Save(new(configuration, null, true));
            Assert.Equal(saved.Revision, setup.Read()!.Revision);
            Assert.Throws<InvalidOperationException>(() => setup.Save(new(configuration, null, true)));
            Assert.Equal(saved.Revision, setup.Read()!.Revision);
            Assert.Equal(4, setup.LoggingFailureCount);
        }
        finally { if (File.Exists(file)) File.Delete(file); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    /// <summary>A local scope never silently ignores an Ethernet selector.</summary>
    [Fact] public void LocalDiscoveryRejectsExtraneousHost()
    {
        foreach (var scope in new[] { "GPIB", "USB" })
            Assert.Throws<ArgumentException>(() => new NativeVisaDiscoveryProvider(scope, "localhost"));
    }
}
