using System.Text.Json;
using BenchForge.Client;
using BenchForge.Flight;
using BenchForge.Oscilloscopes;
using UnitsNet;

namespace BenchForge.Cli;

/// <summary>
/// Command-line runner for Cameo/SysML declarative benches, Flight Acceptance HTML report generation,
/// Brotli flight-recorder trace capture, and safety-bounded MCP (Model Context Protocol) agent hosting.
/// </summary>
public static class Program
{
    /// <summary>CLI entry point supporting <c>run-cameo</c>, <c>describe-cameo</c>, <c>export-cameo-defaults</c>, <c>mcp-manifest</c>, and <c>mcp-serve</c>.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Zero on success; non-zero on invalid arguments or failed validation.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        string cmd = args[0].ToLowerInvariant();
        switch (cmd)
        {
            case "describe-cameo":
                return await DescribeCameoAsync(args).ConfigureAwait(false);
            case "run-cameo":
                return await RunCameoAsync(args).ConfigureAwait(false);
            case "export-cameo-defaults":
                return await ExportCameoDefaultsAsync(args).ConfigureAwait(false);
            case "mcp-manifest":
                Console.WriteLine(JsonSerializer.Serialize(
                    new { server = "BenchForge.Mcp", protocolVersion = "2024-11-05", tools = BenchMcpServer.GetToolCatalog() },
                    new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return 0;
            case "mcp-serve":
                return await ServeMcpAsync(args).ConfigureAwait(false);
            default:
                Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                PrintUsage();
                return 2;
        }
    }

    private static async Task<int> DescribeCameoAsync(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Usage: benchforge describe-cameo <manifest.cameo.json>");
            return 2;
        }

        string json = await File.ReadAllTextAsync(args[1]).ConfigureAwait(false);
        var res = DeclarativeBenchLoader.BuildBenchFromCameoJson(json);
        if (res.IsFailure)
        {
            Console.Error.WriteLine($"ERROR [{res.Error.Code}]: {res.Error.Message}");
            return 1;
        }

        await using var bench = res.Value;
        Console.WriteLine(bench.DescribeJson());
        return 0;
    }

    private static async Task<int> RunCameoAsync(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Usage: benchforge run-cameo <manifest.cameo.json> [--report <report.html>] [--trace <trace.bftrace.br>] [--uut <serial>]");
            return 2;
        }

        string manifestPath = args[1];
        string? reportPath = GetOption(args, "--report");
        string? tracePath = GetOption(args, "--trace");
        string uutSerial = GetOption(args, "--uut") ?? "FLIGHT-UUT-001";

        string json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
        var benchRes = DeclarativeBenchLoader.BuildBenchFromCameoJson(json);
        if (benchRes.IsFailure)
        {
            Console.Error.WriteLine($"ERROR [{benchRes.Error.Code}]: {benchRes.Error.Message}");
            return 1;
        }

        await using var bench = benchRes.Value;
        var hwReports = await bench.ProgramAllHardwareLimitsAsync().ConfigureAwait(false);
        if (hwReports.IsFailure)
        {
            Console.Error.WriteLine($"ERROR [{hwReports.Error.Code}]: {hwReports.Error.Message}");
            return 1;
        }

        bench.Recorder.Record("bench", "LoadCameoManifest", "Ok", $"Loaded {Path.GetFileName(manifestPath)} ({bench.Describe().Count} instruments)");

        // Run a safe nominal checkout across discovered roles while exercising simulation time.
        var descriptors = bench.Describe();
        var swRole = descriptors.FirstOrDefault(d => d.InstrumentClass == "SWITCH")?.Role;
        if (swRole is not null)
        {
            var conn = await bench.SwitchMatrix(swRole).ConnectAsync("BUS_A", "UUT_V").ConfigureAwait(false);
            bench.RecordStep($"Switch({swRole})", conn.IsSuccess, conn.IsSuccess ? "Closed BUS_A->UUT_V" : conn.Error.Message, conn.IsFailure ? conn.Error : null);
            bench.Recorder.Record(swRole, "Connect", conn.IsSuccess ? "Ok" : "Fault", "BUS_A->UUT_V");
        }

        var psuRole = descriptors.FirstOrDefault(d => d.InstrumentClass is "SMU" or "PSU")?.Role;
        if (psuRole is not null)
        {
            var lim = bench.HardLimits.TryGet(psuRole);
            double safeV = Math.Min(12.0, lim?.MaxVoltage?.Volts ?? 12.0);
            double safeI = Math.Min(1.5, lim?.MaxCurrent?.Amperes ?? 1.5);
            await bench.PowerSupply(psuRole).SetCurrentLimitAsync(ElectricCurrent.FromAmperes(safeI)).ConfigureAwait(false);
            await bench.PowerSupply(psuRole).SetVoltageAsync(ElectricPotential.FromVolts(safeV)).ConfigureAwait(false);
            await bench.PowerSupply(psuRole).SetOutputEnabledAsync(true).ConfigureAwait(false);
            bench.AdvanceSimulationTime(TimeSpan.FromSeconds(10));
            var mv = await bench.PowerSupply(psuRole).MeasureVoltageAsync().ConfigureAwait(false);
            var mi = await bench.PowerSupply(psuRole).MeasureCurrentAsync().ConfigureAwait(false);
            bool ok = mv.IsSuccess && mi.IsSuccess;
            string summary = ok ? $"Nominal bus {mv.Value.Volts:F3} V / {mi.Value.Amperes:F3} A" : "Measurement fault";
            bench.RecordStep($"PowerBus({psuRole})", ok, summary);
            bench.Recorder.Record(psuRole, "MeasureNominal", ok ? "Ok" : "Fault", summary);
        }

        var scopeRole = descriptors.FirstOrDefault(d => d.InstrumentClass == "SCOPE")?.Role;
        if (scopeRole is not null)
        {
            var wf = await bench.Oscilloscope(scopeRole).AcquireWaveformVoltsAsync().ConfigureAwait(false);
            var stats = wf.IsSuccess ? WaveformAnalyzer.Analyze(wf.Value) : BenchForge.Outcomes.BenchResult<WaveformMetrics>.Fail(wf.Error);
            bool ok = stats.IsSuccess;
            string summary = ok ? $"Samples={stats.Value.SampleCount}, Mean={stats.Value.Mean:F4} V, RippleAcRms={stats.Value.AcRms * 1000:F2} mV (SIMD={stats.Value.SimdAccelerated})" : stats.Error.Message;
            bench.RecordStep($"ScopeRipple({scopeRole})", ok, summary);
            bench.Recorder.Record(scopeRole, "WaveformAnalyze", ok ? "Ok" : "Fault", summary);
        }

        await bench.EmergencySafeAllAsync().ConfigureAwait(false);
        var evidence = bench.SealEvidence(uutSerial, "BenchForge.Cli");
        var traceBundle = bench.Recorder.ExportBundle();

        if (!string.IsNullOrWhiteSpace(tracePath))
        {
            var saved = await bench.Recorder.SaveBrotliAsync(tracePath).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                Console.Error.WriteLine($"ERROR [{saved.Error.Code}]: {saved.Error.Message}");
                return 1;
            }
        }

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            string html = FlightAcceptanceReportGenerator.RenderHtml(
                evidence,
                bench.HardLimits,
                hwReports.Value,
                bench.SimulationClock.Status(),
                traceBundle);
            var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(reportPath, html).ConfigureAwait(false);
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            benchName = evidence.TestName,
            uutSerial = evidence.UnitUnderTestSerial,
            overallPassed = evidence.OverallPassed,
            sealSha256 = evidence.ManifestSha256,
            stepCount = evidence.Steps.Count,
            traceEvents = traceBundle.Events.Count,
            traceHeadSha256 = traceBundle.HeadSha256,
            reportPath,
            tracePath
        }, new JsonSerializerOptions { WriteIndented = true }));
        return evidence.OverallPassed ? 0 : 1;
    }

    private static async Task<int> ServeMcpAsync(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Usage: benchforge mcp-serve <manifest.cameo.json>");
            return 2;
        }
        string json = await File.ReadAllTextAsync(args[1]).ConfigureAwait(false);
        var benchRes = DeclarativeBenchLoader.BuildBenchFromCameoJson(json);
        if (benchRes.IsFailure)
        {
            Console.Error.WriteLine($"ERROR [{benchRes.Error.Code}]: {benchRes.Error.Message}");
            return 1;
        }
        await using var bench = benchRes.Value;
        var server = new BenchMcpServer(bench);
        await server.RunStdioAsync(Console.In, Console.Out).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> ExportCameoDefaultsAsync(string[] args)
    {
        string? outPath = GetOption(args, "--out");
        string benchName = GetOption(args, "--name") ?? "benchforge-default-egse-bench";
        bool enableDigitalTwin = !args.Any(a => string.Equals(a, "--no-twin", StringComparison.OrdinalIgnoreCase));

        string json = CameoDefaultMocks.ExportDefaultManifestJson(benchName, enableDigitalTwin);
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(outPath, json).ConfigureAwait(false);
            Console.WriteLine($"Exported Cameo-with-units default mocks ({CameoDefaultMocks.CanonicalClasses.Count} blocks) to {outPath}");
        }
        else
        {
            Console.WriteLine(json);
        }
        return 0;
    }

    private static string? GetOption(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("BenchForge CLI (.NET 11)");
        Console.Error.WriteLine("  describe-cameo <manifest.cameo.json>");
        Console.Error.WriteLine("  run-cameo <manifest.cameo.json> [--report <out.html>] [--trace <out.bftrace.br>] [--uut <serial>]");
        Console.Error.WriteLine("  export-cameo-defaults [--name <benchName>] [--no-twin] [--out <file.cameo.json>]");
        Console.Error.WriteLine("  mcp-manifest");
        Console.Error.WriteLine("  mcp-serve <manifest.cameo.json>");
    }
}
