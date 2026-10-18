using BenchDouble.Host.Client;
using BenchDouble.Host.Contracts;
using System.Net;
using System.Net.Http.Json;
using BenchDouble.Host.Server.Heartbeat;
using BenchDouble.Host.Server.Recovery;
using BenchDouble.Host.Server.Setup;
using BenchDouble.Ivi.Official;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using BenchDouble.Host.Server.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Xunit;
using Microsoft.Extensions.Hosting;

namespace BenchDouble.Host.Tests;

/// <summary>Shutdown admission, bounded waiting and ownership-retention regression tests.</summary>
public sealed partial class HostShutdownTests
{
    /// <summary>Shutdown policy cannot silently select an infinite or excessive resource budget.</summary>
    [Fact]
    public void Policy_is_bounded()
    {
        new HostShutdownPolicy(TimeSpan.FromSeconds(5)).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostShutdownPolicy(TimeSpan.Zero).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostShutdownPolicy(TimeSpan.FromSeconds(61)).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostShutdownPolicy(TimeSpan.FromSeconds(1), 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostShutdownPolicy(TimeSpan.FromSeconds(1), 17).Validate());
    }
    /// <summary>Drain is irreversible, idempotent and prevents new grants without discarding existing ownership.</summary>
    [Fact]
    public void Drain_stops_new_grants()
    {
        var registry = new HostResourceRegistry();
        var grant = registry.Reserve("DMM1")!;
        registry.BeginDrain(); registry.BeginDrain();
        Assert.True(registry.IsDraining);
        Assert.Null(registry.Reserve("FGEN1"));
        Assert.True(registry.Monitor().Instruments.Single(x => x.ResourceId == "DMM1").IsLocked);
    }

    /// <summary>Even an existing simulation lease cannot dispatch new queries after drain begins.</summary>
    [Fact]
    public void Drain_stops_existing_query_dispatch()
    {
        var registry = new HostResourceRegistry(); var grant = registry.Reserve("DMM1")!;
        registry.BeginDrain();
        Assert.Throws<InvalidOperationException>(() => registry.Query("DMM1", grant.LeaseId, "*IDN?"));
        Assert.NotEmpty(registry.List());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "bench-shutdown-" + Guid.NewGuid());
        public readonly FileHostOwnershipJournal? Journal;
        public readonly HostResourceRegistry Registry;
        public readonly HostedIviDmmService Service;
        public readonly BenchSetupService Setup;
        public readonly string Id, Revision;
        public Action? OnClose, OnMeasure, OnOpen, OnPrepare, OnRecover;
        public int CloseCalls, MeasureCalls, OpenCalls, PrepareCalls, RecoveryCalls;
        public Fixture(HostResourceRegistry? registry = null, string id = "METER", bool shutdown = false, ILogger<HostResourceRegistry>? logger = null, HostHeartbeatPolicy? heartbeat = null, TimeProvider? clock = null)
        {
            Directory.CreateDirectory(_directory); Id = id;
            if (registry is null) Journal = new FileHostOwnershipJournal(Path.Combine(_directory, "ownership"));
            Registry = registry ?? new HostResourceRegistry(logger, Journal, heartbeat, clock);
            Setup = new(Registry, new SimulatedSetupDiscoveryProvider(Registry), Path.Combine(_directory, "setup.json"));
            var binding = new SetupDriverBinding(new string('a',64), "meter", "session",
                new("driver", "IviNetSoftwareModule", "Vendor.Driver, Vendor"), "TCPIP0::" + id + "::inst0::INSTR", new(), new());
            var config = new BenchSetup(2, [], [new(id, id, "primary",
                [new("primary", "visa", "VISA", binding.ResourceDescriptor, DriverBinding: binding)], true)], [new("meter", id)]);
            Revision = Setup.Save(new(config, null, true)).Revision;
            Service = new(Registry, Setup, [new(id, [binding], _ =>
            {
                OpenCalls++; OnOpen?.Invoke();
                var official = Substitute.For<global::Ivi.Dmm.IIviDmm>();
                var bridge = new OfficialDmmBridge(official, (_, _, _, _) => { }, _ => { MeasureCalls++; OnMeasure?.Invoke(); return 1.0; }, _ => throw new Exception(), _ => "probe");
                return new(bridge, () => { CloseCalls++; OnClose?.Invoke(); });
            }, _ => { RecoveryCalls++; OnRecover?.Invoke(); }, PrepareForShutdown: shutdown ? _ => { PrepareCalls++; OnPrepare?.Invoke(); } : null)], NullLogger<HostedIviDmmService>.Instance);
        }
        public IviDmmActivation Activate()
        {
            var active = Service.Activate("meter", Revision, true)!;
            Service.Configure(Id, active.Lease.LeaseId, BenchDouble.Ivi.IviMeasurementFunction.DcVoltage);
            return active;
        }
        public void Dispose() { Journal?.Dispose(); Directory.Delete(_directory, true); }
    }

    /// <summary>Drain blocks typed operations and registration while cached status remains readable.</summary>
    [Fact]
    public void Drain_blocks_typed_dispatch_but_preserves_inspection()
    {
        using var f = new Fixture(); var active = f.Activate();
        f.Registry.BeginDrain();
        Assert.Throws<InvalidOperationException>(() => f.Service.Measure(f.Id, active.Lease.LeaseId));
        Assert.Equal(0, f.MeasureCalls);
        Assert.Equal("Ready", f.Service.Status(f.Id, active.Lease.LeaseId).State);
        Assert.Throws<InvalidOperationException>(() => new HostedIviDmmService(f.Registry, f.Setup, [], NullLogger<HostedIviDmmService>.Instance));
    }

    /// <summary>Work queued behind a synchronous call is rejected when drain begins before its dispatch.</summary>
    /// <returns>Completion after both the admitted and queued requests finish.</returns>
    [Fact]
    public async Task Queued_command_does_not_dispatch_after_drain()
    {
        using var f = new Fixture(); var active = f.Activate();
        using var entered = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        f.OnMeasure = () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        var running = Task.Run(() => f.Service.Measure(f.Id, active.Lease.LeaseId));
        Task? queued = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            queued = Task.Run(() => f.Service.Measure(f.Id, active.Lease.LeaseId));
            f.Registry.BeginDrain();
        }
        finally { finish.Set(); await running; }
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await queued!);
        Assert.Equal(1, f.MeasureCalls); Assert.Single(f.Journal!.Read());
    }

    /// <summary>All callers share one shutdown decision and cleanup; empty shutdown also stops admission.</summary>
    /// <returns>Completion after the shared report is available.</returns>
    [Fact]
    public async Task Shutdown_is_shared_and_permanent()
    {
        var registry = new HostResourceRegistry(); registry.Reserve("DMM1");
        var first = registry.ShutdownAsync(new(TimeSpan.FromSeconds(1)));
        var second = registry.ShutdownAsync(new(TimeSpan.FromSeconds(2)));
        Assert.Same(first, second);
        var report = await first;
        Assert.Equal("Closed", Assert.Single(report.Items).Outcome);
        Assert.Same(report, registry.ShutdownReport); Assert.Null(registry.Reserve("DMM1"));
    }

    /// <summary>Startup quarantine survives orderly shutdown without inferred provider recovery.</summary>
    /// <returns>Completion after inspecting the retained startup record.</returns>
    [Fact]
    public async Task Shutdown_preserves_startup_quarantine()
    {
        using var f = new Fixture(); f.Activate();
        var restarted = new HostResourceRegistry(journal: f.Journal);
        var report = await restarted.ShutdownAsync(new(TimeSpan.FromSeconds(1)));
        Assert.Equal("StartupQuarantine", Assert.Single(report.Items).Outcome);
        Assert.Single(f.Journal!.Read()); Assert.Single(restarted.PendingRecovery());
    }

    /// <summary>No generic close substitutes for a qualified physical safe-state procedure.</summary>
    /// <returns>Completion after an unsupported provider is retained.</returns>
    [Fact]
    public async Task Missing_shutdown_provider_retains_ownership()
    {
        using var f = new Fixture(); f.Activate();
        var report = await f.Registry.ShutdownAsync(new(TimeSpan.FromSeconds(1)));
        Assert.Equal("ProviderMissing", Assert.Single(report.Items).Outcome);
        Assert.Equal(0, f.CloseCalls); Assert.Single(f.Journal!.Read());
    }

    /// <summary>Waiting for an in-flight operation is bounded and cannot dispatch cleanup after its deadline.</summary>
    /// <returns>Completion after the decision timeout and the original operation finish.</returns>
    [Fact]
    public async Task Deadline_does_not_release_inflight_ownership()
    {
        using var f = new Fixture(); var active = f.Activate();
        using var entered = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        f.OnMeasure = () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        var running = Task.Run(() => f.Service.Measure(f.Id, active.Lease.LeaseId));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var report = await f.Registry.ShutdownAsync(new(TimeSpan.FromMilliseconds(50))).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("DeadlineExceeded", Assert.Single(report.Items).Outcome);
            Assert.Single(f.Journal!.Read()); Assert.Equal(0, f.CloseCalls);
        }
        finally { finish.Set(); await running; }
        Assert.Equal(0, f.CloseCalls); Assert.Single(f.Journal!.Read());
    }

    /// <summary>Manual close cannot bypass the qualified shutdown path after the host starts draining.</summary>
    [Fact]
    public void Manual_close_cannot_bypass_shutdown_policy()
    {
        using var f = new Fixture(); var active = f.Activate(); f.Registry.BeginDrain();
        Assert.Throws<InvalidOperationException>(() => f.Registry.Release(active.Lease.LeaseId));
        Assert.Equal(0, f.CloseCalls); Assert.Single(f.Journal!.Read());
    }

    /// <summary>Qualified preparation completes before close, and durable evidence remains until both finish.</summary>
    /// <returns>Completion after ordered provider cleanup and durable release.</returns>
    [Fact]
    public async Task Qualified_shutdown_prepares_before_closing()
    {
        using var f = new Fixture(shutdown: true); f.Activate();
        f.OnPrepare = () => { Assert.Single(f.Journal!.Read()); Assert.Equal(0, f.CloseCalls); };
        f.OnClose = () => { Assert.Equal(1, f.PrepareCalls); Assert.Single(f.Journal!.Read()); };
        var report = await f.Registry.ShutdownAsync(new(TimeSpan.FromSeconds(1)));
        Assert.Equal("Closed", Assert.Single(report.Items).Outcome);
        Assert.Equal(1, f.CloseCalls); Assert.Empty(f.Journal!.Read());
    }

    /// <summary>Failed safe-state preparation never proceeds to generic close or clears the journal.</summary>
    /// <returns>Completion after the retained failure is reported.</returns>
    [Fact]
    public async Task Preparation_failure_retains_evidence_and_skips_close()
    {
        using var f = new Fixture(shutdown: true); var active = f.Activate();
        f.OnPrepare = () => throw new IOException("SECRET shutdown details");
        var report = await f.Registry.ShutdownAsync(new(TimeSpan.FromSeconds(1)));
        Assert.Equal("RecoveryRequired", Assert.Single(report.Items).Outcome);
        Assert.Equal(0, f.CloseCalls); Assert.Single(f.Journal!.Read());
        Assert.Equal("Shutdown", f.Service.Status(f.Id, active.Lease.LeaseId).LastFailurePhase);
    }

    /// <summary>A stuck provider cannot prevent another resource from receiving cleanup when a worker is available.</summary>
    /// <returns>Completion after timeout and eventual completion of the blocked callback.</returns>
    [Fact]
    public async Task Independent_cleanup_progresses_while_another_provider_blocks()
    {
        using var f = new Fixture(shutdown: true); using var other = new Fixture(f.Registry, "OTHER", shutdown: true);
        f.Activate(); other.Activate();
        using var entered = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim(); using var closed = new ManualResetEventSlim();
        f.OnPrepare = () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        f.OnClose = () => closed.Set();
        try
        {
            var report = await f.Registry.ShutdownAsync(new(TimeSpan.FromMilliseconds(150), 2)).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(entered.IsSet);
            Assert.Equal("DeadlineExceeded", report.Items.Single(x => x.ResourceId == f.Id).Outcome);
            Assert.Equal("Closed", report.Items.Single(x => x.ResourceId == "OTHER").Outcome);
            Assert.Equal(1, other.PrepareCalls); Assert.Equal(1, other.CloseCalls); Assert.Single(f.Journal!.Read());
        }
        finally { finish.Set(); Assert.True(closed.Wait(TimeSpan.FromSeconds(3))); Assert.True(SpinWait.SpinUntil(() => f.Registry.ShutdownWorkPending == 0, TimeSpan.FromSeconds(3))); }
    }

    /// <summary>The configured worker cap bounds vendor concurrency; queued cleanup does not dispatch after timeout.</summary>
    /// <returns>Completion after the one-worker budget expires and the blocked callback finishes.</returns>
    [Fact]
    public async Task Worker_limit_and_deadline_prevent_late_dispatch()
    {
        using var f = new Fixture(shutdown: true); using var other = new Fixture(f.Registry, "OTHER", shutdown: true);
        f.Activate(); other.Activate();
        using var finish = new ManualResetEventSlim(); using var closed = new ManualResetEventSlim();
        f.OnPrepare = () => { if (!finish.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        f.OnClose = () => closed.Set();
        try
        {
            var report = await f.Registry.ShutdownAsync(new(TimeSpan.FromMilliseconds(100), 1));
            Assert.All(report.Items, x => Assert.Equal("DeadlineExceeded", x.Outcome));
            Assert.Equal(0, other.PrepareCalls); Assert.Equal(0, other.CloseCalls);
        }
        finally { finish.Set(); Assert.True(closed.Wait(TimeSpan.FromSeconds(3))); Assert.True(SpinWait.SpinUntil(() => f.Registry.ShutdownWorkPending == 0, TimeSpan.FromSeconds(3))); }
        Assert.Equal(0, other.PrepareCalls);
    }

    /// <summary>A late successful close may clear evidence but cannot rewrite the already returned deadline report.</summary>
    /// <returns>Completion after the late worker durably releases ownership.</returns>
    [Fact]
    public async Task Late_completion_is_visible_without_rewriting_decision()
    {
        using var f = new Fixture(shutdown: true); f.Activate();
        using var finish = new ManualResetEventSlim();
        // Only the test releases this worker. A timer here races the test's
        // continuation under load and no longer models deliberately late cleanup.
        f.OnClose = () => finish.Wait();
        HostShutdownReport report;
        try
        {
            report = await f.Registry.ShutdownAsync(new(TimeSpan.FromMilliseconds(100)))
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("DeadlineExceeded", Assert.Single(report.Items).Outcome);
            Assert.Equal(1, f.Registry.ShutdownWorkPending); Assert.Single(f.Journal!.Read());
        }
        finally { finish.Set(); Assert.True(SpinWait.SpinUntil(() => f.Registry.ShutdownWorkPending == 0, TimeSpan.FromSeconds(3))); }
        Assert.Empty(f.Journal!.Read());
        Assert.Equal("DeadlineExceeded", Assert.Single(report.Items).Outcome);
        Assert.Null(f.Registry.Reserve(f.Id));
    }

    private sealed class Lifetime : IHostApplicationLifetime, IDisposable
    {
        public readonly CancellationTokenSource Stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => Stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => Stopping.Cancel();
        public void Dispose() => Stopping.Dispose();
    }

    /// <summary>The host stopping signal closes admission before cleanup and ignores unrelated caller cancellation.</summary>
    /// <returns>Completion after qualified cleanup despite a pre-cancelled outer token.</returns>
    [Fact]
    public async Task Hosted_shutdown_uses_independent_budget()
    {
        using var f = new Fixture(shutdown: true); f.Activate();
        using var lifetime = new Lifetime();
        using var service = new CanonicalShutdownService(f.Registry, lifetime, new(TimeSpan.FromSeconds(1)));
        await service.StartAsync(CancellationToken.None); lifetime.StopApplication();
        Assert.True(f.Registry.IsDraining); Assert.Equal(0, f.CloseCalls);
        await service.StopAsync(new CancellationToken(true));
        Assert.Equal(1, f.PrepareCalls); Assert.Equal(1, f.CloseCalls); Assert.Empty(f.Journal!.Read());
    }

    /// <summary>Invalid host settings cannot silently select defaults, infinite waiting or excessive parallel cleanup.</summary>
    [Fact]
    public void Configuration_is_strict_and_validated()
    {
        var defaults = HostShutdownPolicy.FromConfiguration(new ConfigurationBuilder().Build());
        Assert.Equal(TimeSpan.FromSeconds(5), defaults.Timeout); Assert.Equal(4, defaults.MaxConcurrency);
        foreach (var value in new[] { "", "-1", "NaN", "1.5", "60001", "0", "SECRET" })
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["BenchShutdown:TimeoutMilliseconds"] = value }).Build();
            var error = Assert.ThrowsAny<ArgumentException>(() => HostShutdownPolicy.FromConfiguration(config));
            Assert.DoesNotContain("SECRET", error.Message);
        }
        var registry = new HostResourceRegistry();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = registry.ShutdownAsync(new(TimeSpan.Zero)); });
        Assert.False(registry.IsDraining);
    }

    private sealed class Capture : ILogger<HostResourceRegistry>
    {
        public readonly ConcurrentQueue<string> Messages = new();
        public readonly ConcurrentQueue<int> Ids = new();
        public bool Throw;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState,Exception?,string> formatter)
        { if (Throw) throw new IOException("SECRET sink"); Ids.Enqueue(id.Id); Messages.Enqueue(formatter(state,exception)); }
    }

    /// <summary>Shutdown emits correlated outcome categories without lease capabilities or provider exception messages.</summary>
    /// <returns>Completion after the diagnostic sequence is recorded.</returns>
    [Fact]
    public async Task Shutdown_logging_excludes_capabilities_and_payloads()
    {
        var log = new Capture(); using var f = new Fixture(shutdown: true, logger: log); var active = f.Activate();
        f.OnPrepare = () => throw new IOException("SECRET vendor payload");
        await f.Registry.ShutdownAsync(new(TimeSpan.FromSeconds(1)));
        Assert.Contains(7480, log.Ids); Assert.Contains(7481, log.Ids); Assert.Contains(7482, log.Ids); Assert.Contains(7483, log.Ids);
        var text = string.Join("\n", log.Messages);
        Assert.DoesNotContain("SECRET", text); Assert.DoesNotContain(active.Lease.LeaseId.ToString(), text);
        Assert.Contains("RecoveryRequired", text);
    }

    /// <summary>A throwing diagnostic sink cannot change successful cleanup, replay callbacks or strand shutdown callers.</summary>
    /// <returns>Completion of all concurrent shutdown callers despite diagnostic failures.</returns>
    [Fact]
    public async Task Shutdown_isolated_from_sink_failures_and_duplicate_calls()
    {
        var log = new Capture(); using var f = new Fixture(shutdown: true, logger: log); f.Activate(); log.Throw = true;
        var calls = Enumerable.Range(0,32).Select(_ => Task.Run(() => f.Registry.ShutdownAsync(new(TimeSpan.FromSeconds(1))))).ToArray();
        var reports = await Task.WhenAll(calls);
        Assert.All(reports, r => Assert.Same(reports[0], r));
        Assert.Equal("Closed", Assert.Single(reports[0].Items).Outcome);
        Assert.Equal(1, f.PrepareCalls); Assert.Equal(1, f.CloseCalls); Assert.Empty(f.Journal!.Read());
        Assert.True(f.Registry.LoggingFailureCount >= 4);
    }

    /// <summary>Preparation, close and already-faulted sessions retain durable quarantine and reject automatic retries.</summary>
    /// <param name="phase">Provider failure phase.</param>
    /// <returns>Completion after shared shutdown decisions preserve failure evidence.</returns>
    [Theory]
    [InlineData("prepare")]
    [InlineData("close")]
    [InlineData("measure")]
    public async Task Shutdown_failure_matrix_retains_ownership(string phase)
    {
        using var f = new Fixture(shutdown: true); var active = f.Activate();
        if (phase == "prepare") f.OnPrepare = () => throw new IOException();
        if (phase == "close") f.OnClose = () => throw new IOException();
        if (phase == "measure")
        {
            f.OnMeasure = () => throw new IOException();
            Assert.Throws<InvalidOperationException>(() => f.Service.Measure(f.Id, active.Lease.LeaseId));
        }
        var first = await f.Registry.ShutdownAsync(new(TimeSpan.FromSeconds(1)));
        var second = await f.Registry.ShutdownAsync(new(TimeSpan.FromSeconds(1)));
        Assert.Same(first, second); Assert.Equal("RecoveryRequired", Assert.Single(first.Items).Outcome);
        Assert.Single(f.Journal!.Read());
        Assert.Equal("RecoveryRequired", f.Registry.Monitor().Instruments.Single(i => i.ResourceId == f.Id).Status);
        Assert.Equal(phase == "measure" ? 0 : 1, f.PrepareCalls);
        Assert.Equal(phase == "close" ? 1 : 0, f.CloseCalls);
    }

    /// <summary>Shutdown waits behind an admitted open and never overlaps preparation with initialization.</summary>
    /// <returns>Completion after opening and shutdown finish under the same canonical gate.</returns>
    [Fact]
    public async Task Shutdown_does_not_overlap_initialization()
    {
        using var f = new Fixture(shutdown: true);
        using var entered = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        f.OnOpen = () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        var opening = Task.Run(() => f.Service.Activate("meter", f.Revision, true));
        Task<HostShutdownReport>? shutdown = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            shutdown = f.Registry.ShutdownAsync(new(TimeSpan.FromSeconds(2)));
            Assert.Equal(0, f.PrepareCalls); Assert.Equal(0, f.CloseCalls); Assert.Single(f.Journal!.Read());
        }
        finally { finish.Set(); await opening; }
        var report = await shutdown!;
        Assert.Equal("Closed", Assert.Single(report.Items).Outcome);
        Assert.Equal(1, f.PrepareCalls); Assert.Equal(1, f.CloseCalls); Assert.Empty(f.Journal!.Read());
    }

    /// <summary>Every new owner gets a fresh generation while read-only health snapshots do not contact the device.</summary>
    [Fact]
    public void Heartbeat_snapshot_has_epoch_and_unique_generation()
    {
        var clock = new ManualClock(); var registry = new HostResourceRegistry(heartbeatPolicy: HostHeartbeatPolicy.Default, timeProvider: clock);
        var a = registry.Reserve("DMM1")!; var first = registry.InspectOwnerHeartbeat("DMM1", a.LeaseId)!;
        Assert.Equal(registry.HostEpoch, first.HostEpoch); Assert.Equal("Healthy", first.State); Assert.Equal(0, first.Sequence);
        registry.Release(a.LeaseId); var b = registry.Reserve("DMM1")!;
        Assert.True(registry.InspectOwnerHeartbeat("DMM1", b.LeaseId)!.OwnerGeneration > first.OwnerGeneration);
        Assert.Throws<UnauthorizedAccessException>(() => registry.InspectOwnerHeartbeat("FGEN1", b.LeaseId));
    }

    /// <summary>Duplicate and out-of-order renewals never extend the owner's last-seen timestamp.</summary>
    [Fact]
    public void Heartbeat_replay_does_not_extend_ownership()
    {
        var clock = new ManualClock(); var registry = new HostResourceRegistry(heartbeatPolicy: HostHeartbeatPolicy.Default,timeProvider:clock);
        var grant = registry.Reserve("DMM1")!; var snapshot = registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!; var peer = Guid.NewGuid();
        registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,snapshot.HostEpoch,snapshot.OwnerGeneration,peer,2);
        clock.Advance(TimeSpan.FromSeconds(2));
        foreach (var sequence in new long[] {0,1,2}) Assert.Throws<InvalidOperationException>(() => registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,snapshot.HostEpoch,snapshot.OwnerGeneration,peer,sequence));
        Assert.Equal(2000,registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!.AgeMilliseconds);
        Assert.Equal(0,registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,snapshot.HostEpoch,snapshot.OwnerGeneration,peer,3).AgeMilliseconds);
    }

    /// <summary>Process, owner and peer epochs fence stale messages even when a sequence is larger.</summary>
    [Fact]
    public void Heartbeat_rejects_wrong_epochs_and_generations()
    {
        var registry = new HostResourceRegistry(heartbeatPolicy:HostHeartbeatPolicy.Default);var grant=registry.Reserve("DMM1")!;
        var s=registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!;var peer=Guid.NewGuid();
        Assert.Throws<UnauthorizedAccessException>(()=>registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,Guid.NewGuid(),s.OwnerGeneration,peer,1));
        Assert.Throws<UnauthorizedAccessException>(()=>registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,s.HostEpoch,s.OwnerGeneration+1,peer,1));
        registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,s.HostEpoch,s.OwnerGeneration,peer,1);
        Assert.Throws<InvalidOperationException>(()=>registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.NewGuid(),2));
        Assert.Equal(1,registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!.Sequence);
    }

    /// <summary>Wall-clock changes cannot affect suspect/expiry deadlines, and expiry is irreversible.</summary>
    [Fact]
    public void Heartbeat_uses_monotonic_time_and_never_revives_expired_owners()
    {
        var clock=new ManualClock();var registry=new HostResourceRegistry(heartbeatPolicy:HostHeartbeatPolicy.Default,timeProvider:clock);
        var grant=registry.Reserve("DMM1")!;var s=registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!;var peer=Guid.NewGuid();
        clock.WallClock=clock.WallClock.AddYears(10);Assert.Equal("Healthy",registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!.State);
        clock.Advance(TimeSpan.FromSeconds(15));Assert.Equal("Suspect",registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!.State);
        registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,s.HostEpoch,s.OwnerGeneration,peer,1);
        clock.WallClock=clock.WallClock.AddYears(-20);clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("Expired",registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!.State);
        Assert.Throws<InvalidOperationException>(()=>registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,s.HostEpoch,s.OwnerGeneration,peer,2));
        Assert.Null(registry.Reserve("DMM1"));
    }

    /// <summary>Expired owners cannot measure, close generically or transfer ownership; durable evidence remains.</summary>
    [Fact]
    public void Heartbeat_expiry_fences_driver_dispatch_and_close()
    {
        var clock=new ManualClock();using var f=new Fixture(heartbeat:HostHeartbeatPolicy.Default,clock:clock);var active=f.Activate();
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Throws<InvalidOperationException>(()=>f.Service.Measure(f.Id,active.Lease.LeaseId));
        Assert.Throws<InvalidOperationException>(()=>f.Registry.Release(active.Lease.LeaseId));
        Assert.Equal(0,f.MeasureCalls);Assert.Equal(0,f.CloseCalls);Assert.Single(f.Journal!.Read());Assert.Null(f.Registry.Reserve(f.Id));
    }

    /// <summary>Heartbeat remains responsive during blocked I/O, while an expired queued command never reaches the driver.</summary>
    /// <returns>Completion after the admitted operation and fenced queued request finish.</returns>
    [Fact]
    public async Task Heartbeat_is_independent_of_io_and_fences_queued_dispatch()
    {
        var clock=new ManualClock();using var f=new Fixture(heartbeat:HostHeartbeatPolicy.Default,clock:clock);var active=f.Activate();
        var s=f.Registry.InspectOwnerHeartbeat(f.Id,active.Lease.LeaseId)!;var peer=Guid.NewGuid();
        using var entered=new ManualResetEventSlim();using var finish=new ManualResetEventSlim();
        f.OnMeasure=()=>{entered.Set();if(!finish.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();};
        var running=Task.Run(()=>f.Service.Measure(f.Id,active.Lease.LeaseId));Task? queued=null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.Equal("Healthy",f.Registry.RenewOwnerHeartbeat(f.Id,active.Lease.LeaseId,s.HostEpoch,s.OwnerGeneration,peer,1).State);
            Assert.Equal("Busy",f.Service.Status(f.Id,active.Lease.LeaseId).State);
            queued=Task.Run(()=>f.Service.Measure(f.Id,active.Lease.LeaseId));clock.Advance(TimeSpan.FromSeconds(30));
        }
        finally{finish.Set();await running;}
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await queued!);
        Assert.Equal(1,f.MeasureCalls);Assert.Single(f.Journal!.Read());
    }

    /// <summary>Heartbeat expiry needs explicit provider reconciliation before fresh ownership; it never invokes recovery itself.</summary>
    [Fact]
    public void Expired_owner_requires_explicit_recovery_before_reassignment()
    {
        var clock=new ManualClock();using var f=new Fixture(heartbeat:HostHeartbeatPolicy.Default,clock:clock);var active=f.Activate();
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("RecoveryRequired",f.Registry.Monitor().Instruments.Single(i=>i.ResourceId==f.Id).Status);
        Assert.Equal(0,f.RecoveryCalls);
        Assert.Throws<InvalidOperationException>(()=>f.Service.Recover(f.Id,active.Lease.LeaseId,false));
        f.Service.Recover(f.Id,active.Lease.LeaseId,true);Assert.Equal(1,f.RecoveryCalls);Assert.Empty(f.Journal!.Read());
        var next=f.Activate();Assert.NotEqual(active.Lease.LeaseId,next.Lease.LeaseId);
        Assert.Throws<UnauthorizedAccessException>(()=>f.Registry.InspectOwnerHeartbeat(f.Id,active.Lease.LeaseId));
    }

    /// <summary>Background health observation is cancellable and never closes or recovers a silent owner.</summary>
    /// <returns>Completion after the monitor service stops without changing ownership.</returns>
    [Fact]
    public async Task Heartbeat_monitor_observes_without_hardware_action()
    {
        var clock=new ManualClock();using var f=new Fixture(heartbeat:HostHeartbeatPolicy.Default,clock:clock);f.Activate();
        clock.Advance(TimeSpan.FromSeconds(30));
        using var monitor=new OwnerHeartbeatService(f.Registry,HostHeartbeatPolicy.Default);
        await monitor.StartAsync(CancellationToken.None);Assert.Equal(1,f.Registry.ObserveOwnerHeartbeats());
        await monitor.StopAsync(CancellationToken.None);
        Assert.Equal(0,f.CloseCalls);Assert.Equal(0,f.RecoveryCalls);Assert.Single(f.Journal!.Read());
    }

    /// <summary>Heartbeat requests require every field and reject unknown JSON before control-owner mutation.</summary>
    [Fact]
    public void Heartbeat_requests_are_strict()
    {
        Assert.Throws<System.Text.Json.JsonException>(()=>BenchSetupService.ParseIviRequest<OwnerHeartbeatRequest>("{}"));
        Assert.Throws<System.Text.Json.JsonException>(()=>BenchSetupService.ParseIviRequest<OwnerHeartbeatRequest>("{\"leaseId\":\"00000000-0000-0000-0000-000000000001\",\"unknown\":true}"));
        var clock=new ManualClock();using var f=new Fixture(heartbeat:HostHeartbeatPolicy.Default,clock:clock);var active=f.Activate();
        Assert.Equal("Healthy",f.Service.Status(f.Id,active.Lease.LeaseId).Heartbeat!.State);
        clock.Advance(TimeSpan.FromSeconds(30));Assert.Equal("OwnerExpired",f.Service.Status(f.Id,active.Lease.LeaseId).State);
    }

    private sealed class HeartbeatHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Respond = (_,_)=>throw new InvalidOperationException();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Respond(request,token);
    }

    /// <summary>Client renewal validates acknowledgment identity and stops after ownership rejection.</summary>
    /// <returns>Completion after success and terminal rejection are observed.</returns>
    [Fact]
    public async Task Heartbeat_client_validates_acknowledgments()
    {
        var handler=new HeartbeatHandler();using var client=new HttpClient(handler){BaseAddress=new Uri("http://localhost/")};
        var snapshot=new OwnerHeartbeatSnapshot(Guid.NewGuid(),1,Guid.Empty,0,"Healthy",0,1000,30000,"FenceAndRetain");
        var calls=0;
        handler.Respond=async (request,token)=>
        {
            calls++;var body=await request.Content!.ReadFromJsonAsync<OwnerHeartbeatRequest>(token);
            return new(HttpStatusCode.OK){Content=JsonContent.Create(snapshot with{PeerEpoch=body!.PeerEpoch,Sequence=body.Sequence})};
        };
        await using var heartbeat=new OwnerLeaseHeartbeat(client,"METER",Guid.NewGuid(),snapshot);
        Assert.Equal("Healthy",await heartbeat.RenewAsync());Assert.Equal(1,heartbeat.LastAcknowledgment!.Sequence);
        handler.Respond=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
        Assert.Equal("Lost",await heartbeat.RenewAsync());Assert.Equal("Lost",await heartbeat.RenewAsync());Assert.Equal(1,calls);
    }

    /// <summary>Stopping cancels a stuck renewal, observer exceptions are isolated, and disposal is repeatable.</summary>
    /// <returns>Completion after acknowledgement notification and cancellation of a later request.</returns>
    [Fact]
    public async Task Heartbeat_client_cancellation_and_observers_are_isolated()
    {
        var handler=new HeartbeatHandler();using var client=new HttpClient(handler){BaseAddress=new Uri("http://localhost/")};
        var snapshot=new OwnerHeartbeatSnapshot(Guid.NewGuid(),1,Guid.NewGuid(),0,"Healthy",0,1000,30000,"FenceAndRetain");
        handler.Respond=async(request,token)=>{var body=await request.Content!.ReadFromJsonAsync<OwnerHeartbeatRequest>(token);return new(HttpStatusCode.OK){Content=JsonContent.Create(snapshot with{Sequence=body!.Sequence})};};
        var heartbeat=new OwnerLeaseHeartbeat(client,"METER",Guid.NewGuid(),snapshot);var observed=0;
        heartbeat.StatusChanged+=_=>throw new IOException();heartbeat.StatusChanged+=_=>observed++;
        Assert.Equal("Healthy",await heartbeat.RenewAsync());Assert.Equal(1,observed);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond=async(_,token)=>{entered.SetResult();await Task.Delay(Timeout.Infinite,token);return new(HttpStatusCode.OK);};
        heartbeat.Start();await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await heartbeat.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));await heartbeat.DisposeAsync();
        Assert.Equal("Stopped",heartbeat.Status);
    }

    /// <summary>Host restart changes its epoch and rejects the old capability while durable ownership stays quarantined.</summary>
    [Fact]
    public void Restart_rejects_old_heartbeat_without_clearing_quarantine()
    {
        using var f=new Fixture(heartbeat:HostHeartbeatPolicy.Default);var active=f.Activate();
        var old=f.Registry.InspectOwnerHeartbeat(f.Id,active.Lease.LeaseId)!;
        var restarted=new HostResourceRegistry(journal:f.Journal,heartbeatPolicy:HostHeartbeatPolicy.Default);
        Assert.NotEqual(old.HostEpoch,restarted.HostEpoch);
        Assert.Throws<UnauthorizedAccessException>(()=>restarted.RenewOwnerHeartbeat(f.Id,active.Lease.LeaseId,old.HostEpoch,old.OwnerGeneration,Guid.NewGuid(),1));
        Assert.Single(restarted.PendingRecovery());Assert.Single(f.Journal!.Read());
    }

    /// <summary>Observers do not renew owners, and one silent experiment cannot expire a separately renewed instrument.</summary>
    [Fact]
    public void Heartbeat_distinguishes_observation_and_independent_owners()
    {
        var clock=new ManualClock();using var f=new Fixture(heartbeat:HostHeartbeatPolicy.Default,clock:clock);
        using var other=new Fixture(f.Registry,"OTHER");var a=f.Activate();var b=other.Activate();
        var health=f.Registry.InspectOwnerHeartbeat(other.Id,b.Lease.LeaseId)!;var peer=Guid.NewGuid();
        clock.Advance(TimeSpan.FromSeconds(15));f.Registry.Monitor();f.Service.Status(f.Id,a.Lease.LeaseId);
        f.Registry.RenewOwnerHeartbeat(other.Id,b.Lease.LeaseId,health.HostEpoch,health.OwnerGeneration,peer,1);
        clock.Advance(TimeSpan.FromSeconds(15));f.Registry.ObserveOwnerHeartbeats();
        Assert.Equal("Expired",f.Registry.InspectOwnerHeartbeat(f.Id,a.Lease.LeaseId)!.State);
        Assert.Equal("Suspect",f.Registry.InspectOwnerHeartbeat(other.Id,b.Lease.LeaseId)!.State);
        f.Registry.RenewOwnerHeartbeat(other.Id,b.Lease.LeaseId,health.HostEpoch,health.OwnerGeneration,peer,2);
        Assert.Equal(1,other.Service.Measure(other.Id,b.Lease.LeaseId));Assert.Equal(0,f.MeasureCalls);
    }

    /// <summary>Responsive heartbeat remains distinct from device faults and monotonic cached-reading age.</summary>
    [Fact]
    public void Heartbeat_readiness_and_reading_age_are_independent()
    {
        var clock=new ManualClock();using var f=new Fixture(heartbeat:HostHeartbeatPolicy.Default,clock:clock);var active=f.Activate();
        Assert.Null(f.Service.Status(f.Id,active.Lease.LeaseId).ReadingAgeMilliseconds);
        f.Service.Measure(f.Id,active.Lease.LeaseId);var original=f.Service.Status(f.Id,active.Lease.LeaseId);
        clock.Advance(TimeSpan.FromSeconds(7));clock.WallClock=clock.WallClock.AddYears(-2);
        f.OnMeasure=()=>throw new IOException();Assert.Throws<InvalidOperationException>(()=>f.Service.Measure(f.Id,active.Lease.LeaseId));
        var status=f.Service.Status(f.Id,active.Lease.LeaseId);var health=status.Heartbeat!;
        Assert.Equal("RecoveryRequired",status.State);Assert.Equal("Healthy",health.State);
        Assert.Equal(7000,status.ReadingAgeMilliseconds);Assert.Equal(original.LastReadingUtc,status.LastReadingUtc);
        Assert.Equal(original.LastDeviceSuccessUtc,status.LastDeviceSuccessUtc);
        Assert.Equal("Healthy",f.Registry.RenewOwnerHeartbeat(f.Id,active.Lease.LeaseId,health.HostEpoch,health.OwnerGeneration,Guid.NewGuid(),1).State);
        Assert.Equal("RecoveryRequired",f.Service.Status(f.Id,active.Lease.LeaseId).State);
    }

    /// <summary>Heartbeat transitions are logged without capabilities; a failed sink cannot alter fencing or renewal.</summary>
    [Fact]
    public void Heartbeat_logs_transitions_without_changing_ownership()
    {
        var log=new Capture();var clock=new ManualClock();using var f=new Fixture(logger:log,heartbeat:HostHeartbeatPolicy.Default,clock:clock);var active=f.Activate();
        var h=f.Registry.InspectOwnerHeartbeat(f.Id,active.Lease.LeaseId)!;var peer=Guid.NewGuid();
        f.Registry.RenewOwnerHeartbeat(f.Id,active.Lease.LeaseId,h.HostEpoch,h.OwnerGeneration,peer,1);
        clock.Advance(TimeSpan.FromSeconds(15));f.Registry.ObserveOwnerHeartbeats();
        f.Registry.RenewOwnerHeartbeat(f.Id,active.Lease.LeaseId,h.HostEpoch,h.OwnerGeneration,peer,2);
        Assert.Contains(7493,log.Ids);Assert.Contains(7490,log.Ids);Assert.Contains(7492,log.Ids);
        log.Throw=true;clock.Advance(TimeSpan.FromSeconds(30));Assert.Equal(1,f.Registry.ObserveOwnerHeartbeats());
        Assert.True(f.Registry.LoggingFailureCount>0);Assert.Single(f.Journal!.Read());
        Assert.DoesNotContain(active.Lease.LeaseId.ToString(),string.Join("\n",log.Messages));
        log.Throw=false;
    }

    /// <summary>Configuration cannot disable expiry through malformed or overflowed budgets; draining cannot renew an owner.</summary>
    [Fact]
    public void Heartbeat_configuration_and_shutdown_are_fail_closed()
    {
        foreach(var value in new[]{"NaN","-1","0","9999999999999999999","SECRET"})
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["BenchHeartbeat:IntervalMilliseconds"]=value}).Build();
            Assert.ThrowsAny<ArgumentException>(()=>HostHeartbeatPolicy.FromConfiguration(config));
        }
        Assert.Throws<ArgumentOutOfRangeException>(()=>new HostHeartbeatPolicy(TimeSpan.FromSeconds(1),TimeSpan.MaxValue,TimeSpan.MaxValue).Validate());
        var registry=new HostResourceRegistry(heartbeatPolicy:HostHeartbeatPolicy.Default);var grant=registry.Reserve("DMM1")!;
        var h=registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!;registry.BeginDrain();
        Assert.Throws<InvalidOperationException>(()=>registry.RenewOwnerHeartbeat("DMM1",grant.LeaseId,h.HostEpoch,h.OwnerGeneration,Guid.NewGuid(),1));
        Assert.Equal(0,registry.InspectOwnerHeartbeat("DMM1",grant.LeaseId)!.Sequence);
    }

}
