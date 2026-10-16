using BenchDouble.Host.Server.Setup;
using BenchDouble.Ivi;
using BenchDouble.Ivi.Official;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Official typed-interface dispatch through the canonical host lease lifecycle.</summary>
public sealed class HostedIviDmmTests
{
    /// <summary>Explicit activation pins the saved route, supports typed calls and shares direct-client contention.</summary>
    [Fact]
    public void Typed_activation_pins_binding_and_closes_before_release()
    {
        using var f = new Fixture();
        var active = f.Activate();
        Assert.True(active.Active);
        Assert.False(f.LastOpen!.Reset);
        Assert.True(f.LastOpen.IdQuery);
        Assert.Null(f.Registry.Reserve("METER"));
        Assert.Null(f.Service.Activate("meter", f.Saved.Revision, true));
        f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.DcVoltage);
        Assert.Equal(4.5, f.Service.Measure("METER", active.Lease.LeaseId));
        Assert.Equal(1, f.ConfigureCalls);
        Assert.Throws<KeyNotFoundException>(() => f.Registry.Query("METER", active.Lease.LeaseId, "*IDN?"));
        var alternate = f.Configuration with { Instruments = [f.Configuration.Instruments[0] with { SelectedConnection = "alternate" }] };
        f.Setup.Save(new(alternate, f.Saved.Revision, true));
        Assert.Equal("TCPIP0::meter::inst0::INSTR", active.Lease.Connection.Address);
        Assert.True(f.Registry.Release(active.Lease.LeaseId));
        Assert.Equal(1, f.CloseCalls);
        Assert.False(f.Registry.Release(active.Lease.LeaseId));
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.Measure("METER", active.Lease.LeaseId));
        Assert.NotNull(f.Registry.Reserve("METER"));
    }

    /// <summary>Opening failures retain ownership until the registered recovery procedure succeeds.</summary>
    [Fact]
    public void Opening_failure_and_failed_recovery_do_not_unlock_instrument()
    {
        using var f = new Fixture { FailOpen = true, FailRecovery = true };
        var failed = f.Activate();
        Assert.False(failed.Active);
        Assert.Equal("recovery_required", failed.ErrorCode);
        Assert.Equal("RecoveryRequired", f.Registry.Monitor().Instruments.Single(i => i.ResourceId == "METER").Status);
        Assert.Null(f.Registry.Reserve("METER"));
        Assert.Throws<InvalidOperationException>(() => f.Registry.Release(failed.Lease.LeaseId));
        Assert.Throws<InvalidOperationException>(() => f.Service.Recover("METER", failed.Lease.LeaseId, false));
        Assert.Throws<InvalidOperationException>(() => f.Service.Recover("METER", failed.Lease.LeaseId, true));
        Assert.Null(f.Registry.Reserve("METER"));
        f.FailRecovery = false;
        f.Service.Recover("METER", failed.Lease.LeaseId, true);
        Assert.NotNull(f.Registry.Reserve("METER"));
        Assert.DoesNotContain("SECRET", string.Join("\n", f.Log.Messages));
        Assert.Contains(f.Log.Ids, i => i == 7455);
        Assert.Contains(f.Log.Ids, i => i == 7456);
    }

    /// <summary>Command and disposal failures block replay and prevent direct-release bypass.</summary>
    /// <param name="operationFailure">True injects a measurement failure; false injects a cleanup failure.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Operation_or_cleanup_failure_requires_recovery(bool operationFailure)
    {
        using var f = new Fixture { FailMeasure = operationFailure, FailClose = !operationFailure };
        var active = f.Activate();
        if (operationFailure)
        {
            Assert.Throws<InvalidOperationException>(() => f.Service.Measure("METER", active.Lease.LeaseId));
            Assert.Throws<InvalidOperationException>(() => f.Service.Measure("METER", active.Lease.LeaseId));
            Assert.Equal(1, f.MeasureCalls);
        }
        Assert.Throws<InvalidOperationException>(() => f.Registry.Release(active.Lease.LeaseId));
        var attempts = f.CloseCalls;
        Assert.Throws<InvalidOperationException>(() => f.Registry.Release(active.Lease.LeaseId));
        Assert.Equal(attempts, f.CloseCalls);
        Assert.Null(f.Registry.Reserve("METER"));
        f.Service.Recover("METER", active.Lease.LeaseId, true);
        Assert.Equal(1, f.RecoveryCalls);
    }

    /// <summary>Preflight rejects changed settings, route, identity policy and stale review before opening.</summary>
    [Fact]
    public void Invalid_review_never_dispatches()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidOperationException>(() => f.Service.Activate("meter", f.Saved.Revision, false));
        Assert.Throws<InvalidOperationException>(() => f.Service.Activate("meter", "stale", true));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => f.Service.Activate("meter", f.Saved.Revision, true, cts.Token));
        var route = f.Configuration.Instruments[0].Connections[0];
        var changed = f.Configuration with { Instruments = [f.Configuration.Instruments[0] with
        { Connections = [route with { Address = "TCPIP0::other::inst0::INSTR" }] }] };
        var saved = f.Setup.Save(new(changed, f.Saved.Revision, true));
        Assert.Throws<InvalidOperationException>(() => f.Service.Activate("meter", saved.Revision, true));
        var options = route.DriverBinding! with { Settings = new() { ["Simulate"] = "True" } };
        changed = f.Configuration with { Instruments = [f.Configuration.Instruments[0] with
        { Connections = [route with { DriverBinding = options }] }] };
        saved = f.Setup.Save(new(changed, saved.Revision, true));
        Assert.Throws<InvalidOperationException>(() => f.Service.Activate("meter", saved.Revision, true));
        Assert.Equal(0, f.OpenCalls);
    }

    /// <summary>GPIB activation requires the current saved adapter confirmation, not an opaque resource string.</summary>
    /// <returns>Completion after discovery and controller confirmation are exercised.</returns>
    [Fact]
    public async Task Gpib_activation_requires_current_controller_confirmation()
    {
        using var f = new Fixture();
        var route = f.Configuration.Instruments[0].Connections[1] with { Transport = "GPIB", Address = "22", ControllerId = "rack" };
        var draft = f.Configuration with { Controllers = [new("rack", "visa", "GPIB0")],
            Instruments = [f.Configuration.Instruments[0] with { Connections = [route], SelectedConnection = route.Id }] };
        var saved = f.Setup.Save(new(draft, f.Saved.Revision, true));
        Assert.Throws<InvalidOperationException>(() => f.Service.Activate("meter", saved.Revision, true));
        var discovery = await f.Setup.DiscoverAsync(provider: new Controllers());
        draft = draft with { Controllers = [draft.Controllers[0] with { ConfirmedDiscoveryId = discovery.Id }] };
        saved = f.Setup.Save(new(draft, saved.Revision, true));
        var active = f.Service.Activate("meter", saved.Revision, true)!;
        Assert.True(active.Active);
        f.Registry.Release(active.Lease.LeaseId);
        await f.Setup.DiscoverAsync(provider: new Controllers());
        Assert.Throws<InvalidOperationException>(() => f.Service.Activate("meter", saved.Revision, true));
        Assert.Equal(1, f.OpenCalls);
    }

    /// <summary>Cancellation after dispatch cannot unlock while synchronous vendor work is still running.</summary>
    /// <returns>Completion after the blocked call and queued release actually finish.</returns>
    [Fact]
    public async Task Inflight_call_retains_lease_until_completion()
    {
        using var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        f.OnMeasure = () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        var active = f.Activate();
        using var cancel = new CancellationTokenSource();
        var measurement = Task.Run(() => f.Service.Measure("METER", active.Lease.LeaseId, cancel.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            cancel.Cancel();
            Assert.Null(f.Registry.Reserve("METER"));
            // The global registry gate remains free while this vendor call blocks.
            var independent = f.Registry.Reserve("DMM1")!;
            Assert.Contains("5", f.Registry.Query("DMM1", independent.LeaseId, "MEAS:VOLT:DC?"));
            Assert.True(f.Registry.Release(independent.LeaseId));
            var release = Task.Run(() => f.Registry.Release(active.Lease.LeaseId));
            Assert.Equal(0, f.CloseCalls);
            finish.Set();
            Assert.Equal(4.5, await measurement);
            Assert.True(await release);
            Assert.Equal(1, f.CloseCalls);
        }
        finally { finish.Set(); await measurement; }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "ivi-active-" + Guid.NewGuid() + ".json");
        public readonly HostResourceRegistry Registry = new();
        public readonly Capture Log = new();
        public readonly BenchSetupService Setup;
        public readonly HostedIviDmmService Service;
        public readonly BenchSetup Configuration;
        public readonly SavedSetup Saved;
        public bool FailOpen, FailMeasure, FailClose, FailRecovery;
        public int OpenCalls, ConfigureCalls, MeasureCalls, CloseCalls, RecoveryCalls;
        public Action? OnMeasure;
        public IviDmmOpenRequest? LastOpen;
        public Fixture()
        {
            Setup = new(Registry, new SimulatedSetupDiscoveryProvider(Registry), _path);
            var binding = new SetupDriverBinding(new string('a', 64), "meter", "session",
                new("driver", "IviNetSoftwareModule", "Vendor.Driver, Vendor"), "TCPIP0::meter::inst0::INSTR",
                new() { ["Simulate"] = "False" }, new());
            var alternate = binding with { ResourceDescriptor = "GPIB0::22::INSTR" };
            Configuration = new(2, [], [new("METER", "Meter", "primary",
                [new("primary", "visa", "VISA", binding.ResourceDescriptor, DriverBinding: binding),
                 new("alternate", "visa", "VISA", alternate.ResourceDescriptor, DriverBinding: alternate)], true)], [new("meter", "METER")]);
            Saved = Setup.Save(new(Configuration, null, true));
            var registration = new IviDmmRegistration("METER", [binding, alternate], options =>
            {
                OpenCalls++; LastOpen = options;
                if (FailOpen) throw new InvalidOperationException("SECRET startup");
                var official = Substitute.For<global::Ivi.Dmm.IIviDmm>();
                var bridge = new OfficialDmmBridge(official, (_, _, _, _) => ConfigureCalls++, _ =>
                { MeasureCalls++; OnMeasure?.Invoke(); if (FailMeasure) throw new IOException("SECRET measurement"); return 4.5; },
                    _ => throw new InvalidOperationException("Reset must not be invoked"), _ => "vendor identity");
                return new(bridge, () => { CloseCalls++; if (FailClose) throw new IOException("SECRET cleanup"); });
            }, _ => { RecoveryCalls++; if (FailRecovery) throw new IOException("SECRET recovery"); });
            Service = new(Registry, Setup, [registration], Log);
        }
        public IviDmmActivation Activate() => Service.Activate("meter", Saved.Revision, true)!;
        public void Dispose() => File.Delete(_path);
    }

    private sealed class Capture : ILogger<HostedIviDmmService>
    {
        public readonly List<string> Messages = [];
        public readonly List<int> Ids = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { lock (Messages) { Ids.Add(id.Id); Messages.Add(formatter(state, exception)); } }
    }

    private sealed class Controllers : ISetupDiscoveryProvider
    {
        public ValueTask<SetupDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SetupDiscovery(Guid.NewGuid().ToString("N"), [new("visa", "GPIB0", "adapter")], [], []));
    }
}
