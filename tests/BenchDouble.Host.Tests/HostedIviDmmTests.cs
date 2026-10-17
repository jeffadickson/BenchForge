using BenchDouble.Host.Server.Recovery;
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
        var active = f.Activate(configure: false);
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
            Assert.Equal("Busy", f.Service.Status("METER", active.Lease.LeaseId).State);
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

    /// <summary>A conflicting registration cannot leave earlier entries partially registered.</summary>
    [Fact]
    public void Registration_batch_is_atomic()
    {
        using var f = new Fixture();
        var binding = f.Configuration.Instruments[0].Connections[0].DriverBinding!;
        IviDmmRegistration Registration(string id, string descriptor) => new(id,
            [binding with { ResourceDescriptor = descriptor }], _ => throw new Exception("No I/O"), _ => { });
        Assert.Throws<ArgumentException>(() => new HostedIviDmmService(f.Registry, f.Setup,
            [Registration("NEW", "TCPIP0::new::INSTR"), Registration("DMM1", "TCPIP0::conflict::INSTR")], f.Log));
        Assert.DoesNotContain(f.Registry.List(), d => d.ResourceId == "NEW");
        Assert.Null(f.Registry.Reserve("NEW"));
    }

    /// <summary>Malformed trusted registration input receives a bounded error before any registration or I/O.</summary>
    [Fact]
    public void Invalid_registration_bindings_fail_before_registration()
    {
        using var f = new Fixture();
        var binding = f.Configuration.Instruments[0].Connections[0].DriverBinding!;
        foreach (var invalid in new SetupDriverBinding[] { null!, binding with { Settings = null! },
            binding with { Module = null! }, binding with { SourceRevision = "bad" },
            binding with { ResourceDescriptor = "" }, binding with { VirtualNames = new() { ["channel"] = null! } } })
        {
            Assert.Throws<ArgumentException>(() => new HostedIviDmmService(f.Registry, f.Setup,
                [new("NEW", [invalid], _ => throw new Exception("No I/O"), _ => { })], f.Log));
            Assert.DoesNotContain(f.Registry.List(), d => d.ResourceId == "NEW");
        }
    }

    /// <summary>Measurement is refused without I/O until a valid configuration has completed.</summary>
    [Fact]
    public void Measurement_requires_successful_configuration()
    {
        using var f = new Fixture();
        var active = f.Activate(configure: false);
        Assert.Throws<InvalidOperationException>(() => f.Service.Measure("METER", active.Lease.LeaseId));
        Assert.Throws<ArgumentException>(() => f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.DcVoltage, double.NaN));
        Assert.Equal(0, f.MeasureCalls);
        Assert.Equal(0, f.ConfigureCalls);
        f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.DcVoltage);
        Assert.Equal(4.5, f.Service.Measure("METER", active.Lease.LeaseId));
        Assert.True(f.Registry.Release(active.Lease.LeaseId));
    }

    /// <summary>Logs identify operation, revision and duration without leaking capability tokens or measured values.</summary>
    [Fact]
    public void Lifecycle_logs_correlate_operations_without_payloads()
    {
        using var f = new Fixture();
        var active = f.Activate();
        f.Service.Measure("METER", active.Lease.LeaseId);
        f.Registry.Release(active.Lease.LeaseId);
        var log = string.Join("\n", f.Log.Messages);
        Assert.Contains(f.Saved.Revision, log);
        Assert.Contains("Configure started", log);
        Assert.Contains("Measure completed in", log);
        Assert.DoesNotContain(active.Lease.LeaseId.ToString(), log);
        Assert.Contains(7457, f.Log.Ids);
        Assert.Equal(2, f.Log.Ids.Count(id => id == 7453));
    }

    /// <summary>Direct service clients receive the same payload-free preflight diagnostics as HTTP clients.</summary>
    [Fact]
    public void Rejected_activation_records_stable_reason_without_user_input()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidOperationException>(() => f.Service.Activate("SECRET role", f.Saved.Revision, false));
        Assert.Throws<InvalidOperationException>(() => f.Service.Activate("meter", "SECRET revision", true));
        Assert.Throws<InvalidOperationException>(() => f.Service.Activate("SECRET role", f.Saved.Revision, true));
        Assert.Equal(3, f.Log.Ids.Count(id => id == 7458));
        var log = string.Join("\n", f.Log.Messages);
        Assert.Contains("confirmation_missing", log);
        Assert.Contains("revision_changed", log);
        Assert.Contains("role_missing", log);
        Assert.DoesNotContain("SECRET", log);
        Assert.Equal(0, f.OpenCalls);
    }

    /// <summary>Optional identity checks compare only supplied fields and quarantine mismatches after opening.</summary>
    /// <param name="serial">Optional expected serial; matching is case sensitive.</param>
    /// <param name="matches">Whether initialization should succeed.</param>
    [Theory]
    [InlineData(null, true)]
    [InlineData("SECRET-serial", true)]
    [InlineData("secret-serial", false)]
    [InlineData("different", false)]
    public void Expected_identity_is_optional_and_mismatches_retain_ownership(string? serial, bool matches)
    {
        using var f = new Fixture(identityReader: true, expected: new("acme", "DMM", serial));
        var active = f.Activate(configure: false);
        Assert.Equal(matches, active.Active);
        Assert.Equal(1, f.IdentityCalls);
        Assert.Equal(0, f.ConfigureCalls);
        Assert.Null(f.Registry.Reserve("METER"));
        if (matches) Assert.True(f.Registry.Release(active.Lease.LeaseId));
        else
        {
            Assert.Throws<InvalidOperationException>(() => f.Registry.Release(active.Lease.LeaseId));
            f.Service.Recover("METER", active.Lease.LeaseId, true);
        }
        Assert.DoesNotContain("SECRET-serial", string.Join("\n", f.Log.Messages));
    }

    /// <summary>Unsupported identity policies fail before I/O; absent expectations do not trigger extra queries.</summary>
    [Fact]
    public void Identity_reader_is_required_only_for_requested_checks()
    {
        using var unsupported = new Fixture(expected: new(Model: "DMM"));
        Assert.Throws<InvalidOperationException>(() => unsupported.Activate());
        Assert.Equal(0, unsupported.OpenCalls);
        using var optional = new Fixture(identityReader: true);
        var active = optional.Activate();
        Assert.Equal(0, optional.IdentityCalls);
        optional.Registry.Release(active.Lease.LeaseId);
    }

    /// <summary>Session inspection validates ownership and reports cached state without vendor calls.</summary>
    [Fact]
    public void Session_status_is_cached_and_lease_scoped()
    {
        using var f = new Fixture();
        var active = f.Activate(configure: false);
        var status = f.Service.Status("METER", active.Lease.LeaseId);
        Assert.Equal("Ready", status.State);
        Assert.Null(status.Function);
        Assert.Equal(f.Saved.Revision, status.Revision);
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.Status("OTHER", active.Lease.LeaseId));
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.Status("METER", Guid.NewGuid()));
        f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.DcVoltage);
        Assert.Equal(IviMeasurementFunction.DcVoltage, f.Service.Status("METER", active.Lease.LeaseId).Function);
        f.FailMeasure = true;
        Assert.Throws<InvalidOperationException>(() => f.Service.Measure("METER", active.Lease.LeaseId));
        Assert.Equal("RecoveryRequired", f.Service.Status("METER", active.Lease.LeaseId).State);
        Assert.Equal(1, f.OpenCalls);
        Assert.Equal(1, f.MeasureCalls);
        f.Service.Recover("METER", active.Lease.LeaseId, true);
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.Status("METER", active.Lease.LeaseId));
    }

    /// <summary>Queued cancellation returns before blocked vendor work finishes and never releases its lease.</summary>
    /// <returns>Completion after both cancellation and the original call are observed.</returns>
    [Fact]
    public async Task Queued_cancellation_does_not_wait_for_vendor_or_release_owner()
    {
        using var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        f.OnMeasure = () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        var active = f.Activate();
        var running = Task.Run(() => f.Service.Measure("METER", active.Lease.LeaseId));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            using var cancel = new CancellationTokenSource();
            var queued = Task.Run(() => f.Service.Measure("METER", active.Lease.LeaseId, cancel.Token));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, f.MeasureCalls);
            Assert.Equal("Busy", f.Service.Status("METER", active.Lease.LeaseId).State);
            Assert.Null(f.Registry.Reserve("METER"));
        }
        finally { finish.Set(); await running; f.Registry.Release(active.Lease.LeaseId); }
    }

    /// <summary>Concurrent successful cleanup calls close the driver exactly once.</summary>
    /// <returns>Completion after all competing releases finish.</returns>
    [Fact]
    public async Task Concurrent_release_closes_once()
    {
        using var f = new Fixture();
        var active = f.Activate();
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => f.Registry.Release(active.Lease.LeaseId))));
        Assert.Single(results.Where(x => x));
        Assert.Equal(1, f.CloseCalls);
    }

    /// <summary>Failed configuration is never replayed and cannot authorize a subsequent measurement.</summary>
    [Fact]
    public void Configure_failure_quarantines_without_replay()
    {
        using var f = new Fixture { FailConfigure = true };
        var active = f.Activate(configure: false);
        Assert.Throws<InvalidOperationException>(() => f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.DcVoltage));
        Assert.Throws<InvalidOperationException>(() => f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.DcVoltage));
        Assert.Throws<InvalidOperationException>(() => f.Service.Measure("METER", active.Lease.LeaseId));
        Assert.Equal(1, f.ConfigureCalls);
        Assert.Equal(0, f.MeasureCalls);
        Assert.Equal("RecoveryRequired", f.Service.Status("METER", active.Lease.LeaseId).State);
        f.Service.Recover("METER", active.Lease.LeaseId, true);
    }

    /// <summary>A broken log sink cannot turn success into quarantine or defeat cleanup.</summary>
    [Fact]
    public void Log_sink_failure_does_not_change_driver_outcomes()
    {
        using var f = new Fixture();
        f.Log.Fail = true;
        var active = f.Activate();
        Assert.True(active.Active);
        Assert.Equal(4.5, f.Service.Measure("METER", active.Lease.LeaseId));
        Assert.True(f.Registry.Release(active.Lease.LeaseId));
        Assert.Equal(1, f.CloseCalls);
        Assert.True(f.Service.LoggingFailureCount > 0);
        f.FailOpen = true;
        var failed = f.Activate();
        Assert.False(failed.Active);
        Assert.Null(f.Registry.Reserve("METER"));
        f.Service.Recover("METER", failed.Lease.LeaseId, true);
    }

    /// <summary>Registry log failure cannot strand a grant before returning its capability.</summary>
    [Fact]
    public void Registry_log_sink_failure_preserves_reservation_results()
    {
        var registry = new HostResourceRegistry(new ThrowingLog<HostResourceRegistry>());
        var grant = registry.Reserve("DMM1")!;
        Assert.NotNull(grant);
        Assert.True(registry.Release(grant.LeaseId));
        Assert.True(registry.LoggingFailureCount > 0);
    }

    private sealed class ThrowingLog<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new IOException("SECRET log sink failure");
    }

    /// <summary>Separate provider services cannot assign the same route to different physical IDs.</summary>
    [Fact]
    public void Route_ownership_is_global_to_the_canonical_registry()
    {
        using var f = new Fixture();
        var binding = f.Configuration.Instruments[0].Connections[0].DriverBinding!;
        var duplicate = new IviDmmRegistration("SECOND", [binding with { ResourceDescriptor = binding.ResourceDescriptor.ToLowerInvariant() }],
            _ => throw new Exception("No I/O"), _ => { });
        Assert.Throws<ArgumentException>(() => new HostedIviDmmService(f.Registry, f.Setup, [duplicate], f.Log));
        Assert.DoesNotContain(f.Registry.List(), d => d.ResourceId == "SECOND");
        Assert.Equal(0, f.OpenCalls);
        var distinct = duplicate with { ApprovedBindings = [binding with { ResourceDescriptor = "TCPIP0::second::inst0::INSTR" }] };
        _ = new HostedIviDmmService(f.Registry, f.Setup, [distinct], f.Log);
        Assert.Contains(f.Registry.List(), d => d.ResourceId == "SECOND");
    }

    /// <summary>Provider capability declarations are copied and unsupported functions fail before dispatch.</summary>
    [Fact]
    public void Declared_functions_are_enforced_and_detached()
    {
        var functions = new[] { IviMeasurementFunction.Resistance };
        using var f = new Fixture(functions: functions);
        functions[0] = IviMeasurementFunction.DcCurrent;
        var published = f.Service.SupportedFunctions("METER");
        Assert.Equal(IviMeasurementFunction.Resistance, Assert.Single(published));
        published[0] = IviMeasurementFunction.AcVoltage;
        var active = f.Activate(configure: false);
        Assert.Throws<ArgumentException>(() => f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.DcVoltage));
        Assert.Equal(0, f.ConfigureCalls);
        Assert.Equal("Ready", f.Service.Status("METER", active.Lease.LeaseId).State);
        f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.Resistance);
        Assert.Equal(1, f.ConfigureCalls);
        f.Registry.Release(active.Lease.LeaseId);
    }

    /// <summary>Wire requests cannot default missing measurement functions or hide duplicate fields.</summary>
    [Fact]
    public void Runtime_requests_require_explicit_unambiguous_fields()
    {
        var lease = Guid.NewGuid().ToString();
        foreach (var json in new[] { "{}", "null", "{\"leaseId\":\"" + lease + "\"}",
            "{\"leaseId\":\"" + lease + "\",\"function\":0,\"Function\":1}",
            "{\"leaseId\":\"" + lease + "\",\"function\":0,\"SECRET\":true}" })
        {
            var error = Assert.Throws<System.Text.Json.JsonException>(() => BenchSetupService.ParseIviRequest<IviDmmConfigureRequest>(json));
            Assert.DoesNotContain("SECRET", error.Message);
        }
        var valid = BenchSetupService.ParseIviRequest<IviDmmConfigureRequest>("{\"leaseId\":\"" + lease + "\",\"function\":0}");
        Assert.Equal(IviMeasurementFunction.DcVoltage, valid.Function);
        Assert.Throws<System.Text.Json.JsonException>(() => BenchSetupService.ParseIviRequest<ActivateIviRoleRequest>("{\"expectedRevision\":\"test\"}"));
    }

    /// <summary>Reading metadata is pinned to its configuration and sequence rather than a later status query.</summary>
    [Fact]
    public void Measurement_metadata_matches_the_dispatch_configuration()
    {
        using var f = new Fixture(functions: [IviMeasurementFunction.DcVoltage, IviMeasurementFunction.Resistance]);
        var active = f.Activate();
        var before = DateTimeOffset.UtcNow;
        var voltage = f.Service.MeasureWithMetadata("METER", active.Lease.LeaseId);
        Assert.Equal("V", voltage.Unit);
        Assert.Equal(1, voltage.Sequence);
        Assert.Equal(f.Saved.Revision, voltage.Revision);
        Assert.InRange(voltage.CompletedUtc, before, DateTimeOffset.UtcNow);
        f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.Resistance, 1000, 0.01);
        var resistance = f.Service.MeasureWithMetadata("METER", active.Lease.LeaseId);
        Assert.Equal("ohm", resistance.Unit);
        Assert.Equal(2, resistance.Sequence);
        Assert.Equal(1000, resistance.RequestedRange);
        Assert.Equal(0.01, resistance.RequestedResolution);
        Assert.Equal(IviMeasurementFunction.DcVoltage, voltage.Function);
        Assert.Null(voltage.RequestedRange);
        f.Registry.Release(active.Lease.LeaseId);
        active = f.Activate();
        Assert.Equal(1, f.Service.MeasureWithMetadata("METER", active.Lease.LeaseId).Sequence);
        f.Registry.Release(active.Lease.LeaseId);
    }

    /// <summary>Cached diagnostics identify failed phases and cleanup starts without exposing driver payloads.</summary>
    [Fact]
    public void Failure_phase_and_cleanup_progress_remain_observable()
    {
        using var f = new Fixture { FailClose = true, FailRecovery = true };
        var active = f.Activate();
        Assert.Throws<InvalidOperationException>(() => f.Registry.Release(active.Lease.LeaseId));
        var failed = f.Service.Status("METER", active.Lease.LeaseId);
        Assert.Equal("Close", failed.LastFailurePhase);
        Assert.NotEqual(Guid.Empty, failed.CorrelationId);
        Assert.NotEqual(active.Lease.LeaseId, failed.CorrelationId);
        Assert.Contains(7460, f.Log.Ids);
        Assert.Throws<InvalidOperationException>(() => f.Service.Recover("METER", active.Lease.LeaseId, true));
        Assert.Equal("Recover", f.Service.Status("METER", active.Lease.LeaseId).LastFailurePhase);
        Assert.Contains(7461, f.Log.Ids);
        f.Log.Fail = true;
        f.FailRecovery = false;
        Assert.True(f.Service.Status("METER", active.Lease.LeaseId).DiagnosticWriteFailures == 0);
        f.Service.Recover("METER", active.Lease.LeaseId, true);
        Assert.True(f.Service.LoggingFailureCount > 0);
    }

    /// <summary>Queued recovery can cancel while dispatched recovery remains owned and completes exactly once.</summary>
    /// <returns>Completion after the blocked provider recovery releases ownership.</returns>
    [Fact]
    public async Task Recovery_cancellation_never_interrupts_dispatched_reconciliation()
    {
        using var f = new Fixture { FailOpen = true };
        var failed = f.Activate();
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        using var runningCancel = new CancellationTokenSource();
        f.OnRecovery = () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        var running = Task.Run(() => f.Service.Recover("METER", failed.Lease.LeaseId, true, runningCancel.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal("Recovering", f.Service.Status("METER", failed.Lease.LeaseId).State);
            using var queuedCancel = new CancellationTokenSource();
            var queued = Task.Run(() => f.Service.Recover("METER", failed.Lease.LeaseId, true, queuedCancel.Token));
            queuedCancel.Cancel();runningCancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, f.RecoveryCalls);
            Assert.Null(f.Registry.Reserve("METER"));
        }
        finally { finish.Set(); await running; }
        Assert.NotNull(f.Registry.Reserve("METER"));
    }

    /// <summary>Concurrent reconfiguration cannot relabel a reading already in flight.</summary>
    /// <returns>Completion after the blocked reading and queued configuration finish.</returns>
    [Fact]
    public async Task Concurrent_reading_keeps_original_configuration_and_session()
    {
        using var f = new Fixture(functions: [IviMeasurementFunction.DcVoltage, IviMeasurementFunction.Resistance]);
        var active = f.Activate();
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        f.OnMeasure = () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        var readingTask = Task.Run(() => f.Service.MeasureWithMetadata("METER", active.Lease.LeaseId));
        Task? configure = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            configure = Task.Run(() => f.Service.Configure("METER", active.Lease.LeaseId, IviMeasurementFunction.Resistance));
            Assert.Equal(IviMeasurementFunction.DcVoltage, f.Service.Status("METER", active.Lease.LeaseId).Function);
        }
        finally { finish.Set(); }
        var reading = await readingTask;
        await configure!;
        Assert.Equal("V", reading.Unit);
        Assert.Equal(IviMeasurementFunction.DcVoltage, reading.Function);
        Assert.Equal(f.Service.Status("METER", active.Lease.LeaseId).CorrelationId, reading.CorrelationId);
        Assert.Equal(IviMeasurementFunction.Resistance, f.Service.Status("METER", active.Lease.LeaseId).Function);
        f.OnMeasure = null;
        var readings = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => f.Service.MeasureWithMetadata("METER", active.Lease.LeaseId))));
        Assert.Equal(Enumerable.Range(2, 24).Select(x => (long)x), readings.Select(r => r.Sequence).Order());
        Assert.All(readings, r => { Assert.Equal("ohm", r.Unit); Assert.Equal(reading.CorrelationId, r.CorrelationId); });
        f.Registry.Release(active.Lease.LeaseId);
        active = f.Activate();
        Assert.NotEqual(reading.CorrelationId, f.Service.MeasureWithMetadata("METER", active.Lease.LeaseId).CorrelationId);
        f.Registry.Release(active.Lease.LeaseId);
    }

    /// <summary>Competing recovery calls cannot replay a successfully completed provider reconciliation.</summary>
    /// <returns>Completion after all explicit recovery requests finish.</returns>
    [Fact]
    public async Task Concurrent_recovery_runs_successful_provider_once()
    {
        using var f = new Fixture { FailOpen = true };
        var failed = f.Activate();
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            Record.Exception(() => f.Service.Recover("METER", failed.Lease.LeaseId, true)))));
        Assert.Single(results.Where(e => e is null));
        Assert.All(results.Where(e => e is not null), e => Assert.True(e is UnauthorizedAccessException or InvalidOperationException));
        Assert.Equal(1, f.RecoveryCalls);
    }

    /// <summary>Nonfinite readings retain ownership and never produce a successful measurement envelope.</summary>
    /// <param name="value">Injected invalid vendor result.</param>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Nonfinite_readings_quarantine_without_replay(double value)
    {
        using var f = new Fixture { Reading = value };
        var active = f.Activate();
        Assert.Throws<InvalidOperationException>(() => f.Service.MeasureWithMetadata("METER", active.Lease.LeaseId));
        Assert.Throws<InvalidOperationException>(() => f.Service.MeasureWithMetadata("METER", active.Lease.LeaseId));
        Assert.Equal(1, f.MeasureCalls);
        Assert.Equal("Measure", f.Service.Status("METER", active.Lease.LeaseId).LastFailurePhase);
        f.Service.Recover("METER", active.Lease.LeaseId, true);
    }

    /// <summary>Durable acquisition evidence exists before any provider construction.</summary>
    [Fact]
    public void Ownership_is_recorded_before_opening()
    {
        var journal = new TestJournal();
        using var f = new Fixture(journal: journal);
        f.OnOpen = () => Assert.Equal(f.Saved.Revision, Assert.Single(journal.Read()).Revision);
        var active = f.Activate();
        Assert.True(active.Active);
        Assert.DoesNotContain(active.Lease.LeaseId.ToString(), System.Text.Json.JsonSerializer.Serialize(journal.Read()));
    }

    private sealed class TestJournal : IHostOwnershipJournal
    {
        public readonly Dictionary<string, OwnershipRecord> Records = new(StringComparer.OrdinalIgnoreCase);
        public bool FailAdd, FailRemove;
        public OwnershipRecord[] Read() => Records.Values.ToArray();
        public void Add(OwnershipRecord record) { if (FailAdd) throw new IOException("SECRET journal path"); Records.Add(record.ResourceId, record); }
        public void Remove(string resourceId, Guid recordId)
        {
            if (FailRemove) throw new IOException("SECRET journal path");
            if (!Records.TryGetValue(resourceId, out var record) || record.RecordId != recordId) throw new InvalidOperationException();
            Records.Remove(resourceId);
        }
    }

    /// <summary>Cleanup failures keep persisted ownership until provider recovery succeeds.</summary>
    [Fact]
    public void Journal_is_cleared_only_after_cleanup_or_recovery()
    {
        var journal = new TestJournal();
        using var f = new Fixture(journal: journal);
        f.OnClose = () => Assert.Single(journal.Read());
        var active = f.Activate();
        Assert.True(f.Registry.Release(active.Lease.LeaseId));
        Assert.Empty(journal.Read());
        active = f.Activate();f.FailClose = true;
        Assert.Throws<InvalidOperationException>(() => f.Registry.Release(active.Lease.LeaseId));
        Assert.Single(journal.Read());
        f.OnRecovery = () => Assert.Single(journal.Read());
        f.Service.Recover("METER", active.Lease.LeaseId, true);
        Assert.Empty(journal.Read());
    }

    /// <summary>A new registry quarantines unresolved resources and rejects old process capabilities.</summary>
    [Fact]
    public void Restart_blocks_reservation_and_old_lease_dispatch()
    {
        var journal = new TestJournal();
        using var previous = new Fixture(journal: journal);
        var old = previous.Activate();
        using var restarted = new Fixture(journal: journal);
        Assert.Null(restarted.Registry.Reserve("METER"));
        Assert.Null(restarted.Service.Activate("meter", restarted.Saved.Revision, true));
        Assert.False(restarted.Registry.Release(old.Lease.LeaseId));
        Assert.Throws<UnauthorizedAccessException>(() => restarted.Service.Measure("METER", old.Lease.LeaseId));
        Assert.Equal(0, restarted.OpenCalls);
        Assert.Single(journal.Read());
    }

    /// <summary>Unregistered orphan records remain visible and cannot be mistaken for idle hardware.</summary>
    [Fact]
    public void Startup_quarantine_is_visible_without_a_provider()
    {
        var journal = new TestJournal();
        using var previous = new Fixture(journal: journal);
        previous.Activate();
        Assert.Empty(previous.Registry.PendingRecovery());
        var restarted = new HostResourceRegistry(journal: journal);
        var record = Assert.Single(restarted.PendingRecovery());
        Assert.Equal("METER", record.ResourceId);
        Assert.Equal("RecoveryRequired", restarted.Monitor().Instruments.Single(i => i.ResourceId == "METER").Status);
        Assert.Equal("UnresolvedOwnership", restarted.List().Single(i => i.ResourceId == "METER").Backend);
        Assert.Null(restarted.Reserve("METER"));
    }

    /// <summary>Missing or renamed startup registrations cannot bypass quarantine through another canonical ID.</summary>
    [Fact]
    public void Unmapped_startup_ownership_blocks_new_physical_grants()
    {
        var journal = new TestJournal();
        journal.Add(new("OLD-METER", Guid.NewGuid(), OwnershipRecord.DigestRoutes(["TCPIP0::meter::inst0::INSTR"]), null, DateTimeOffset.UtcNow));
        using var renamed = new Fixture(journal: journal);
        Assert.Throws<InvalidOperationException>(() => renamed.Activate());
        Assert.Equal(0, renamed.OpenCalls);
        Assert.Single(journal.Read());
        Assert.NotNull(renamed.Registry.Reserve("DMM1")); // Simulator is unaffected.
    }

    /// <summary>Restart reconciliation requires a separately qualified provider and explicit current review.</summary>
    [Fact]
    public void Restart_reconciliation_requires_qualified_provider_and_confirmation()
    {
        var journal = new TestJournal();
        using var previous = new Fixture(journal: journal);previous.Activate();
        using var unsupported = new Fixture(journal: journal);
        var record = Assert.Single(unsupported.Registry.PendingRecovery());
        Assert.Throws<InvalidOperationException>(() => unsupported.Service.ReconcileRestart("METER", record.RecordId, true));
        using var restarted = new Fixture(journal: journal, restartRecovery: true);
        Assert.Throws<InvalidOperationException>(() => restarted.Service.ReconcileRestart("METER", record.RecordId, false));
        Assert.Throws<InvalidOperationException>(() => restarted.Service.ReconcileRestart("METER", Guid.NewGuid(), true));
        Assert.Equal(0, restarted.RestartCalls);
        restarted.OnRestart = () => Assert.Single(journal.Read());
        restarted.Service.ReconcileRestart("METER", record.RecordId, true);
        Assert.Empty(restarted.Registry.PendingRecovery());Assert.Empty(journal.Read());
        Assert.Equal(1, restarted.RestartCalls);Assert.Equal(0, restarted.OpenCalls);
        var active = restarted.Activate();Assert.True(active.Active);restarted.Registry.Release(active.Lease.LeaseId);
    }

    /// <summary>Changing the registered route set cannot redirect restart recovery to different hardware.</summary>
    [Fact]
    public void Restart_reconciliation_rejects_changed_route_digest()
    {
        var journal = new TestJournal();
        using var previous = new Fixture(journal: journal);previous.Activate();
        var old = Assert.Single(journal.Read());
        journal.Records["METER"] = old with { RoutesDigest = OwnershipRecord.DigestRoutes(["GPIB9::22::INSTR"]) };
        using var restarted = new Fixture(journal: journal, restartRecovery: true);
        Assert.Throws<InvalidOperationException>(() => restarted.Service.ReconcileRestart("METER", old.RecordId, true));
        Assert.Equal(0, restarted.RestartCalls);Assert.Single(journal.Read());Assert.Null(restarted.Registry.Reserve("METER"));
    }

    /// <summary>Restart recovery serializes per record while other instruments and status remain available.</summary>
    /// <returns>Completion after provider reconciliation finishes despite caller cancellation.</returns>
    [Fact]
    public async Task Restart_reconciliation_is_serialized_without_global_blocking()
    {
        var journal = new TestJournal();using var previous = new Fixture(journal: journal);previous.Activate();
        using var restarted = new Fixture(journal: journal, restartRecovery: true);
        var record = Assert.Single(restarted.Registry.PendingRecovery());
        using var entered = new ManualResetEventSlim();using var finish = new ManualResetEventSlim();
        using var runningCancel = new CancellationTokenSource();
        restarted.OnRestart = () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        var running = Task.Run(() => restarted.Service.ReconcileRestart("METER", record.RecordId, true, runningCancel.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Single(restarted.Registry.PendingRecovery());
            var other = restarted.Registry.Reserve("DMM1")!;Assert.True(restarted.Registry.Release(other.LeaseId));
            using var queuedCancel = new CancellationTokenSource();
            var queued = Task.Run(() => restarted.Service.ReconcileRestart("METER", record.RecordId, true, queuedCancel.Token));
            queuedCancel.Cancel();runningCancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Null(restarted.Registry.Reserve("METER"));
        }
        finally { finish.Set();await running; }
        Assert.Equal(1, restarted.RestartCalls);Assert.Empty(journal.Read());
    }

    /// <summary>A failed durable acknowledgment retains ownership without replaying successful provider cleanup.</summary>
    [Fact]
    public void Journal_clear_failure_retains_ownership_without_repeating_hardware()
    {
        var journal = new TestJournal();using var previous = new Fixture(journal: journal);previous.Activate();
        using var restarted = new Fixture(journal: journal, restartRecovery: true);
        var record = Assert.Single(restarted.Registry.PendingRecovery());journal.FailRemove = true;
        Assert.Throws<InvalidOperationException>(() => restarted.Service.ReconcileRestart("METER", record.RecordId, true));
        Assert.Throws<InvalidOperationException>(() => restarted.Service.ReconcileRestart("METER", record.RecordId, true));
        Assert.Equal(1, restarted.RestartCalls);Assert.Null(restarted.Registry.Reserve("METER"));
        journal.FailRemove = false;restarted.Service.ReconcileRestart("METER", record.RecordId, true);
        var active = restarted.Activate();journal.FailRemove = true;
        Assert.Throws<InvalidOperationException>(() => restarted.Registry.Release(active.Lease.LeaseId));
        Assert.Equal("JournalUpdateRequired", restarted.Service.Status("METER", active.Lease.LeaseId).State);
        Assert.Throws<InvalidOperationException>(() => restarted.Registry.Release(active.Lease.LeaseId));
        Assert.Equal(1, restarted.CloseCalls);Assert.Single(journal.Read());
        journal.FailRemove = false;Assert.True(restarted.Registry.Release(active.Lease.LeaseId));Assert.Empty(journal.Read());
    }

    /// <summary>Restart logs identify review records and outcomes without credentials or provider messages.</summary>
    [Fact]
    public void Restart_logging_reports_quarantine_and_reconciliation_without_payloads()
    {
        var journal = new TestJournal();using var previous = new Fixture(journal: journal);var active = previous.Activate();
        Assert.Contains(7470, previous.Log.Ids);
        using var restarted = new Fixture(journal: journal, restartRecovery: true) { FailRestart = true };
        var record = Assert.Single(restarted.Registry.PendingRecovery());Assert.Contains(7472, restarted.Log.Ids);
        Assert.Throws<InvalidOperationException>(() => restarted.Service.ReconcileRestart("METER", record.RecordId, true));
        Assert.Contains(7473, restarted.Log.Ids);Assert.Contains(7475, restarted.Log.Ids);Assert.Contains(7476, restarted.Log.Ids);
        restarted.FailRestart = false;restarted.Service.ReconcileRestart("METER", record.RecordId, true);
        Assert.Contains(7474, restarted.Log.Ids);
        var log = string.Join("\n", restarted.Log.Messages);
        Assert.DoesNotContain("SECRET", log);Assert.DoesNotContain(active.Lease.LeaseId.ToString(), log);
    }

    /// <summary>Operators can distinguish missing providers from records ready for explicit reconciliation.</summary>
    [Fact]
    public void Recovery_overview_explains_missing_qualification_without_io()
    {
        var journal = new TestJournal();using var previous = new Fixture(journal: journal);previous.Activate();
        using var restarted = new Fixture(journal: journal);
        var unavailable = Assert.Single(restarted.Service.RecoveryOverview());
        Assert.False(unavailable.CanReconcile);Assert.Equal("restart_provider_missing", unavailable.Code);
        var orphanRegistry = new HostResourceRegistry(journal: journal);
        var orphan = new HostedIviDmmService(orphanRegistry, restarted.Setup, [], restarted.Log);
        Assert.Equal("provider_missing", Assert.Single(orphan.RecoveryOverview()).Code);
        using var qualified = new Fixture(journal: journal, restartRecovery: true);
        Assert.True(Assert.Single(qualified.Service.RecoveryOverview()).CanReconcile);
        Assert.Equal(0, qualified.OpenCalls);Assert.Equal(0, qualified.RestartCalls);
    }

    /// <summary>No factory runs when durable acquisition cannot be acknowledged.</summary>
    [Fact]
    public void Ownership_write_failure_prevents_open_and_redacts_backend_error()
    {
        var journal = new TestJournal { FailAdd = true };using var f = new Fixture(journal: journal);
        var error = Assert.Throws<InvalidOperationException>(() => f.Activate());
        Assert.Equal(0, f.OpenCalls);Assert.Empty(journal.Read());Assert.DoesNotContain("SECRET", error.Message);
        Assert.Contains(7478, f.Log.Ids);Assert.DoesNotContain("SECRET", string.Join("\n", f.Log.Messages));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "ivi-active-" + Guid.NewGuid() + ".json");
        public readonly HostResourceRegistry Registry;
        public readonly Capture Log = new();
        public readonly BenchSetupService Setup;
        public readonly HostedIviDmmService Service;
        public readonly BenchSetup Configuration;
        public readonly SavedSetup Saved;
        public bool FailOpen, FailMeasure, FailClose, FailRecovery, FailConfigure;
        public int OpenCalls, ConfigureCalls, MeasureCalls, CloseCalls, RecoveryCalls, IdentityCalls;
        public double Reading = 4.5;
        public SetupIdentity ReturnedIdentity = new("Acme", "DMM", "SECRET-serial");
        public Action? OnMeasure, OnRecovery, OnOpen, OnClose, OnRestart;
        public int RestartCalls;
        public bool FailRestart;
        public IviDmmOpenRequest? LastOpen;
        public Fixture(bool identityReader = false, SetupIdentity? expected = null, IviMeasurementFunction[]? functions = null, IHostOwnershipJournal? journal = null, bool restartRecovery = false)
        {
            Registry = new(Log, journal);
            Setup = new(Registry, new SimulatedSetupDiscoveryProvider(Registry), _path);
            var binding = new SetupDriverBinding(new string('a', 64), "meter", "session",
                new("driver", "IviNetSoftwareModule", "Vendor.Driver, Vendor"), "TCPIP0::meter::inst0::INSTR",
                new() { ["Simulate"] = "False" }, new());
            var alternate = binding with { ResourceDescriptor = "GPIB0::22::INSTR" };
            Configuration = new(2, [], [new("METER", "Meter", "primary",
                [new("primary", "visa", "VISA", binding.ResourceDescriptor, DriverBinding: binding),
                 new("alternate", "visa", "VISA", alternate.ResourceDescriptor, DriverBinding: alternate)], true, expected)], [new("meter", "METER")]);
            Saved = Setup.Save(new(Configuration, null, true));
            var registration = new IviDmmRegistration("METER", [binding, alternate], options =>
            {
                OnOpen?.Invoke(); OpenCalls++; LastOpen = options;
                if (FailOpen) throw new InvalidOperationException("SECRET startup");
                var official = Substitute.For<global::Ivi.Dmm.IIviDmm>();
                var bridge = new OfficialDmmBridge(official, (_, _, _, _) => { ConfigureCalls++; if (FailConfigure) throw new IOException("SECRET configure"); }, _ =>
                { MeasureCalls++; OnMeasure?.Invoke(); if (FailMeasure) throw new IOException("SECRET measurement"); return Reading; },
                    _ => throw new InvalidOperationException("Reset must not be invoked"), _ => "vendor identity");
                return new(bridge, () => { OnClose?.Invoke(); CloseCalls++; if (FailClose) throw new IOException("SECRET cleanup"); });
            }, _ => { RecoveryCalls++; OnRecovery?.Invoke(); if (FailRecovery) throw new IOException("SECRET recovery"); },
                identityReader ? _ => { IdentityCalls++; return ReturnedIdentity; } : null, functions, restartRecovery ? () => { RestartCalls++; OnRestart?.Invoke(); if (FailRestart) throw new IOException("SECRET restart"); } : null);
            Service = new(Registry, Setup, [registration], Log);
        }
        public IviDmmActivation Activate(bool configure = true)
        {
            var result = Service.Activate("meter", Saved.Revision, true)!;
            if (result.Active && configure) Service.Configure("METER", result.Lease.LeaseId, IviMeasurementFunction.DcVoltage);
            return result;
        }
        public void Dispose() => File.Delete(_path);
    }

    private sealed class Capture : ILogger<HostedIviDmmService>, ILogger<HostResourceRegistry>
    {
        public bool Fail;
        public readonly List<string> Messages = [];
        public readonly List<int> Ids = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (Fail) throw new IOException("SECRET log sink"); lock (Messages) { Ids.Add(id.Id); Messages.Add(formatter(state, exception)); } }
    }

    private sealed class Controllers : ISetupDiscoveryProvider
    {
        public ValueTask<SetupDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SetupDiscovery(Guid.NewGuid().ToString("N"), [new("visa", "GPIB0", "adapter")], [], []));
    }
}
