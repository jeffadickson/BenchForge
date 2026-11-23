using System.Text.Json;
using BenchForge.Client;
using BenchForge.Flight;
using BenchForge.Interfaces;
using BenchForge.Oscilloscopes;
using BenchForge.Outcomes;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>
/// End-to-end flagship verification of Cameo-driven closed-loop digital-twin physics,
/// canonical <see cref="BenchForge.Simulation.AdjustableSimulationClock"/> time progression,
/// all 4 new aerospace instrument classes (VNA, DAQ, DIO, Watchdog Safety Interlock),
/// Brotli hash-chained <see cref="FlightRecorder"/> + deterministic replay,
/// self-contained HTML Flight Acceptance Reports, and safety-bounded <see cref="BenchMcpServer"/> JSON-RPC.
/// </summary>
public sealed class CameoDigitalTwinAndFlagshipTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>Verify the curated spacecraft EPS+TVAC Cameo manifest runs closed-loop time-driven digital-twin physics and watchdog auto-trip.</summary>
    /// <returns>Completion of digital-twin and watchdog assertions.</returns>
    [Fact]
    public async Task SpacecraftEpsTvacCameoManifestRunsClosedLoopDigitalTwinAndTimeDrivenWatchdog()
    {
        string path = Path.Combine(RepoRoot, "examples", "cameo", "spacecraft-eps-tvac.cameo.json");
        Assert.True(File.Exists(path), $"Expected Cameo example at {path}");

        var res = DeclarativeBenchLoader.BuildBenchFromCameoJson(await File.ReadAllTextAsync(path));
        Assert.True(res.IsSuccess, res.IsFailure ? res.Error.Message : null);
        await using var bench = res.Value;

        Assert.NotNull(bench.DigitalTwin);
        Assert.Equal(9, bench.Describe().Count);

        // 1. Energize PSU within Cameo hard limits (28.0 V tightened from 28.5 V, 28 V * 4.0 A = 112 W <= 120 W!)
        Assert.True((await bench.PowerSupply("eps_psu").SetCurrentLimitAsync(ElectricCurrent.FromAmperes(4.0))).IsSuccess);
        Assert.True((await bench.PowerSupply("eps_psu").SetVoltageAsync(ElectricPotential.FromVolts(28.0))).IsSuccess);
        Assert.True((await bench.PowerSupply("eps_psu").SetOutputEnabledAsync(true)).IsSuccess);

        // Over-limit 28.3 V fails closed because Cameo constraint voltage <= 28.0 V tightened 28.5 V!
        var overV = await bench.PowerSupply("eps_psu").SetVoltageAsync(ElectricPotential.FromVolts(28.3));
        Assert.True(overV.IsFailure);
        Assert.Equal("BF-HARDLIMIT-VOLT", overV.Error.Code);
        // AutoSafeOnViolation turned PSU off -> re-enable at 28.0 V for closed-loop test
        Assert.True((await bench.PowerSupply("eps_psu").SetVoltageAsync(ElectricPotential.FromVolts(28.0))).IsSuccess);
        Assert.True((await bench.PowerSupply("eps_psu").SetOutputEnabledAsync(true)).IsSuccess);

        // 2. Switch matrix continuity gates the coupled DMM!
        Assert.Equal(0.0, (await bench.Dmm("bus_dmm").MeasureDcVoltageAsync()).Value.Volts, 3);
        Assert.True((await bench.SwitchMatrix("harness_sw").ConnectAsync("BUS_A", "UUT_V")).IsSuccess);
        double busVNoLoad = (await bench.Dmm("bus_dmm").MeasureDcVoltageAsync()).Value.Volts;
        Assert.InRange(busVNoLoad, 27.0, 28.0);

        // 3. Engaging coupled Electronic Load draws real extra current and causes IR bus voltage droop!
        Assert.True((await bench.ElectronicLoad("bus_eload").SetCurrentSinkAsync(ElectricCurrent.FromAmperes(1.5))).IsSuccess);
        Assert.True((await bench.ElectronicLoad("bus_eload").SetInputEnabledAsync(true)).IsSuccess);
        double busVLoaded = (await bench.Dmm("bus_dmm").MeasureDcVoltageAsync()).Value.Volts;
        Assert.True(busVLoaded < busVNoLoad, $"Expected bus voltage droop under load ({busVLoaded} < {busVNoLoad}).");

        // 4. Coupled oscilloscope waveform reflects live bus voltage + switching ripple under SIMD analysis
        var wf = await bench.Oscilloscope("bus_scope").AcquireWaveformVoltsAsync();
        Assert.True(wf.IsSuccess);
        var metrics = WaveformAnalyzer.Analyze(wf.Value);
        Assert.True(metrics.IsSuccess);
        Assert.InRange(metrics.Value.Mean, 26.5, 28.0);
        Assert.True(metrics.Value.AcRms > 0.005);

        // 5. Time-driven thermal dynamics + DAQ timestamp + Watchdog auto-trip!
        Assert.True((await bench.Thermal("tvac_chamber").SetSetpointAsync(Temperature.FromDegreesCelsius(75.0))).IsSuccess);
        double t0 = (await bench.Thermal("tvac_chamber").MeasureTemperatureAsync()).Value.DegreesCelsius;

        // Kick watchdog at t=0, advance 20 s (inside 30s timeout) -> still armed, temperature rises
        Assert.True((await bench.SafetyInterlock("safety_loop").KickWatchdogAsync()).IsSuccess);
        Assert.True(bench.AdvanceSimulationTime(TimeSpan.FromSeconds(20)).IsSuccess);
        Assert.True((await bench.SafetyInterlock("safety_loop").GetStatusAsync()).Value.IsArmed);
        double t20 = (await bench.Thermal("tvac_chamber").MeasureTemperatureAsync()).Value.DegreesCelsius;
        Assert.True(t20 > t0 + 10.0, $"Expected temperature rise after 20s ({t0} -> {t20}).");

        // DAQ scan carries exact simulation elapsed timestamp (20s)
        var scan = await bench.Daq("tc_daq").ScanChannelsAsync([1, 2, 3]);
        Assert.True(scan.IsSuccess);
        Assert.Equal(TimeSpan.FromSeconds(20), scan.Value[0].SimulationElapsed);

        // Advance another 40 s WITHOUT kicking watchdog (60s > 30s timeout) -> watchdog auto-trips!
        Assert.True(bench.AdvanceSimulationTime(TimeSpan.FromSeconds(40)).IsSuccess);
        var wdStatus = (await bench.SafetyInterlock("safety_loop").GetStatusAsync()).Value;
        Assert.True(wdStatus.IsTripped);
        Assert.False(wdStatus.IsArmed);
        Assert.True((await bench.SafetyInterlock("safety_loop").KickWatchdogAsync()).IsFailure);

        // Explicit operator reset re-arms safety interlock
        Assert.True((await bench.SafetyInterlock("safety_loop").ResetAndArmAsync("Verified bench safe")).IsSuccess);
        Assert.True((await bench.SafetyInterlock("safety_loop").GetStatusAsync()).Value.IsArmed);

        // Discrete Digital I/O
        Assert.True((await bench.DigitalIo("egse_dio").WritePinAsync("ARM_CMD", true)).IsSuccess);
        Assert.True((await bench.DigitalIo("egse_dio").ReadPinAsync("ARM_CMD")).Value);
    }

    /// <summary>Verify the S-Band RF Cameo manifest enforces VNA and RF signal generator hard limits.</summary>
    /// <returns>Completion of RF payload assertions.</returns>
    [Fact]
    public async Task SbandRfPayloadCameoManifestEnforcesVnaAndRfLimits()
    {
        string path = Path.Combine(RepoRoot, "examples", "cameo", "sband-rf-payload.cameo.json");
        Assert.True(File.Exists(path));

        var res = DeclarativeBenchLoader.BuildBenchFromCameoJson(await File.ReadAllTextAsync(path));
        Assert.True(res.IsSuccess, res.IsFailure ? res.Error.Message : null);
        await using var bench = res.Value;

        // VNA enforces 0 dBm (1 mW) max power and [1.8, 2.7] GHz sweep bounds
        var vna = bench.Vna("rf_vna");
        Assert.True((await vna.SetFrequencyRangeAsync(Frequency.FromGigahertz(2.0), Frequency.FromGigahertz(2.4))).IsSuccess);
        Assert.True((await vna.SetSourcePowerAsync(Power.FromMilliwatts(0.8m))).IsSuccess);
        Assert.True((await vna.SetSourcePowerAsync(Power.FromMilliwatts(5.0m))).IsFailure);
        Assert.True((await vna.SetFrequencyRangeAsync(Frequency.FromGigahertz(1.0), Frequency.FromGigahertz(2.4))).IsFailure);

        var s21 = await vna.MeasureSParameterSweepAsync("S21", 51);
        Assert.True(s21.IsSuccess);
        Assert.Equal(51, s21.Value.Count);
    }

    /// <summary>Verify FlightRecorder Brotli persistence, deterministic replay, tamper detection, and HTML Acceptance Report rendering.</summary>
    /// <returns>Completion of flight-recorder and HTML report assertions.</returns>
    [Fact]
    public async Task FlightRecorderBrotliReplayAndHtmlAcceptanceReportWorkEndToEnd()
    {
        string path = Path.Combine(RepoRoot, "examples", "cameo", "spacecraft-eps-tvac.cameo.json");
        await using var bench = DeclarativeBenchLoader.BuildBenchFromCameoJson(await File.ReadAllTextAsync(path)).GetValueOrThrow();

        bench.Recorder.Record("eps_psu", "SetVoltage", "Ok", "28.0 V");
        bench.AdvanceSimulationTime(TimeSpan.FromSeconds(15));
        bench.Recorder.Record("eps_psu", "SetVoltage", "Denied", "35.0 V blocked", "BF-HARDLIMIT-VOLT");
        bench.RecordStep("NominalCheckout", true, "Bus 27.9 V nominal");

        var hwReports = (await bench.ProgramAllHardwareLimitsAsync()).GetValueOrThrow();
        var evidence = bench.SealEvidence("SAT-FM-001", "flight-lead");

        string tmpTrace = Path.Combine(Path.GetTempPath(), $"benchforge-trace-{Guid.NewGuid():N}.bftrace.br");
        try
        {
            var saved = await bench.Recorder.SaveBrotliAsync(tmpTrace);
            Assert.True(saved.IsSuccess && saved.Value > 0);

            var loaded = await FlightRecorder.LoadBrotliAsync(tmpTrace);
            Assert.True(loaded.IsSuccess);
            Assert.Equal(3, loaded.Value.Events.Count);

            var replay = FlightTraceReplayer.Replay(loaded.Value);
            Assert.True(replay.IsSuccess);
            Assert.Equal(3, replay.Value.EventCount);
            Assert.Equal(1, replay.Value.FaultCount);
            Assert.Equal(TimeSpan.FromSeconds(15), replay.Value.TotalSimulationElapsed);

            // Tampering with any event breaks SHA-256 verification
            var tamperedEvents = loaded.Value.Events.ToArray();
            tamperedEvents[0] = tamperedEvents[0] with { Summary = "TAMPERED" };
            var tampered = loaded.Value with { Events = tamperedEvents };
            Assert.True(FlightRecorder.VerifyBundle(tampered).IsFailure);

            // Self-contained HTML Flight Acceptance Report contains seal, limits, and BD-RES-002 boundary
            string html = FlightAcceptanceReportGenerator.RenderHtml(
                evidence,
                bench.HardLimits,
                hwReports,
                bench.SimulationClock.Status(),
                loaded.Value);
            Assert.Contains("SAT-FM-001", html, StringComparison.Ordinal);
            Assert.Contains(evidence.ManifestSha256, html, StringComparison.Ordinal);
            Assert.Contains("BD-RES-002", html, StringComparison.Ordinal);
            Assert.Contains("eps_psu", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tmpTrace)) File.Delete(tmpTrace);
        }
    }

    /// <summary>Verify safety-bounded MCP JSON-RPC 2.0 server allows discovery, time advance, and measurements while blocking over-limit AI commands.</summary>
    /// <returns>Completion of MCP server assertions.</returns>
    [Fact]
    public async Task BenchMcpServerEnforcesCameoHardLimitsOverJsonRpc()
    {
        string path = Path.Combine(RepoRoot, "examples", "cameo", "spacecraft-eps-tvac.cameo.json");
        await using var bench = DeclarativeBenchLoader.BuildBenchFromCameoJson(await File.ReadAllTextAsync(path)).GetValueOrThrow();
        var mcp = new BenchMcpServer(bench);

        // 1. Initialize & tools/list
        var initReply = JsonDocument.Parse(await mcp.HandleJsonRpcAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}"""));
        Assert.Equal("BenchForge.Mcp", initReply.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

        var listReply = JsonDocument.Parse(await mcp.HandleJsonRpcAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}"""));
        Assert.True(listReply.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength() >= 12);

        // 2. AI agent attempts 40 V on eps_psu (Cameo ceiling is 28 V) -> blocked cleanly with isError=true!
        var blockedReply = JsonDocument.Parse(await mcp.HandleJsonRpcAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"bench_psu_set_voltage","arguments":{"role":"eps_psu","volts":40.0}}}"""));
        Assert.True(blockedReply.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("BF-HARDLIMIT-VOLT", blockedReply.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!, StringComparison.Ordinal);

        // 3. AI agent programs safe 24 V + advances simulation time + runs SIMD scope analysis
        var setI = JsonDocument.Parse(await mcp.HandleJsonRpcAsync(
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"bench_psu_set_current_limit","arguments":{"role":"eps_psu","amperes":3.0}}}"""));
        Assert.False(setI.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        var setV = JsonDocument.Parse(await mcp.HandleJsonRpcAsync(
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"bench_psu_set_voltage","arguments":{"role":"eps_psu","volts":24.0}}}"""));
        Assert.False(setV.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        var setOn = JsonDocument.Parse(await mcp.HandleJsonRpcAsync(
            """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"bench_psu_set_output","arguments":{"role":"eps_psu","enabled":true}}}"""));
        Assert.False(setOn.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        var adv = JsonDocument.Parse(await mcp.HandleJsonRpcAsync(
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"bench_advance_time","arguments":{"deltaSeconds":12.5}}}"""));
        Assert.False(adv.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(12.5, bench.SimulationClock.Elapsed.TotalSeconds, 4);

        var scope = JsonDocument.Parse(await mcp.HandleJsonRpcAsync(
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"bench_scope_analyze","arguments":{"role":"bus_scope"}}}"""));
        Assert.False(scope.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("peakToPeak", scope.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!, StringComparison.Ordinal);
    }
}
