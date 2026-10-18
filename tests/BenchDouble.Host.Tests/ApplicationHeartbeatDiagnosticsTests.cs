using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BenchDouble.Host.Client;
using BenchDouble.Host.Contracts;
using BenchDouble.Host.Server.Setup;
using BenchDouble.Ivi.Official;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Heartbeat lifecycle, bounded setup persistence and IVI logging-isolation regressions.</summary>
public sealed class ApplicationHeartbeatDiagnosticsTests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => reply(request, token);
    }
    private sealed class BrokenLogger<T> : ILogger<T>
    {
        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <inheritdoc/>
        public bool IsEnabled(LogLevel level) => true;
        /// <inheritdoc/>
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState,Exception?,string> formatter) => throw new IOException("diagnostic-sink-secret");
    }
    private sealed class SetupFixture : IDisposable
    {
        internal readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "batch350-" + Guid.NewGuid().ToString("N"));
        internal readonly string FilePath;
        internal readonly BenchSetupService Setup;
        internal SetupFixture()
        {
            FilePath = Path.Combine(DirectoryPath, "setup.json"); var registry = new HostResourceRegistry();
            Setup = new(registry, new SimulatedSetupDiscoveryProvider(registry), FilePath);
        }
        /// <inheritdoc/>
        public void Dispose() { if (File.Exists(FilePath)) File.Delete(FilePath); if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath); }
    }
    private static OwnerHeartbeatSnapshot Snapshot() => new(Guid.NewGuid(),1,Guid.Empty,0,"Healthy",0,10,30000,"FenceAndRetain");
    private static BenchSetup Empty() => new(1, [], [], []);
    private static IviConfigurationCatalog Catalog() => new(new string('a',64), [], [], [], [], ["original"]);

    /// <summary>Gone leases terminate scope renewal after the first host rejection.</summary>
    /// <returns>Completion after terminal status and loop shutdown.</returns>
    [Fact] public async Task ScopeGoneStopsRenewal()
    {
        var calls = 0; using var client = new HttpClient(new Handler((_,_) => { Interlocked.Increment(ref calls); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone)); })) { BaseAddress = new Uri("http://localhost/") };
        var heartbeat = new ScopeLeaseHeartbeat(client,"scope",Guid.NewGuid(),TimeSpan.FromMilliseconds(10));
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.StatusChanged += status => { if (status == ScopeHeartbeatStatus.Lost) lost.TrySetResult(); };
        heartbeat.Start(); await lost.Task.WaitAsync(TimeSpan.FromSeconds(10)); await heartbeat.DisposeAsync(); Assert.Equal(1,calls);
    }

    /// <summary>Terminal owner rejection prevents every later direct renewal from sending another request.</summary>
    /// <returns>Completion after terminal and repeated renewal attempts.</returns>
    [Fact] public async Task OwnerGoneIsTerminal()
    {
        var calls = 0; using var client = new HttpClient(new Handler((_,_) => { ++calls; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone)); })) { BaseAddress = new Uri("http://localhost/") };
        await using var heartbeat = new OwnerLeaseHeartbeat(client,"meter",Guid.NewGuid(),Snapshot());
        Assert.Equal("Lost", await heartbeat.RenewAsync()); Assert.Equal("Lost", await heartbeat.RenewAsync());
        Assert.Equal(1,calls); Assert.Null(heartbeat.LastAcknowledgment);
    }

    /// <summary>A throwing scope observer is counted and cannot suppress a second observer.</summary>
    /// <returns>Completion after independent health observation.</returns>
    [Fact] public async Task ScopeObserverFailureIsCounted()
    {
        using var client = new HttpClient(new Handler((_,_) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone)))) { BaseAddress = new Uri("http://localhost/") };
        await using var heartbeat = new ScopeLeaseHeartbeat(client,"scope",Guid.NewGuid(),TimeSpan.FromMilliseconds(10));
        var notified = new TaskCompletionSource<ScopeHeartbeatStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.StatusChanged += _ => throw new IOException("observer"); heartbeat.StatusChanged += status => notified.TrySetResult(status);
        heartbeat.Start(); Assert.Equal(ScopeHeartbeatStatus.Lost, await notified.Task.WaitAsync(TimeSpan.FromSeconds(10))); Assert.Equal(1,heartbeat.ObserverFailureCount);
    }

    /// <summary>A throwing owner observer leaves terminal owner health intact and increments diagnostics.</summary>
    /// <returns>Completion after notification and terminal status.</returns>
    [Fact] public async Task OwnerObserverFailureIsCounted()
    {
        using var client = new HttpClient(new Handler((_,_) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone)))) { BaseAddress = new Uri("http://localhost/") };
        await using var heartbeat = new OwnerLeaseHeartbeat(client,"meter",Guid.NewGuid(),Snapshot()); var observed = "";
        heartbeat.StatusChanged += _ => throw new IOException("observer"); heartbeat.StatusChanged += status => observed = status;
        Assert.Equal("Lost",await heartbeat.RenewAsync()); Assert.Equal("Lost",observed); Assert.Equal(1,heartbeat.ObserverFailureCount);
    }

    /// <summary>Disposal explicitly reports Stopped whether or not the helper was started.</summary>
    /// <param name="start">Whether to admit a renewal loop before stopping.</param>
    /// <returns>Completion after idempotent shutdown.</returns>
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task ScopeDisposalReportsStopped(bool start)
    {
        using var client = new HttpClient(new Handler((_,_) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)))) { BaseAddress = new Uri("http://localhost/") };
        var heartbeat = new ScopeLeaseHeartbeat(client,"scope",Guid.NewGuid()); if (start) heartbeat.Start();
        await heartbeat.DisposeAsync(); await heartbeat.DisposeAsync(); Assert.Equal(ScopeHeartbeatStatus.Stopped,heartbeat.Status); Assert.Throws<InvalidOperationException>(heartbeat.Start);
    }

    /// <summary>All public setup import boundaries reject oversized JSON before deserialization.</summary>
    /// <param name="boundary">Import boundary under test.</param>
    [Theory] [InlineData("setup")] [InlineData("save")] [InlineData("binding")] [InlineData("operation")]
    public void SetupImportSizeIsBounded(string boundary)
    {
        var text = new string(' ',1048577) + "{}";
        Assert.Throws<ArgumentException>(() =>
        {
            switch (boundary)
            {
                case "setup": BenchSetupService.Parse(text); break;
                case "save": BenchSetupService.ParseSaveRequest(text); break;
                case "binding": BenchSetupService.ParseApplyIviBindingRequest(text); break;
                default: BenchSetupService.ParseIviRequest<OwnerHeartbeatRequest>(text); break;
            }
        });
    }

    /// <summary>Deeply nested documents are rejected at a bounded parser depth.</summary>
    /// <param name="depth">Nesting level beyond the supported boundary.</param>
    [Theory] [InlineData(33)] [InlineData(64)] [InlineData(128)]
    public void SetupImportDepthIsBounded(int depth) => Assert.ThrowsAny<JsonException>(() => BenchSetupService.Parse(new string('[',depth) + "0" + new string(']',depth)));

    /// <summary>Oversized saved data is rejected without deletion and a repaired file can be read normally.</summary>
    [Fact] public void OversizedSavedFileCanBeRepaired()
    {
        using var fixture = new SetupFixture(); var saved = fixture.Setup.Save(new(Empty(),null,true)); var original = File.ReadAllBytes(fixture.FilePath);
        File.WriteAllBytes(fixture.FilePath,new byte[4*1048576+1]); Assert.Throws<InvalidDataException>(() => fixture.Setup.Read());
        Assert.Equal(4*1048576+1,new FileInfo(fixture.FilePath).Length);
        File.WriteAllBytes(fixture.FilePath,original); Assert.Equal(saved.Revision,fixture.Setup.Read()!.Revision);
    }

    /// <summary>An oversized valid draft cannot replace a previously saved revision.</summary>
    [Fact] public void OversizedSavePreservesPreviousRevision()
    {
        using var fixture = new SetupFixture(); var saved = fixture.Setup.Save(new(Empty(),null,true)); var original = File.ReadAllBytes(fixture.FilePath);
        var instruments = Enumerable.Range(0,1200).Select(i => new SetupInstrument("meter"+i,new string('n',512),"lan",
            [new("lan","visa","Ethernet",("host"+i).PadRight(512,'x'))])).ToArray();
        Assert.Throws<ArgumentException>(() => fixture.Setup.Save(new(new BenchSetup(1,[],instruments,[]),saved.Revision,true)));
        Assert.Equal(original,File.ReadAllBytes(fixture.FilePath)); Assert.Equal(saved.Revision,fixture.Setup.Read()!.Revision);
    }

    /// <summary>Inspection succeeds with a broken sink and returns detached catalog metadata.</summary>
    [Fact] public void IviInspectionSurvivesSinkFailures()
    {
        var original = Catalog(); var service = new IviSetupCatalogService(new BrokenLogger<IviSetupCatalogService>(),(_,_) => original);
        var result = service.Inspect("not logged"); Assert.Equal(original.Revision,result.Revision); Assert.Equal(2,service.LoggingFailureCount);
        result.Diagnostics[0] = "mutated"; Assert.Equal("original",original.Diagnostics[0]);
    }

    /// <summary>A failed provider keeps its original exception even when both diagnostic events fail.</summary>
    [Fact] public void IviReaderFailurePreservesOriginalException()
    {
        var original = new IOException("provider-secret"); var service = new IviSetupCatalogService(new BrokenLogger<IviSetupCatalogService>(),(_,_) => throw original);
        Assert.Same(original,Assert.Throws<IOException>(() => service.Inspect("private XML"))); Assert.Equal(2,service.LoggingFailureCount);
    }

    /// <summary>Draft rejection cannot be replaced by an exception from its logging sink.</summary>
    [Fact] public void IviDraftRejectionSurvivesSinkFailure()
    {
        var service = new IviSetupCatalogService(new BrokenLogger<IviSetupCatalogService>(),(_,_) => Catalog()); var catalog = service.Inspect("private XML");
        var error = Assert.Throws<InvalidOperationException>(() => service.Apply(new(Empty(),catalog.Revision,"missing","meter",null,true)));
        Assert.DoesNotContain("diagnostic-sink-secret",error.Message); Assert.Equal(3,service.LoggingFailureCount);
    }

    /// <summary>Stopping an admitted renewal cancels transport, never releases ownership and retains caller HTTP ownership.</summary>
    /// <returns>Completion after cancellation, shutdown and a caller-owned probe request.</returns>
    [Fact] public async Task OwnerShutdownCancelsOnlyRenewal()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var methods = new List<HttpMethod>();
        using var client = new HttpClient(new Handler(async (request,token) =>
        {
            methods.Add(request.Method); if (request.RequestUri!.AbsolutePath == "/probe") return new(HttpStatusCode.NoContent);
            started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan,token); return new(HttpStatusCode.NoContent);
        })) { BaseAddress = new Uri("http://localhost/") };
        var heartbeat = new OwnerLeaseHeartbeat(client,"meter",Guid.NewGuid(),Snapshot()); var renewal = heartbeat.RenewAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); var stopping = heartbeat.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await renewal); await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Stopped",heartbeat.Status); using var probe = await client.GetAsync("probe"); Assert.True(probe.IsSuccessStatusCode);
        Assert.Equal(new[] { HttpMethod.Post,HttpMethod.Get },methods);
    }
}
