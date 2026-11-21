using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BenchForge.Artifacts;
using BenchForge.Artifacts.Sqlite;
using BenchForge.Async;
using BenchForge.Contracts;
using BenchForge.Experiments;
using BenchForge.Generated;
using BenchForge.Host.Server.Recovery;
using BenchForge.Procedures;
using BenchForge.Simulation;
using BenchForge.Transports;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;

// Flagship end-to-end showcase of the BenchForge .NET 11 vertical stack:
// 1. Pure-C# zero-VISA SCPI-over-TCP raw socket transport (ScpiTcpSession)
// 2. Pre-dispatch safety interlocks (GuardedScpiSession + PhysicalOutputInterlockGuard)
// 3. Durable fenced command ledger & canonical dispatcher (FileCommandLedger + CanonicalCommandDispatcher)
// 4. Generated clean-room typed drivers (Keithley2450 SMU + Keysight34461A DMM)
// 5. Deterministic parallel Starlark (.star) procedure with all_of + measure_async + SHA-256 fingerprint
// 6. Zero-dependency .NET 11 Brotli-compressed SQLite evidence bundle with SHA-256 round-trip verification

var workDir = Path.Combine(Path.GetTempPath(), "benchforge-flagship-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workDir);

try
{
    Console.WriteLine("=== BenchForge .NET 11 Flagship Vertical Stack ===");
    Console.WriteLine();

    // -------------------------------------------------------------------------
    // 1. Pure-C# SCPI-over-TCP + Guarded Session + Durable Fenced Command Ledger
    // -------------------------------------------------------------------------
    var smuMock = new Keithley2450Mock();
    var dmmMock = new Keysight34461AMock();
    dmmMock.SetMockDcVoltage(1, ElectricPotential.FromVolts(3.2985));

    await using var smuServer = await LoopbackScpiTcpServer.StartAsync(smuMock);
    await using var dmmServer = await LoopbackScpiTcpServer.StartAsync(dmmMock);

    bool interlockArmed = false;
    var interlock = new PhysicalOutputInterlockGuard(() => interlockArmed, enforceOnSimulation: true);

    await using var smuTcp = await ScpiTcpSession.ConnectAsync(new ScpiTcpOptions("127.0.0.1", smuServer.Port));
    await using var dmmTcp = await ScpiTcpSession.ConnectAsync(new ScpiTcpOptions("127.0.0.1", dmmServer.Port));

    var guardedSmu = new GuardedScpiSession("SMU1", (IAsyncScpiSession)smuTcp, interlock, isPhysicalHardware: false);
    var guardedDmm = new GuardedScpiSession("DMM1", (IAsyncScpiSession)dmmTcp, interlock, isPhysicalHardware: false);

    // Demonstrate fail-closed interlock before energizing output.
    try
    {
        await guardedSmu.WriteAsync("OUTP ON");
        throw new InvalidOperationException("Interlock should have blocked OUTP ON.");
    }
    catch (OperationGuardDeniedException denied)
    {
        Console.WriteLine($"[1/5] Interlock fail-closed verified: {denied.Decision.RuleId} -> {denied.Decision.Reason}");
    }

    // Arm interlock, then drive generated clean-room Keithley2450 & Keysight34461A over live TCP sockets.
    interlockArmed = true;
    var smuDriver = new Keithley2450(guardedSmu);
    var dmmDriver = new Keysight34461A(guardedDmm);
    await smuDriver.SetSourceVoltageAsync(1, ElectricPotential.FromVolts(3.3));
    var smuSetpoint = await smuDriver.GetSourceVoltageAsync(1);
    var dmmReading = await dmmDriver.GetDcVoltageAsync(1);
    Console.WriteLine($"[2/5] Pure-C# TCP SCPI (zero VISA): Keithley2450 setpoint={smuSetpoint.Volts:F4} V, Keysight34461A read={dmmReading.Volts:F4} V");

    // Durable command ledger + fenced canonical dispatcher (BD-CMD-001..008).
    var ledgerPath = Path.Combine(workDir, "command-ledger.json");
    var activeLease = new LeaseFence(Guid.NewGuid(), HostEpoch: 1, Generation: 1);
    using var ledger = new FileCommandLedger(ledgerPath);
    using var dispatcher = new CanonicalCommandDispatcher(
        "SMU1",
        guardedSmu,
        ledger,
        (resourceId, lease) => resourceId == "SMU1" && lease == activeLease,
        interlock);

    var cmdId = Guid.NewGuid();
    var writeEnvelope = new CommandEnvelope(
        CommandEnvelope.CurrentSchemaVersion,
        cmdId,
        "flagship-corr-001",
        "SMU1",
        activeLease,
        InstrumentOperationKind.Write,
        CommandSafetyClass.AbsoluteSetpoint,
        "SOUR:VOLT 3.3",
        ExpectsResponse: false,
        MaxSafeRetries: 1);

    var receipt = await dispatcher.SubmitAsync(writeEnvelope);
    var verified = dispatcher.ConfirmPhysicalVerification(cmdId, $"DMM1 readback confirmed {dmmReading.Volts:F4} V");
    Console.WriteLine($"[3/5] Durable Command Ledger: {receipt.State} -> {verified.State} (attempts={verified.AttemptCount}, file={Path.GetFileName(ledgerPath)})");

    // -------------------------------------------------------------------------
    // 2. Deterministic Parallel Starlark (.star) Multi-Instrument Sweep
    // -------------------------------------------------------------------------
    const string starlarkSource = """
        instrument("dmm_rail")
        instrument("dmm_sense")
        configure("dmm_rail", "VOLTAGE")
        configure("dmm_sense", "RESISTANCE")

        # Launch concurrent measurements as deterministic futures; collect in deterministic order.
        readings = all_of([
            measure_async("dmm_rail", "VOLTAGE"),
            measure_async("dmm_sense", "RESISTANCE"),
        ])

        rail_v = readings[0]
        load_r = readings[1]
        record_sample("rail_voltage", rail_v)
        record_sample("load_resistance", load_r)
        verify_between(rail_v, 3.2, 3.4, "V")
        verify_between(load_r, 95.0, 105.0, "Ohm")
        wait_s(0.5)
        """;

    var parse = ProcedureParser.Parse(starlarkSource, "flagship_sweep.star");
    if (!parse.Success || parse.Document is null)
        throw new InvalidOperationException(string.Join("; ", parse.Diagnostics.Select(d => d.Message)));

    var definition = new ExperimentDefinitionBuilder()
        .Simulated<ITypedConfigurableMeasurementSource>("dmm_rail", sp =>
            new TypedMockMeter(
                new TypedMockMeasurementsBuilder().Constant(ElectricPotential.FromVolts(3.301)).Build(),
                sp.GetRequiredService<ISimulationClock>()))
        .Simulated<ITypedConfigurableMeasurementSource>("dmm_sense", sp =>
            new TypedMockMeter(
                new TypedMockMeasurementsBuilder().Constant(ElectricResistance.FromOhms(99.85)).Build(),
                sp.GetRequiredService<ISimulationClock>()))
        .Build("FlagshipBench");

    await using var session = definition.StartSimulated();
    var sink = new InMemoryExperimentResultSink();
    var runner = new ExperimentRunner(sink, Microsoft.Extensions.Logging.Abstractions.NullLogger<ExperimentRunner>.Instance);
    var procedure = new MockProcedure("FlagshipParallelSweep", parse.Document, session);
    var run = await runner.RunAsync(procedure);
    if (run.Status != ExperimentStatus.Completed)
        throw new InvalidOperationException($"Starlark sweep failed: {run.Error?.Message}");

    Console.WriteLine($"[4/5] Starlark Parallel Sweep: status={run.Status}, steps={procedure.LastRunSteps}, samples={sink.Samples.Count}, fingerprint={procedure.RunFingerprint[..16]}...");

    // -------------------------------------------------------------------------
    // 3. Zero-Dependency .NET 11 Brotli-Compressed SQLite Evidence Bundle
    // -------------------------------------------------------------------------
    var dbPath = Path.Combine(workDir, "flagship-artifacts.sqlite");
    var store = SqliteArtifactStore.Open(dbPath);
    var waveformBytes = new byte[4096 * sizeof(double)];
    for (int i = 0; i < 4096; i++)
    {
        double sample = 3.3 + 0.015 * Math.Sin(2.0 * Math.PI * i / 128.0);
        BitConverter.TryWriteBytes(waveformBytes.AsSpan(i * sizeof(double), sizeof(double)), sample);
    }

    var descriptor = new CaptureDescriptor(
        run.Id.ToString("D"),
        "acq-001",
        "Keysight34461A",
        DateTimeOffset.UtcNow,
        "application/octet-stream",
        "waveform",
        "Keysight,34461A,SIM-RAIL,1.0",
        new WaveformDescription(
            "CH1",
            "ieee754-float64-le",
            4096,
            "V",
            1e-6,
            0.0,
            1.0,
            0.0,
            JsonSerializer.Serialize(new { fingerprint = procedure.RunFingerprint, sweep = procedure.Name })));

    var artifactId = Guid.NewGuid();
    var saved = await CompressedArtifactCodec.SaveCompressedAsync(store, artifactId, descriptor, waveformBytes);
    using var restored = new MemoryStream();
    var (_, rawLen, rawSha) = await CompressedArtifactCodec.CopyDecompressedToAsync(
        store, artifactId, restored, expectedRawSha256: saved.RawSha256);

    Console.WriteLine(
        $"[5/5] Brotli SQLite Evidence: raw={saved.RawByteLength} B -> stored={saved.Record.ByteLength} B " +
        $"(ratio={saved.CompressionRatio:P1}), roundtrip={rawLen} B, sha256={rawSha[..16]}... OK");
    Console.WriteLine();
    Console.WriteLine("Flagship vertical stack passed end-to-end.");
}
finally
{
    if (Directory.Exists(workDir))
        Directory.Delete(workDir, recursive: true);
}

internal sealed class LoopbackScpiTcpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IAsyncScpiSession _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    private LoopbackScpiTcpServer(TcpListener listener, IAsyncScpiSession handler)
    {
        _listener = listener;
        _handler = handler;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public static Task<LoopbackScpiTcpServer> StartAsync(IAsyncScpiSession handler)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Task.FromResult(new LoopbackScpiTcpServer(listener, handler));
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                _ = Task.Run(() => ServeClientAsync(client), _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task ServeClientAsync(TcpClient client)
    {
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            while (!_cts.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                if (line is null) break;
                var cmd = line.Trim();
                if (cmd.Length == 0) continue;
                if (cmd.EndsWith('?'))
                {
                    string reply = await _handler.QueryAsync(cmd, _cts.Token).ConfigureAwait(false);
                    var bytes = Encoding.ASCII.GetBytes(reply + "\n");
                    await stream.WriteAsync(bytes, _cts.Token).ConfigureAwait(false);
                    await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
                }
                else
                {
                    await _handler.WriteAsync(cmd, _cts.Token).ConfigureAwait(false);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try { await _acceptLoop.ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }
}
