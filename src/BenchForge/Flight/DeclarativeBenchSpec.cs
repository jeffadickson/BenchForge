using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BenchForge.Client;
using BenchForge.Contracts;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using BenchForge.Simulation;
using UnitsNet;

namespace BenchForge.Flight;

/// <summary>
/// Single typed value property exported from a Cameo Systems Modeler / SysML instrument block
/// (for example, <c>maxVoltage = 28.5 V</c> or <c>maxPower = 10 dBm</c>).
/// </summary>
/// <param name="Name">SysML value property name (e.g., <c>maxVoltage</c>, <c>minTemperature</c>, <c>maxPower</c>).</param>
/// <param name="Value">Numeric value in <paramref name="Unit"/>.</param>
/// <param name="Unit">Physical unit symbol (e.g., <c>V</c>, <c>mV</c>, <c>A</c>, <c>mA</c>, <c>W</c>, <c>mW</c>, <c>dBm</c>, <c>Hz</c>, <c>kHz</c>, <c>MHz</c>, <c>GHz</c>, <c>degC</c>, <c>K</c>, <c>Ohm</c>).</param>
public sealed record CameoValueProperty(string Name, double Value, string Unit);

/// <summary>
/// SysML constraint expression attached to a Cameo instrument block (for example,
/// <c>voltage &lt;= 28.5</c>, <c>temperature &gt;= -40</c>, or <c>resistance &gt;= 2.0</c>).
/// </summary>
/// <param name="Id">Optional SysML requirement or constraint identifier.</param>
/// <param name="Expression">Simple comparison expression (<c>quantity &lt;= value</c> or <c>quantity &gt;= value</c>).</param>
/// <param name="Unit">Optional unit override when not SI base units.</param>
public sealed record CameoConstraint(string Id, string Expression, string? Unit = null);

/// <summary>
/// Optional behavioral simulation parameters declared on a Cameo / SysML mock block.
/// </summary>
/// <param name="LoadResistanceOhm">Simulated load resistance in ohms for SMU/PSU mocks.</param>
/// <param name="DefaultCurrentLimitA">Initial current compliance limit in amperes.</param>
/// <param name="NominalVoltageV">Initial simulated DC voltage for DMM mocks.</param>
/// <param name="NominalResistanceOhm">Initial simulated resistance for DMM/LCR mocks.</param>
/// <param name="InitialTemperatureC">Initial simulated temperature in Celsius for thermal mocks.</param>
/// <param name="SourceVoltageV">Simulated bus voltage in volts for electronic load mocks.</param>
/// <param name="DcOffsetV">Simulated DC offset in volts for oscilloscope mocks.</param>
/// <param name="RippleAmplitudeV">Simulated AC ripple amplitude in volts for oscilloscope mocks.</param>
/// <param name="MeasuredPowerW">Simulated RF power in watts for RF power meter mocks.</param>
/// <param name="FrequencyHz">Simulated frequency in hertz for frequency counter mocks.</param>
/// <param name="CapacitanceF">Simulated capacitance in farads for LCR mocks.</param>
/// <param name="ChannelCount">Initial channel count for DAQ mocks.</param>
/// <param name="WatchdogTimeoutSeconds">Watchdog timeout in seconds for safety interlock mocks.</param>
public sealed record CameoMockParameters(
    double? LoadResistanceOhm = null,
    double? DefaultCurrentLimitA = null,
    double? NominalVoltageV = null,
    double? NominalResistanceOhm = null,
    double? InitialTemperatureC = null,
    double? SourceVoltageV = null,
    double? DcOffsetV = null,
    double? RippleAmplitudeV = null,
    decimal? MeasuredPowerW = null,
    double? FrequencyHz = null,
    double? CapacitanceF = null,
    int? ChannelCount = null,
    double? WatchdogTimeoutSeconds = null);

/// <summary>Declarative simulation clock configuration exported from Cameo / SysML.</summary>
/// <param name="Mode">Clock mode (<c>Manual</c>, <c>Paused</c>, <c>RealTime</c>, <c>Accelerated</c>, <c>Slowed</c>).</param>
/// <param name="Speed">Logical seconds per wall second (0.001 to 1000000; default 1.0).</param>
public sealed record CameoSimulationTimeConfig(
    string Mode = "Manual",
    double Speed = 1.0);

/// <summary>Declarative closed-loop spacecraft UUT digital-twin physics block exported from Cameo / SysML.</summary>
/// <param name="Enabled">True to couple PSU, Thermal, Electronic Load, DMM, and Oscilloscope blocks onto a shared <see cref="DigitalTwinPlant"/>.</param>
/// <param name="BaseLoadResistanceOhms">Nominal UUT resistance at 25 C in ohms.</param>
/// <param name="TempCoeffPerCelsius">Fractional resistance temperature coefficient per degree Celsius.</param>
/// <param name="StandbyCurrentAmperes">Constant quiescent current draw above 3 V.</param>
/// <param name="BusSeriesResistanceOhms">Harness IR series resistance in ohms.</param>
/// <param name="SwitchingRippleVolts">Peak sinusoidal bus switching ripple in volts.</param>
/// <param name="InitialTemperatureCelsius">Initial UUT/chamber temperature in degrees Celsius.</param>
/// <param name="ThermalTimeConstantSeconds">Chamber first-order thermal time constant tau in seconds.</param>
/// <param name="SelfHeatingCelsiusPerWatt">UUT self-heating coefficient in C/W.</param>
/// <param name="DmmSwitchRole">Optional switch-matrix role gating DMM continuity to the UUT bus.</param>
/// <param name="DmmRequiredRoute">Optional closed relay route required for DMM readings (for example, <c>BUS_A->UUT_V</c>).</param>
public sealed record CameoDigitalTwinConfig(
    bool Enabled = false,
    double BaseLoadResistanceOhms = 14.0,
    double TempCoeffPerCelsius = 0.0035,
    double StandbyCurrentAmperes = 0.25,
    double BusSeriesResistanceOhms = 0.08,
    double SwitchingRippleVolts = 0.015,
    double InitialTemperatureCelsius = 25.0,
    double ThermalTimeConstantSeconds = 60.0,
    double SelfHeatingCelsiusPerWatt = 0.35,
    string? DmmSwitchRole = null,
    string? DmmRequiredRoute = null);

/// <summary>
/// Declarative instrument block exported from Cameo Systems Modeler / SysML combining role identity,
/// canonical class, fail-safe limits (value properties + constraints), and optional mock parameters.
/// </summary>
/// <param name="Role">Logical bench role identifier (for example, <c>eps_psu</c>).</param>
/// <param name="InstrumentClass">Canonical BenchForge class tag (<c>PSU</c>, <c>SMU</c>, <c>DMM</c>, <c>THERMAL</c>, <c>FGEN</c>, <c>SCOPE</c>, <c>SWITCH</c>, <c>ELOAD</c>, <c>RFSIGGEN</c>, <c>SPECAN</c>, <c>PWRMETER</c>, <c>COUNTER</c>, <c>LCR</c>, <c>VNA</c>, <c>DAQ</c>, <c>DIO</c>, <c>INTERLOCK</c>).</param>
/// <param name="Backend">Declared backend (<c>Mock</c> or <c>Physical</c>; defaults to <c>Mock</c>).</param>
/// <param name="ValueProperties">Optional SysML typed value properties declaring hard ceilings/floors.</param>
/// <param name="Constraints">Optional SysML constraint expressions monotonically intersected into hard limits.</param>
/// <param name="Mock">Optional simulation parameters when <paramref name="Backend"/> is <c>Mock</c>.</param>
/// <param name="RequireComplianceBeforeOutputOn">When true, blocks output enable prior to setting current compliance.</param>
/// <param name="AutoSafeOnViolation">When true, triggers safe state on attempted limit violation.</param>
/// <param name="PolicyNote">Optional ICD or SysML requirement traceability note.</param>
public sealed record CameoInstrumentBlock(
    string Role,
    string InstrumentClass,
    string Backend = "Mock",
    IReadOnlyList<CameoValueProperty>? ValueProperties = null,
    IReadOnlyList<CameoConstraint>? Constraints = null,
    CameoMockParameters? Mock = null,
    bool RequireComplianceBeforeOutputOn = true,
    bool AutoSafeOnViolation = false,
    string? PolicyNote = null);

/// <summary>
/// Root declarative bench specification exported from Cameo Systems Modeler / SysML JSON.
/// </summary>
/// <param name="BenchName">Logical bench or EGSE test-stand name.</param>
/// <param name="ModelVersion">Optional Cameo / SysML model revision tag.</param>
/// <param name="RequireHardLimitsForPhysical">When true, fails closed if any physical output instrument lacks hard limits.</param>
/// <param name="SimulationTime">Optional simulation time model configuration (<see cref="AdjustableSimulationClock"/>).</param>
/// <param name="DigitalTwin">Optional closed-loop coupled spacecraft UUT digital-twin physics parameters.</param>
/// <param name="Blocks">Ordered instrument blocks to instantiate and protect with fail-safe hard limits.</param>
public sealed record CameoBenchManifest(
    string BenchName,
    string? ModelVersion = null,
    bool RequireHardLimitsForPhysical = true,
    CameoSimulationTimeConfig? SimulationTime = null,
    CameoDigitalTwinConfig? DigitalTwin = null,
    IReadOnlyList<CameoInstrumentBlock>? Blocks = null);

/// <summary>Compiled declarative bench plan ready to apply to a <see cref="BenchBuilder"/>.</summary>
/// <param name="BenchName">Logical bench name.</param>
/// <param name="ModelVersion">Optional source model revision.</param>
/// <param name="RequireHardLimitsForPhysical">True when physical output instruments must have registered hard limits.</param>
/// <param name="SimulationTime">Optional simulation clock settings.</param>
/// <param name="DigitalTwin">Optional coupled digital-twin physics settings.</param>
/// <param name="Blocks">Validated instrument block definitions.</param>
/// <param name="CompiledLimits">Monotonically tightened <see cref="InstrumentHardLimits"/> per instrument role.</param>
public sealed record DeclarativeBenchPlan(
    string BenchName,
    string? ModelVersion,
    bool RequireHardLimitsForPhysical,
    CameoSimulationTimeConfig? SimulationTime,
    CameoDigitalTwinConfig? DigitalTwin,
    IReadOnlyList<CameoInstrumentBlock> Blocks,
    IReadOnlyDictionary<string, InstrumentHardLimits> CompiledLimits);

/// <summary>
/// Compiles declarative Cameo / SysML JSON bench manifests into sealed fail-safe <see cref="InstrumentHardLimits"/>,
/// canonical <see cref="AdjustableSimulationClock"/> configuration, coupled <see cref="DigitalTwinPlant"/> models,
/// and symmetric simulated bench instruments without verbose hand-written C# boilerplate.
/// </summary>
public static class DeclarativeBenchLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly Regex ConstraintRegex = new(
        @"^\s*(?<prop>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op><=|>=|<|>)\s*(?<num>[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*(?<unit>[A-Za-z/%]+)?\s*$",
        RegexOptions.Compiled);

    /// <summary>Parse and validate a Cameo / SysML JSON bench manifest into a <see cref="DeclarativeBenchPlan"/>.</summary>
    /// <param name="json">JSON manifest text exported from Cameo / SysML or authored declaratively.</param>
    /// <returns>Railroad outcome containing the compiled plan or a structured validation error.</returns>
    public static BenchResult<DeclarativeBenchPlan> LoadCameoJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return BenchResult<DeclarativeBenchPlan>.Fail(BenchError.Validation("BF-CAMEO-EMPTY", "Cameo manifest JSON must not be empty."));

        CameoBenchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CameoBenchManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return BenchResult<DeclarativeBenchPlan>.Fail(BenchError.Validation("BF-CAMEO-JSON", $"Invalid Cameo manifest JSON: {ex.Message}"));
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.BenchName))
            return BenchResult<DeclarativeBenchPlan>.Fail(BenchError.Validation("BF-CAMEO-NAME", "Cameo manifest requires a non-empty benchName."));

        var blocks = manifest.Blocks ?? Array.Empty<CameoInstrumentBlock>();
        var compiled = new Dictionary<string, InstrumentHardLimits>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Role))
                return BenchResult<DeclarativeBenchPlan>.Fail(BenchError.Validation("BF-CAMEO-ROLE", "Every Cameo instrument block requires a non-empty role."));

            var limitRes = CompileBlockLimits(block);
            if (limitRes.IsFailure)
                return BenchResult<DeclarativeBenchPlan>.Fail(limitRes.Error);

            if (limitRes.Value is not null)
                compiled[block.Role] = compiled.TryGetValue(block.Role, out var prev)
                    ? prev.TightenWith(limitRes.Value)
                    : limitRes.Value;
        }

        return BenchResult<DeclarativeBenchPlan>.Ok(new DeclarativeBenchPlan(
            manifest.BenchName,
            manifest.ModelVersion,
            manifest.RequireHardLimitsForPhysical,
            manifest.SimulationTime,
            manifest.DigitalTwin,
            blocks,
            compiled));
    }

    /// <summary>Apply a compiled <see cref="DeclarativeBenchPlan"/> onto a <see cref="BenchBuilder"/>, registering simulation clock, coupled digital twin, mocks, and fail-safe limits.</summary>
    /// <param name="builder">Target bench builder.</param>
    /// <param name="plan">Compiled declarative plan.</param>
    /// <param name="registerMocks">When true, instantiates every block whose backend is <c>Mock</c>.</param>
    /// <returns>Railroad result confirming all blocks and limits were applied.</returns>
    public static BenchResult ApplyTo(BenchBuilder builder, DeclarativeBenchPlan plan, bool registerMocks = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.RequireHardLimitsForPhysical)
            builder.RequireHardLimitsForPhysical(true);

        if (plan.SimulationTime is { } timeCfg)
        {
            try
            {
                var simClock = new AdjustableSimulationClock(speed: timeCfg.Speed);
                if (string.Equals(timeCfg.Mode, "RealTime", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(timeCfg.Mode, "Accelerated", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(timeCfg.Mode, "Slowed", StringComparison.OrdinalIgnoreCase))
                    simClock.Start();
                else if (string.Equals(timeCfg.Mode, "Paused", StringComparison.OrdinalIgnoreCase))
                    simClock.Pause();
                builder.WithSimulationClock(simClock);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BenchResult.Fail(BenchError.Validation("BF-CAMEO-TIME", $"Invalid simulationTime configuration: {ex.Message}"));
            }
        }

        DigitalTwinPlant? twin = null;
        if (plan.DigitalTwin is { Enabled: true } dt)
        {
            twin = new DigitalTwinPlant(new DigitalTwinParameters(
                dt.BaseLoadResistanceOhms,
                dt.TempCoeffPerCelsius,
                dt.StandbyCurrentAmperes,
                dt.BusSeriesResistanceOhms,
                dt.SwitchingRippleVolts,
                dt.InitialTemperatureCelsius,
                dt.ThermalTimeConstantSeconds,
                dt.SelfHeatingCelsiusPerWatt));
            builder.WithDigitalTwin(twin);
        }

        if (registerMocks)
        {
            // Instantiate switch matrices first so CoupledTwinDmm can bind to a required relay route if declared.
            var switches = new Dictionary<string, IBenchSwitchMatrix>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in plan.Blocks)
            {
                if (!string.Equals(block.Backend, "Mock", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(block.InstrumentClass.Trim(), "SWITCH", StringComparison.OrdinalIgnoreCase))
                {
                    var sw = new StatefulMockSwitchMatrix(block.Role);
                    switches[block.Role] = sw;
                    builder.AddInstrument(sw);
                }
            }

            foreach (var block in plan.Blocks)
            {
                if (!string.Equals(block.Backend, "Mock", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(block.InstrumentClass.Trim(), "SWITCH", StringComparison.OrdinalIgnoreCase)) continue;
                var addRes = AddMockBlock(builder, block, twin, plan.DigitalTwin, switches);
                if (addRes.IsFailure)
                    return addRes;
            }
        }

        foreach (var (role, limits) in plan.CompiledLimits)
        {
            try
            {
                builder.WithHardLimits(role, limits);
            }
            catch (BenchOperationException ex)
            {
                return BenchResult.Fail(ex.Error);
            }
        }

        return BenchResult.Ok();
    }

    /// <summary>Build a ready-to-run <see cref="BenchSession"/> directly from a Cameo / SysML JSON manifest.</summary>
    /// <param name="json">Cameo / SysML JSON bench manifest.</param>
    /// <param name="clock">Optional <see cref="TimeProvider"/>.</param>
    /// <returns>Railroad outcome containing the sealed <see cref="BenchSession"/>.</returns>
    public static BenchResult<BenchSession> BuildBenchFromCameoJson(string json, TimeProvider? clock = null)
    {
        var planRes = LoadCameoJson(json);
        if (planRes.IsFailure)
            return BenchResult<BenchSession>.Fail(planRes.Error);

        var builder = BenchSession.Create(planRes.Value.BenchName, clock);
        var applyRes = ApplyTo(builder, planRes.Value, registerMocks: true);
        if (applyRes.IsFailure)
            return BenchResult<BenchSession>.Fail(applyRes.Error);

        try
        {
            return BenchResult<BenchSession>.Ok(builder.Build());
        }
        catch (BenchOperationException ex)
        {
            return BenchResult<BenchSession>.Fail(ex.Error);
        }
    }

    private static BenchResult AddMockBlock(
        BenchBuilder builder,
        CameoInstrumentBlock block,
        DigitalTwinPlant? twin,
        CameoDigitalTwinConfig? twinCfg,
        Dictionary<string, IBenchSwitchMatrix> switches)
    {
        var m = block.Mock ?? new CameoMockParameters();
        switch (block.InstrumentClass.Trim().ToUpperInvariant())
        {
            case "PSU":
            case "SMU":
                if (twin is not null)
                    builder.AddInstrument(new CoupledTwinPowerSupply(block.Role, twin));
                else
                    builder.AddMockSmu(block.Role, m.LoadResistanceOhm ?? 10.0, m.DefaultCurrentLimitA ?? 0.1);
                return BenchResult.Ok();
            case "DMM":
                if (twin is not null)
                {
                    IBenchSwitchMatrix? sw = twinCfg?.DmmSwitchRole is { } swRole && switches.TryGetValue(swRole, out var found) ? found : null;
                    builder.AddInstrument(new CoupledTwinDmm(block.Role, twin, sw, twinCfg?.DmmRequiredRoute));
                }
                else
                {
                    builder.AddMockDmm(block.Role, m.NominalVoltageV ?? 0.0, m.NominalResistanceOhm ?? 100.0);
                }
                return BenchResult.Ok();
            case "THERMAL":
                if (twin is not null)
                    builder.AddInstrument(new CoupledTwinThermalController(block.Role, twin));
                else
                    builder.AddMockThermal(block.Role, m.InitialTemperatureC ?? 25.0);
                return BenchResult.Ok();
            case "FGEN":
                builder.AddMockFunctionGenerator(block.Role);
                return BenchResult.Ok();
            case "SCOPE":
                if (twin is not null)
                    builder.AddInstrument(new CoupledTwinOscilloscope(block.Role, twin));
                else
                    builder.AddMockOscilloscope(block.Role, m.DcOffsetV ?? 3.3, m.RippleAmplitudeV ?? 0.008);
                return BenchResult.Ok();
            case "ELOAD":
                if (twin is not null)
                    builder.AddInstrument(new CoupledTwinElectronicLoad(block.Role, twin));
                else
                    builder.AddMockElectronicLoad(block.Role, m.SourceVoltageV ?? 28.0);
                return BenchResult.Ok();
            case "RFSIGGEN":
                builder.AddMockRfSignalGenerator(block.Role);
                return BenchResult.Ok();
            case "SPECAN":
                builder.AddMockSpectrumAnalyzer(block.Role);
                return BenchResult.Ok();
            case "PWRMETER":
                builder.AddMockRfPowerMeter(block.Role, m.MeasuredPowerW ?? 0.001m);
                return BenchResult.Ok();
            case "COUNTER":
                builder.AddMockFrequencyCounter(block.Role, m.FrequencyHz ?? 10_000_000.0);
                return BenchResult.Ok();
            case "LCR":
                builder.AddMockLcrMeter(block.Role, m.CapacitanceF ?? 1e-6, m.NominalResistanceOhm ?? 0.05);
                return BenchResult.Ok();
            case "VNA":
                builder.AddMockVna(block.Role);
                return BenchResult.Ok();
            case "DAQ":
                builder.AddMockDaq(block.Role, m.ChannelCount ?? 8);
                return BenchResult.Ok();
            case "DIO":
                builder.AddMockDigitalIo(block.Role);
                return BenchResult.Ok();
            case "INTERLOCK":
                builder.AddMockSafetyInterlock(block.Role, m.WatchdogTimeoutSeconds is { } s ? TimeSpan.FromSeconds(s) : null);
                return BenchResult.Ok();
            default:
                return BenchResult.Fail(BenchError.Validation("BF-CAMEO-CLASS", $"Unsupported instrumentClass '{block.InstrumentClass}' on role '{block.Role}'.", block.Role));
        }
    }

    private static BenchResult<InstrumentHardLimits?> CompileBlockLimits(CameoInstrumentBlock block)
    {
        InstrumentHardLimits? acc = null;

        if (block.ValueProperties is not null)
        {
            foreach (var prop in block.ValueProperties)
            {
                var step = ParseValueProperty(block.Role, prop, block);
                if (step.IsFailure) return BenchResult<InstrumentHardLimits?>.Fail(step.Error);
                if (step.Value is not null)
                    acc = acc is null ? step.Value : acc.TightenWith(step.Value);
            }
        }

        if (block.Constraints is not null)
        {
            foreach (var constraint in block.Constraints)
            {
                var step = ParseConstraint(block.Role, constraint, block);
                if (step.IsFailure) return BenchResult<InstrumentHardLimits?>.Fail(step.Error);
                if (step.Value is not null)
                    acc = acc is null ? step.Value : acc.TightenWith(step.Value);
            }
        }

        if (acc is not null)
        {
            try
            {
                acc.Validate();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BenchResult<InstrumentHardLimits?>.Fail(BenchError.Validation("BF-CAMEO-BOUNDS", $"Invalid hard limits on '{block.Role}': {ex.Message}", block.Role));
            }
        }

        return BenchResult<InstrumentHardLimits?>.Ok(acc);
    }

    private static BenchResult<InstrumentHardLimits?> ParseValueProperty(string role, CameoValueProperty prop, CameoInstrumentBlock block)
    {
        string key = prop.Name.Trim().ToLowerInvariant().Replace("_", "");
        string u = prop.Unit.Trim();
        try
        {
            InstrumentHardLimits? partial = key switch
            {
                "maxvoltage" or "maxv" or "ovp" => Make(block, maxV: ToVolts(prop.Value, u)),
                "minvoltage" or "minv" => Make(block, minV: ToVolts(prop.Value, u)),
                "maxcurrent" or "maxi" or "ocp" => Make(block, maxI: ToAmps(prop.Value, u)),
                "mincurrent" or "mini" => Make(block, minI: ToAmps(prop.Value, u)),
                "maxpower" or "maxw" or "opp" => Make(block, maxP: ToWatts(prop.Value, u)),
                "maxfrequency" or "maxfreq" => Make(block, maxF: ToHertz(prop.Value, u)),
                "minfrequency" or "minfreq" => Make(block, minF: ToHertz(prop.Value, u)),
                "maxtemperature" or "maxtemp" => Make(block, maxT: ToCelsius(prop.Value, u)),
                "mintemperature" or "mintemp" => Make(block, minT: ToCelsius(prop.Value, u)),
                "minresistance" or "minohm" => Make(block, minR: ToOhms(prop.Value, u)),
                "maxresistance" or "maxohm" => Make(block, maxR: ToOhms(prop.Value, u)),
                _ => null
            };
            return BenchResult<InstrumentHardLimits?>.Ok(partial);
        }
        catch (ArgumentException ex)
        {
            return BenchResult<InstrumentHardLimits?>.Fail(BenchError.Validation("BF-CAMEO-UNIT", $"Role '{role}' property '{prop.Name}': {ex.Message}", role));
        }
    }

    private static BenchResult<InstrumentHardLimits?> ParseConstraint(string role, CameoConstraint constraint, CameoInstrumentBlock block)
    {
        var m = ConstraintRegex.Match(constraint.Expression ?? string.Empty);
        if (!m.Success || !double.TryParse(m.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return BenchResult<InstrumentHardLimits?>.Fail(BenchError.Validation("BF-CAMEO-EXPR", $"Cannot parse SysML constraint '{constraint.Expression}' on '{role}'.", role));

        string prop = m.Groups["prop"].Value.ToLowerInvariant();
        string op = m.Groups["op"].Value;
        bool isMax = op is "<=" or "<";
        string unit = !string.IsNullOrWhiteSpace(m.Groups["unit"].Value) ? m.Groups["unit"].Value : (constraint.Unit ?? string.Empty);

        try
        {
            InstrumentHardLimits? partial = prop switch
            {
                "voltage" or "v" or "amplitude" => isMax ? Make(block, maxV: ToVolts(value, unit)) : Make(block, minV: ToVolts(value, unit)),
                "current" or "i" => isMax ? Make(block, maxI: ToAmps(value, unit)) : Make(block, minI: ToAmps(value, unit)),
                "power" or "p" or "rfpower" when isMax => Make(block, maxP: ToWatts(value, unit)),
                "frequency" or "freq" or "f" => isMax ? Make(block, maxF: ToHertz(value, unit)) : Make(block, minF: ToHertz(value, unit)),
                "temperature" or "temp" or "t" => isMax ? Make(block, maxT: ToCelsius(value, unit)) : Make(block, minT: ToCelsius(value, unit)),
                "resistance" or "res" or "r" => isMax ? Make(block, maxR: ToOhms(value, unit)) : Make(block, minR: ToOhms(value, unit)),
                _ => null
            };
            if (partial is null)
                return BenchResult<InstrumentHardLimits?>.Fail(BenchError.Validation("BF-CAMEO-EXPR", $"Unsupported constraint quantity '{prop}' in '{constraint.Expression}' on '{role}'.", role));
            return BenchResult<InstrumentHardLimits?>.Ok(partial);
        }
        catch (ArgumentException ex)
        {
            return BenchResult<InstrumentHardLimits?>.Fail(BenchError.Validation("BF-CAMEO-UNIT", $"Role '{role}' constraint '{constraint.Id}': {ex.Message}", role));
        }
    }

    private static InstrumentHardLimits Make(
        CameoInstrumentBlock block,
        ElectricPotential? maxV = null,
        ElectricPotential? minV = null,
        ElectricCurrent? maxI = null,
        ElectricCurrent? minI = null,
        Power? maxP = null,
        Frequency? minF = null,
        Frequency? maxF = null,
        Temperature? minT = null,
        Temperature? maxT = null,
        ElectricResistance? minR = null,
        ElectricResistance? maxR = null) =>
        new(
            MaxVoltage: maxV,
            MinVoltage: minV,
            MaxCurrent: maxI,
            MinCurrent: minI,
            MaxPower: maxP,
            MinFrequency: minF,
            MaxFrequency: maxF,
            MinTemperature: minT,
            MaxTemperature: maxT,
            MinResistance: minR,
            MaxResistance: maxR,
            RequireComplianceBeforeOutputOn: block.RequireComplianceBeforeOutputOn,
            AutoSafeOnViolation: block.AutoSafeOnViolation,
            PolicyNote: block.PolicyNote);

    private static ElectricPotential ToVolts(double v, string unit) => unit.ToLowerInvariant() switch
    {
        "" or "v" or "volt" or "volts" => ElectricPotential.FromVolts(v),
        "mv" or "millivolt" or "millivolts" => ElectricPotential.FromMillivolts(v),
        "kv" or "kilovolt" => ElectricPotential.FromKilovolts(v),
        _ => throw new ArgumentException($"Unsupported voltage unit '{unit}'.")
    };

    private static ElectricCurrent ToAmps(double a, string unit) => unit.ToLowerInvariant() switch
    {
        "" or "a" or "amp" or "amps" or "ampere" or "amperes" => ElectricCurrent.FromAmperes(a),
        "ma" or "milliamp" or "milliamperes" => ElectricCurrent.FromMilliamperes(a),
        "ua" or "microamp" => ElectricCurrent.FromMicroamperes(a),
        _ => throw new ArgumentException($"Unsupported current unit '{unit}'.")
    };

    private static Power ToWatts(double p, string unit) => unit.ToLowerInvariant() switch
    {
        "" or "w" or "watt" or "watts" => Power.FromWatts((decimal)p),
        "mw" or "milliwatt" or "milliwatts" => Power.FromMilliwatts((decimal)p),
        "kw" or "kilowatt" => Power.FromKilowatts((decimal)p),
        "dbm" => Power.FromWatts((decimal)Math.Pow(10.0, (p - 30.0) / 10.0)),
        _ => throw new ArgumentException($"Unsupported power unit '{unit}'.")
    };

    private static Frequency ToHertz(double f, string unit) => unit.ToLowerInvariant() switch
    {
        "" or "hz" or "hertz" => Frequency.FromHertz(f),
        "khz" => Frequency.FromKilohertz(f),
        "mhz" => Frequency.FromMegahertz(f),
        "ghz" => Frequency.FromGigahertz(f),
        _ => throw new ArgumentException($"Unsupported frequency unit '{unit}'.")
    };

    private static Temperature ToCelsius(double t, string unit) => unit.ToLowerInvariant() switch
    {
        "" or "c" or "degc" or "celsius" => Temperature.FromDegreesCelsius(t),
        "k" or "kelvin" => Temperature.FromKelvins(t),
        _ => throw new ArgumentException($"Unsupported temperature unit '{unit}'.")
    };

    private static ElectricResistance ToOhms(double r, string unit) => unit.ToLowerInvariant() switch
    {
        "" or "ohm" or "ohms" => ElectricResistance.FromOhms(r),
        "mohm" => ElectricResistance.FromMilliohms(r),
        "kohm" => ElectricResistance.FromKiloohms(r),
        _ => throw new ArgumentException($"Unsupported resistance unit '{unit}'.")
    };
}

/// <summary>Fluent extensions applying Cameo / SysML declarative manifests directly onto <see cref="BenchBuilder"/>.</summary>
public static class DeclarativeBenchBuilderExtensions
{
    /// <summary>Load and apply a Cameo / SysML JSON manifest (mocks + monotonically tightened fail-safe hard limits) onto this builder.</summary>
    /// <param name="builder">Target bench builder.</param>
    /// <param name="cameoJson">Cameo / SysML JSON manifest text.</param>
    /// <param name="registerMocks">When true, also registers all mock blocks defined in the manifest.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="BenchOperationException">Thrown when the manifest is invalid or attempts to relax existing hard limits.</exception>
    public static BenchBuilder UseCameoManifestJson(this BenchBuilder builder, string cameoJson, bool registerMocks = true)
    {
        var plan = DeclarativeBenchLoader.LoadCameoJson(cameoJson).GetValueOrThrow();
        var apply = DeclarativeBenchLoader.ApplyTo(builder, plan, registerMocks);
        if (apply.IsFailure)
            throw new BenchOperationException(apply.Error);
        return builder;
    }
}
