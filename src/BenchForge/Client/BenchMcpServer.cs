using System.Text.Json;
using System.Text.Json.Nodes;
using BenchForge.Flight;
using BenchForge.Oscilloscopes;
using BenchForge.Outcomes;
using UnitsNet;

namespace BenchForge.Client;

/// <summary>Descriptor of one MCP (Model Context Protocol) tool exposed to AI agents over a <see cref="BenchSession"/>.</summary>
/// <param name="Name">Stable MCP tool name.</param>
/// <param name="Description">Human- and agent-readable tool description including safety semantics.</param>
/// <param name="InputSchema">JSON Schema object describing expected tool arguments.</param>
public sealed record BenchMcpToolDescriptor(string Name, string Description, object InputSchema);

/// <summary>
/// Zero-dependency JSON-RPC 2.0 Model Context Protocol (MCP) server wrapping a <see cref="BenchSession"/>.
/// Allows AI coding and test agents to discover instruments, inspect sealed fail-safe ceilings, advance simulation time,
/// analyze waveforms, and operate instruments while remaining structurally unable to relax or bypass registered Cameo hard limits.
/// </summary>
public sealed class BenchMcpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly BenchSession _session;

    /// <summary>Create an MCP JSON-RPC server bound to <paramref name="session"/>.</summary>
    /// <param name="session">Configured bench session with sealed <see cref="HardLimitRegistry"/>.</param>
    public BenchMcpServer(BenchSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>Return the machine-readable catalog of safety-bounded MCP tools exposed by this server.</summary>
    /// <returns>Ordered list of MCP tool descriptors.</returns>
    public static IReadOnlyList<BenchMcpToolDescriptor> GetToolCatalog() =>
    [
        new("bench_describe", "Inspect all registered instruments, capabilities, provenance, simulation clock status, and active fail-safe hard limits.", new { type = "object", properties = new { } }),
        new("bench_tighten_limits", "Monotonically tighten registered fail-safe hard limits on an instrument role (never allows raising ceilings).", new { type = "object", required = new[] { "role" }, properties = new { role = new { type = "string" }, maxVoltageV = new { type = "number" }, maxCurrentA = new { type = "number" }, maxPowerW = new { type = "number" }, maxTemperatureC = new { type = "number" }, minTemperatureC = new { type = "number" } } }),
        new("bench_advance_time", "Advance the canonical simulation clock by deltaSeconds and step all time-aware digital-twin physics and watchdogs.", new { type = "object", required = new[] { "deltaSeconds" }, properties = new { deltaSeconds = new { type = "number" } } }),
        new("bench_psu_set_voltage", "Program PSU/SMU output voltage in volts (strictly enforced by sealed InstrumentHardLimits).", new { type = "object", required = new[] { "role", "volts" }, properties = new { role = new { type = "string" }, volts = new { type = "number" }, channel = new { type = "integer" } } }),
        new("bench_psu_set_current_limit", "Program PSU/SMU current compliance limit in amperes (strictly enforced by sealed InstrumentHardLimits).", new { type = "object", required = new[] { "role", "amperes" }, properties = new { role = new { type = "string" }, amperes = new { type = "number" }, channel = new { type = "integer" } } }),
        new("bench_psu_set_output", "Enable or disable PSU/SMU output (requires compliance set first when policy demands it).", new { type = "object", required = new[] { "role", "enabled" }, properties = new { role = new { type = "string" }, enabled = new { type = "boolean" }, channel = new { type = "integer" } } }),
        new("bench_psu_measure", "Measure live terminal voltage (V) and current (A) from a PSU/SMU role.", new { type = "object", required = new[] { "role" }, properties = new { role = new { type = "string" }, channel = new { type = "integer" } } }),
        new("bench_dmm_measure", "Measure DC voltage (V) and resistance (Ohm) from a DMM role.", new { type = "object", required = new[] { "role" }, properties = new { role = new { type = "string" } } }),
        new("bench_thermal_set", "Program thermal chamber setpoint in degrees Celsius (enforced by sealed InstrumentHardLimits).", new { type = "object", required = new[] { "role", "celsius" }, properties = new { role = new { type = "string" }, celsius = new { type = "number" } } }),
        new("bench_thermal_measure", "Measure current temperature in degrees Celsius from a thermal controller role.", new { type = "object", required = new[] { "role" }, properties = new { role = new { type = "string" } } }),
        new("bench_scope_analyze", "Acquire a waveform from an oscilloscope role and run .NET 11 SIMD WaveformAnalyzer metrics (Min, Max, PeakToPeak, Mean, Rms, AcRms).", new { type = "object", required = new[] { "role" }, properties = new { role = new { type = "string" }, channel = new { type = "integer" } } }),
        new("bench_emergency_safe_all", "Transition every registered instrument on the bench to its safe idle state immediately.", new { type = "object", properties = new { } }),
        new("bench_seal_evidence", "Seal all recorded as-run steps into a SHA-256 FlightEvidencePackage.", new { type = "object", required = new[] { "uutSerial", "operatorPrincipal" }, properties = new { uutSerial = new { type = "string" }, operatorPrincipal = new { type = "string" } } })
    ];

    /// <summary>Process a single newline-delimited MCP JSON-RPC 2.0 request and return its JSON response.</summary>
    /// <param name="requestJson">JSON-RPC 2.0 request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Serialized JSON-RPC 2.0 response string.</returns>
    public async Task<string> HandleJsonRpcAsync(string requestJson, CancellationToken cancellationToken = default)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(requestJson);
        }
        catch (JsonException ex)
        {
            return SerializeError(null, -32700, $"Parse error: {ex.Message}");
        }

        if (root is not JsonObject obj)
            return SerializeError(null, -32600, "Invalid JSON-RPC request object.");

        JsonNode? id = obj["id"]?.DeepClone();
        string? method = obj["method"]?.GetValue<string>();

        if (string.Equals(method, "initialize", StringComparison.Ordinal))
        {
            return SerializeSuccess(id, new
            {
                protocolVersion = "2024-11-05",
                serverInfo = new { name = "BenchForge.Mcp", version = "1.0.0" },
                capabilities = new { tools = new { listChanged = false } }
            });
        }

        if (string.Equals(method, "tools/list", StringComparison.Ordinal))
        {
            return SerializeSuccess(id, new { tools = GetToolCatalog() });
        }

        if (string.Equals(method, "tools/call", StringComparison.Ordinal))
        {
            var paramsObj = obj["params"] as JsonObject;
            string? toolName = paramsObj?["name"]?.GetValue<string>();
            var args = paramsObj?["arguments"] as JsonObject ?? new JsonObject();
            if (string.IsNullOrWhiteSpace(toolName))
                return SerializeError(id, -32602, "tools/call requires params.name.");

            var (isError, payload) = await DispatchToolAsync(toolName, args, cancellationToken).ConfigureAwait(false);
            string text = JsonSerializer.Serialize(payload, JsonOptions);
            return SerializeSuccess(id, new
            {
                isError,
                content = new[] { new { type = "text", text } }
            });
        }

        return SerializeError(id, -32601, $"Unknown MCP method '{method}'.");
    }

    /// <summary>Run an interactive newline-delimited JSON-RPC 2.0 MCP loop over standard input and output streams.</summary>
    /// <param name="input">Input stream reader.</param>
    /// <param name="output">Output stream writer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion when the input stream reaches EOF or cancellation occurs.</returns>
    public async Task RunStdioAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            string reply = await HandleJsonRpcAsync(line, cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync(reply.AsMemory(), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(bool IsError, object Payload)> DispatchToolAsync(string tool, JsonObject args, CancellationToken ct)
    {
        try
        {
            switch (tool)
            {
                case "bench_describe":
                    return (false, JsonSerializer.Deserialize<JsonElement>(_session.DescribeJson()));

                case "bench_tighten_limits":
                {
                    string role = RequireString(args, "role");
                    var stricter = new InstrumentHardLimits(
                        MaxVoltage: OptionalDouble(args, "maxVoltageV") is { } mv ? ElectricPotential.FromVolts(mv) : null,
                        MaxCurrent: OptionalDouble(args, "maxCurrentA") is { } mi ? ElectricCurrent.FromAmperes(mi) : null,
                        MaxPower: OptionalDouble(args, "maxPowerW") is { } mp ? Power.FromWatts((decimal)mp) : null,
                        MinTemperature: OptionalDouble(args, "minTemperatureC") is { } nt ? Temperature.FromDegreesCelsius(nt) : null,
                        MaxTemperature: OptionalDouble(args, "maxTemperatureC") is { } mt ? Temperature.FromDegreesCelsius(mt) : null);
                    var res = _session.TryTightenHardLimits(role, stricter);
                    return FromBenchResult(res, l => new { role, maxVoltageV = l.MaxVoltage?.Volts, maxCurrentA = l.MaxCurrent?.Amperes, maxPowerW = l.MaxPower?.Watts });
                }

                case "bench_advance_time":
                {
                    double seconds = RequireDouble(args, "deltaSeconds");
                    var res = _session.AdvanceSimulationTime(TimeSpan.FromSeconds(seconds));
                    return FromBenchResult(res, status => new { elapsedSeconds = status.Elapsed.TotalSeconds, mode = status.Mode.ToString(), speed = status.Speed });
                }

                case "bench_psu_set_voltage":
                {
                    string role = RequireString(args, "role");
                    double volts = RequireDouble(args, "volts");
                    int ch = OptionalInt(args, "channel") ?? 1;
                    var psu = _session.TryGetPowerSupply(role);
                    if (psu.IsFailure) return (true, psu.Error);
                    var res = await psu.Value.SetVoltageAsync(ElectricPotential.FromVolts(volts), ch, ct).ConfigureAwait(false);
                    return FromVoidResult(res, new { role, channel = ch, setpointVolts = volts });
                }

                case "bench_psu_set_current_limit":
                {
                    string role = RequireString(args, "role");
                    double amps = RequireDouble(args, "amperes");
                    int ch = OptionalInt(args, "channel") ?? 1;
                    var psu = _session.TryGetPowerSupply(role);
                    if (psu.IsFailure) return (true, psu.Error);
                    var res = await psu.Value.SetCurrentLimitAsync(ElectricCurrent.FromAmperes(amps), ch, ct).ConfigureAwait(false);
                    return FromVoidResult(res, new { role, channel = ch, currentLimitAmperes = amps });
                }

                case "bench_psu_set_output":
                {
                    string role = RequireString(args, "role");
                    bool enabled = RequireBool(args, "enabled");
                    int ch = OptionalInt(args, "channel") ?? 1;
                    var psu = _session.TryGetPowerSupply(role);
                    if (psu.IsFailure) return (true, psu.Error);
                    var res = await psu.Value.SetOutputEnabledAsync(enabled, ch, ct).ConfigureAwait(false);
                    return FromVoidResult(res, new { role, channel = ch, outputEnabled = enabled });
                }

                case "bench_psu_measure":
                {
                    string role = RequireString(args, "role");
                    int ch = OptionalInt(args, "channel") ?? 1;
                    var psu = _session.TryGetPowerSupply(role);
                    if (psu.IsFailure) return (true, psu.Error);
                    var v = await psu.Value.MeasureVoltageAsync(ch, ct).ConfigureAwait(false);
                    if (v.IsFailure) return (true, v.Error);
                    var i = await psu.Value.MeasureCurrentAsync(ch, ct).ConfigureAwait(false);
                    if (i.IsFailure) return (true, i.Error);
                    return (false, new { role, channel = ch, volts = v.Value.Volts, amperes = i.Value.Amperes, watts = v.Value.Volts * i.Value.Amperes });
                }

                case "bench_dmm_measure":
                {
                    string role = RequireString(args, "role");
                    var dmm = _session.TryGetDmm(role);
                    if (dmm.IsFailure) return (true, dmm.Error);
                    var v = await dmm.Value.MeasureDcVoltageAsync(ct).ConfigureAwait(false);
                    if (v.IsFailure) return (true, v.Error);
                    var r = await dmm.Value.MeasureResistanceAsync(ct).ConfigureAwait(false);
                    if (r.IsFailure) return (true, r.Error);
                    return (false, new { role, dcVolts = v.Value.Volts, resistanceOhms = r.Value.Ohms });
                }

                case "bench_thermal_set":
                {
                    string role = RequireString(args, "role");
                    double c = RequireDouble(args, "celsius");
                    var th = _session.TryGetThermal(role);
                    if (th.IsFailure) return (true, th.Error);
                    var res = await th.Value.SetSetpointAsync(Temperature.FromDegreesCelsius(c), ct).ConfigureAwait(false);
                    return FromVoidResult(res, new { role, setpointCelsius = c });
                }

                case "bench_thermal_measure":
                {
                    string role = RequireString(args, "role");
                    var th = _session.TryGetThermal(role);
                    if (th.IsFailure) return (true, th.Error);
                    var t = await th.Value.MeasureTemperatureAsync(ct).ConfigureAwait(false);
                    return FromBenchResult(t, val => new { role, temperatureCelsius = val.DegreesCelsius });
                }

                case "bench_scope_analyze":
                {
                    string role = RequireString(args, "role");
                    int ch = OptionalInt(args, "channel") ?? 1;
                    var sc = _session.TryGetOscilloscope(role);
                    if (sc.IsFailure) return (true, sc.Error);
                    var wf = await sc.Value.AcquireWaveformVoltsAsync(ch, ct).ConfigureAwait(false);
                    if (wf.IsFailure) return (true, wf.Error);
                    var metrics = WaveformAnalyzer.Analyze(wf.Value);
                    return FromBenchResult(metrics, m => m);
                }

                case "bench_emergency_safe_all":
                {
                    var res = await _session.EmergencySafeAllAsync(ct).ConfigureAwait(false);
                    return FromVoidResult(res, new { safe = true });
                }

                case "bench_seal_evidence":
                {
                    string serial = RequireString(args, "uutSerial");
                    string op = RequireString(args, "operatorPrincipal");
                    var pkg = _session.SealEvidence(serial, op);
                    return (false, new { pkg.UnitUnderTestSerial, pkg.OverallPassed, sealSha256 = pkg.ManifestSha256, stepCount = pkg.Steps.Count });
                }

                default:
                    return (true, BenchError.Validation("BF-MCP-TOOL", $"Unknown MCP tool '{tool}'."));
            }
        }
        catch (ArgumentException ex)
        {
            return (true, BenchError.Validation("BF-MCP-ARGS", ex.Message));
        }
    }

    private static (bool, object) FromVoidResult(BenchResult result, object okPayload) =>
        result.IsSuccess ? (false, okPayload) : (true, result.Error);

    private static (bool, object) FromBenchResult<T>(BenchResult<T> result, Func<T, object> map) =>
        result.IsSuccess ? (false, map(result.Value)) : (true, result.Error);

    private static string RequireString(JsonObject obj, string key)
    {
        var val = obj[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(val)) throw new ArgumentException($"Missing required string argument '{key}'.");
        return val;
    }

    private static double RequireDouble(JsonObject obj, string key) =>
        obj[key]?.GetValue<double>() ?? throw new ArgumentException($"Missing required numeric argument '{key}'.");

    private static bool RequireBool(JsonObject obj, string key) =>
        obj[key]?.GetValue<bool>() ?? throw new ArgumentException($"Missing required boolean argument '{key}'.");

    private static double? OptionalDouble(JsonObject obj, string key) =>
        obj[key] is null ? null : obj[key]!.GetValue<double>();

    private static int? OptionalInt(JsonObject obj, string key) =>
        obj[key] is null ? null : obj[key]!.GetValue<int>();

    private static string SerializeSuccess(JsonNode? id, object result) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, JsonOptions);

    private static string SerializeError(JsonNode? id, int code, string message) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } }, JsonOptions);
}
