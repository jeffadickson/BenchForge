using System.Net;
using System.Net.Sockets;
using System.Text;
using BenchForge.Async;
using BenchForge.Contracts;
using BenchForge.Generated;
using BenchForge.Transports;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>
/// Regression tests for canonical command envelopes, durable ledgers, retry safety classes,
/// queue/cancellation bounds (BD-CMD-001..008), pure-C# SCPI-over-TCP, and pre-dispatch guards.
/// </summary>
public sealed class FlagshipCommandAndTransportTests
{
    private static readonly LeaseFence ValidLease = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, 1);

    private static CommandEnvelope Envelope(
        string command = "SOUR:VOLT 5.0",
        Guid? commandId = null,
        InstrumentOperationKind opKind = InstrumentOperationKind.Write,
        CommandSafetyClass safety = CommandSafetyClass.NonIdempotent,
        bool expectsResponse = false,
        int maxRetries = 0,
        LeaseFence? lease = null,
        int schemaVersion = CommandEnvelope.CurrentSchemaVersion,
        TimeSpan? timeout = null) =>
        new(
            schemaVersion,
            commandId ?? Guid.NewGuid(),
            "corr-test-001",
            "SMU1",
            lease ?? ValidLease,
            opKind,
            safety,
            command,
            expectsResponse,
            maxRetries,
            timeout);

    /// <summary>Verify BD-CMD-001 rejects invalid schema, newline injection, illegal retries, and stale leases before I/O.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task RejectsInvalidOrUnauthorizedEnvelopesBeforeInstrumentIo()
    {
        var session = new StubAsyncSession();
        var ledger = new InMemoryCommandLedger();
        using var dispatcher = new CanonicalCommandDispatcher("SMU1", session, ledger, (_, l) => l == ValidLease);

        var badSchema = await dispatcher.SubmitAsync(Envelope(schemaVersion: 99));
        var badNewline = await dispatcher.SubmitAsync(Envelope(command: "SOUR:VOLT 1\nOUTP ON"));
        var unsafeRetry = await dispatcher.SubmitAsync(Envelope(safety: CommandSafetyClass.NonIdempotent, maxRetries: 1));
        var outputRetryable = await dispatcher.SubmitAsync(Envelope(
            command: "OUTP ON",
            opKind: InstrumentOperationKind.OutputControl,
            safety: CommandSafetyClass.AbsoluteSetpoint));
        var badLease = await dispatcher.SubmitAsync(Envelope(lease: new LeaseFence(Guid.NewGuid(), 1, 1)));

        Assert.Equal(CommandFaultKind.ValidationFault, badSchema.FaultKind);
        Assert.Equal(CommandFaultKind.ValidationFault, badNewline.FaultKind);
        Assert.Equal(CommandFaultKind.ValidationFault, unsafeRetry.FaultKind);
        Assert.Equal(CommandFaultKind.ValidationFault, outputRetryable.FaultKind);
        Assert.Equal(CommandFaultKind.LeaseFault, badLease.FaultKind);
        Assert.Equal(0, session.CallCount);
    }

    /// <summary>Verify BD-CMD-002 prevents acceptance and wire dispatch when durable persistence fails (disk full).</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task PersistenceFailurePreventsAcceptanceAndDispatch()
    {
        var session = new StubAsyncSession();
        var ledger = new InMemoryCommandLedger
        {
            FaultInjector = _ => throw new IOException("Simulated disk full.")
        };
        using var dispatcher = new CanonicalCommandDispatcher("SMU1", session, ledger, (_, l) => l == ValidLease);

        var receipt = await dispatcher.SubmitAsync(Envelope());

        Assert.Equal(CommandExecutionState.FailedBeforeDispatch, receipt.State);
        Assert.Equal(CommandFaultKind.PersistenceFault, receipt.FaultKind);
        Assert.Equal(0, receipt.AttemptCount);
        Assert.Equal(0, session.CallCount);
    }

    /// <summary>Verify BD-CMD-003 deduplicates identical CommandId and rejects changed-payload duplicates.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task DeduplicatesSimultaneousAndReplayedCommandsAndRejectsPayloadConflict()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new StubAsyncSession
        {
            OnWrite = async (_, ct) => await gate.Task.WaitAsync(ct)
        };
        var ledger = new InMemoryCommandLedger();
        using var dispatcher = new CanonicalCommandDispatcher("SMU1", session, ledger, (_, l) => l == ValidLease);

        var cmdId = Guid.NewGuid();
        var env = Envelope("SOUR:VOLT 3.3", commandId: cmdId);
        var first = dispatcher.SubmitAsync(env);
        var duplicateConcurrent = dispatcher.SubmitAsync(env);
        gate.SetResult();

        var r1 = await first;
        var r2 = await duplicateConcurrent;
        var replay = await dispatcher.SubmitAsync(env);
        var changedPayload = await dispatcher.SubmitAsync(Envelope("SOUR:VOLT 9.9", commandId: cmdId));

        Assert.Equal(CommandExecutionState.TransportCompleted, r1.State);
        Assert.Equal(r1, r2);
        Assert.Equal(r1, replay);
        Assert.Equal(CommandFaultKind.IdempotencyConflictFault, changedPayload.FaultKind);
        Assert.Equal(CommandExecutionState.FailedBeforeDispatch, changedPayload.State);
        Assert.Equal(1, session.CallCount);
    }

    /// <summary>Verify BD-CMD-004 marks crash/persistence failure after dispatch as Uncertain and quarantines subsequent actuation.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task FailureBetweenTransportAndCompletionJournalMarksUncertainAndQuarantines()
    {
        var session = new StubAsyncSession();
        var ledger = new InMemoryCommandLedger
        {
            FaultInjector = r =>
            {
                if (r.State == CommandExecutionState.TransportCompleted)
                    throw new IOException("Simulated crash before completion commit.");
            }
        };
        using var dispatcher = new CanonicalCommandDispatcher("SMU1", session, ledger, (_, l) => l == ValidLease);

        var receipt = await dispatcher.SubmitAsync(Envelope("OUTP ON", opKind: InstrumentOperationKind.OutputControl));

        Assert.Equal(CommandExecutionState.Uncertain, receipt.State);
        Assert.Equal(CommandFaultKind.PersistenceFault, receipt.FaultKind);
        Assert.True(receipt.RequiresRecovery);
        Assert.True(ledger.RequiresRecovery("SMU1"));
        Assert.Equal(1, session.CallCount);

        ledger.FaultInjector = null;
        var blocked = await dispatcher.SubmitAsync(Envelope("SOUR:VOLT 1.0"));
        Assert.Equal(CommandExecutionState.RecoveryRequired, blocked.State);
        Assert.Equal(1, session.CallCount);

        int reconciled = ledger.ReconcileResource("SMU1", "Operator verified output safe.");
        Assert.Equal(1, reconciled);
        var afterRecovery = await dispatcher.SubmitAsync(Envelope("SOUR:VOLT 1.0"));
        Assert.Equal(CommandExecutionState.TransportCompleted, afterRecovery.State);
    }

    /// <summary>Verify BD-CMD-005 distinguishes transport completion from explicit physical verification.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task TransportCompletionIsNotPhysicalVerificationUntilExplicitlyConfirmed()
    {
        var session = new StubAsyncSession();
        var ledger = new InMemoryCommandLedger();
        using var dispatcher = new CanonicalCommandDispatcher("SMU1", session, ledger, (_, l) => l == ValidLease);

        var cmdId = Guid.NewGuid();
        var receipt = await dispatcher.SubmitAsync(Envelope("SOUR:VOLT 5.0", commandId: cmdId));

        Assert.Equal(CommandExecutionState.TransportCompleted, receipt.State);
        Assert.True(receipt.IsTransportSuccess);
        Assert.False(receipt.IsPhysicallyVerified);

        var verified = dispatcher.ConfirmPhysicalVerification(cmdId, "Readback confirmed 5.0001 V.");
        Assert.Equal(CommandExecutionState.PhysicallyVerified, verified.State);
        Assert.True(verified.IsPhysicallyVerified);
    }

    /// <summary>Verify BD-CMD-006 never retries NonIdempotent commands while bounding retries on safe classes.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task RetrySafetyClassesBlockUnsafeRetriesAndBoundSafeRetries()
    {
        var failingSession = new StubAsyncSession
        {
            OnWrite = (_, _) => throw new IOException("Transient link drop.")
        };
        var ledger = new InMemoryCommandLedger();
        using var dispatcher = new CanonicalCommandDispatcher("SMU1", failingSession, ledger, (_, l) => l == ValidLease);

        var nonIdempotent = await dispatcher.SubmitAsync(Envelope("INIT:IMM", safety: CommandSafetyClass.NonIdempotent));
        Assert.Equal(CommandExecutionState.Uncertain, nonIdempotent.State);
        Assert.Equal(CommandFaultKind.TransportFault, nonIdempotent.FaultKind);
        Assert.Equal(1, nonIdempotent.AttemptCount);

        ledger.ReconcileResource("SMU1", "Cleared for retry budget test.");

        int attempts = 0;
        var recoveringSession = new StubAsyncSession
        {
            OnQuery = (_, _) =>
            {
                attempts++;
                if (attempts < 3)
                    throw new IOException("Temporary socket reset.");
                return ValueTask.FromResult("+3.3000E+00");
            }
        };
        using var queryDispatcher = new CanonicalCommandDispatcher("SMU1", recoveringSession, ledger, (_, l) => l == ValidLease);
        var queryReceipt = await queryDispatcher.SubmitAsync(Envelope(
            "MEAS:VOLT:DC?",
            opKind: InstrumentOperationKind.Query,
            safety: CommandSafetyClass.ReadOnlyIdempotent,
            expectsResponse: true,
            maxRetries: 2));

        Assert.Equal(CommandExecutionState.TransportCompleted, queryReceipt.State);
        Assert.Equal(3, queryReceipt.AttemptCount);
        Assert.Equal("+3.3000E+00", queryReceipt.Response);
    }

    /// <summary>Verify BD-CMD-007 surfaces distinct typed faults for guard denial, timeout, and protocol errors.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task DistinctTypedFaultsPreserveCorrelationId()
    {
        var interlock = new PhysicalOutputInterlockGuard(() => false, enforceOnSimulation: true);
        var slowSession = new StubAsyncSession
        {
            OnQuery = async (cmd, ct) =>
            {
                if (cmd == "SLOW?")
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    return "0";
                }
                throw new FormatException("Malformed SCPI float.");
            }
        };
        var ledger = new InMemoryCommandLedger();
        using var dispatcher = new CanonicalCommandDispatcher("SMU1", slowSession, ledger, (_, l) => l == ValidLease, interlock);

        var denied = await dispatcher.SubmitAsync(Envelope("OUTP ON", opKind: InstrumentOperationKind.OutputControl));
        Assert.Equal(CommandFaultKind.GuardDeniedFault, denied.FaultKind);
        Assert.Equal("corr-test-001", denied.CorrelationId);

        var proto = await dispatcher.SubmitAsync(Envelope("MEAS?", opKind: InstrumentOperationKind.Query, expectsResponse: true));
        Assert.Equal(CommandFaultKind.ProtocolFault, proto.FaultKind);
        Assert.Equal("corr-test-001", proto.CorrelationId);

        ledger.ReconcileResource("SMU1", "Recovered after protocol error.");
        var timeout = await dispatcher.SubmitAsync(Envelope(
            "SLOW?",
            opKind: InstrumentOperationKind.Query,
            expectsResponse: true,
            timeout: TimeSpan.FromMilliseconds(40)));
        Assert.Equal(CommandFaultKind.TimeoutFault, timeout.FaultKind);
        Assert.Equal(CommandExecutionState.Uncertain, timeout.State);
    }

    /// <summary>Verify BD-CMD-008 bounds queue depth and distinguishes queued cancellation from in-flight cancellation.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task BoundedQueueAndCancellationDistinguishPreDispatchFromInFlight()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new StubAsyncSession
        {
            OnWrite = async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        var ledger = new InMemoryCommandLedger();
        using var dispatcher = new CanonicalCommandDispatcher("SMU1", session, ledger, (_, l) => l == ValidLease, maxQueueDepth: 2);

        using var inflightCts = new CancellationTokenSource();
        var inflightTask = dispatcher.SubmitAsync(Envelope("SOUR:VOLT 1"), inflightCts.Token);
        await entered.Task;

        using var queuedCts = new CancellationTokenSource();
        var queuedTask = dispatcher.SubmitAsync(Envelope("SOUR:VOLT 2"), queuedCts.Token);

        // Third command exceeds maxQueueDepth = 2.
        var saturated = await dispatcher.SubmitAsync(Envelope("SOUR:VOLT 3"));
        Assert.Equal(CommandFaultKind.QueueSaturatedFault, saturated.FaultKind);

        // Cancel queued command before dispatch -> CancelledBeforeDispatch.
        queuedCts.Cancel();
        var queuedReceipt = await queuedTask;
        Assert.Equal(CommandExecutionState.CancelledBeforeDispatch, queuedReceipt.State);
        Assert.Equal(0, queuedReceipt.AttemptCount);

        // Cancel in-flight command -> Uncertain (never Completed).
        inflightCts.Cancel();
        var inflightReceipt = await inflightTask;
        Assert.Equal(CommandExecutionState.Uncertain, inflightReceipt.State);
        Assert.Equal(CommandFaultKind.CancellationFault, inflightReceipt.FaultKind);
        Assert.Equal(1, inflightReceipt.AttemptCount);
    }

    /// <summary>Verify pure-C# ScpiTcpSession drives generated instruments over TCP, enforces guards, and fails closed on disconnect.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task PureCSharpScpiTcpSessionDrivesGeneratedDriversAndFailsClosedOnFault()
    {
        var mock = new Keithley2450Mock();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var serverCts = new CancellationTokenSource();
        var serverTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(serverCts.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                while (!serverCts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(serverCts.Token);
                    if (line is null) break;
                    if (line == "DROP?")
                    {
                        client.Close();
                        break;
                    }
                    if (line.EndsWith('?'))
                    {
                        var reply = await mock.QueryAsync(line, serverCts.Token);
                        var bytes = Encoding.ASCII.GetBytes(reply + "\n");
                        await stream.WriteAsync(bytes, serverCts.Token);
                        await stream.FlushAsync(serverCts.Token);
                    }
                    else
                    {
                        await mock.WriteAsync(line, serverCts.Token);
                    }
                }
            }
            catch { /* Shutdown */ }
        });

        try
        {
            await using var tcp = await ScpiTcpSession.ConnectAsync(new ScpiTcpOptions("127.0.0.1", port));
            bool armed = false;
            var guarded = new GuardedScpiSession(
                "SMU1",
                (IAsyncScpiSession)tcp,
                new PhysicalOutputInterlockGuard(() => armed, enforceOnSimulation: true));

            await Assert.ThrowsAsync<OperationGuardDeniedException>(() => guarded.WriteAsync("OUTP ON").AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => tcp.WriteAsync("SOUR:VOLT 1\nOUTP ON").AsTask());

            armed = true;
            var driver = new Keithley2450(guarded);
            await driver.SetSourceVoltageAsync(1, ElectricPotential.FromVolts(4.25));
            var readback = await driver.GetSourceVoltageAsync(1);
            Assert.Equal(4.25, readback.Volts, precision: 6);

            // Server drops connection mid-query -> session fails closed without blind retry.
            await Assert.ThrowsAsync<IOException>(() => tcp.QueryAsync("DROP?").AsTask());
            Assert.True(tcp.IsFaulted);
            await Assert.ThrowsAsync<IOException>(() => tcp.QueryAsync("SOUR:VOLT?").AsTask());
        }
        finally
        {
            await serverCts.CancelAsync();
            listener.Stop();
            await serverTask;
        }
    }

    private sealed class StubAsyncSession : IAsyncScpiSession
    {
        public int CallCount;
        public Func<string, CancellationToken, ValueTask>? OnWrite { get; init; }
        public Func<string, CancellationToken, ValueTask<string>>? OnQuery { get; init; }

        public ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            return OnWrite is not null ? OnWrite(command, cancellationToken) : ValueTask.CompletedTask;
        }

        public ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            return OnQuery is not null ? OnQuery(command, cancellationToken) : ValueTask.FromResult("0");
        }
    }
}
