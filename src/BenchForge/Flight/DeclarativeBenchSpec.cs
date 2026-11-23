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
/// (for example, <c>maxVoltage = 28.5 V</c>, <c>maxPower = 10 dBm</c>, or mock defaults like <c>frequency = 10 MHz</c>).
/// </summary>
/// <param name="Name">SysML value property name (e.g., <c>maxVoltage</c>, <c>minTemperature</c>, <c>loadResistance</c>, <c>watchdogTimeout</c>).</param>
/// <param name="Value">Numeric value in <paramref name="Unit"/>.</param>
/// <param name="Unit">Physical unit symbol (e.g., <c>V</c>, <c>mV</c>, <c>A</c>, <c>mA</c>, <c>W</c>, <c>mW</c>, <c>dBm</c>, <c>Hz</c>, <c>kHz</c>, <c>MHz</c>, <c>GHz</c>, <c>degC</c>, <c>K</c>, <c>degF</c>, <c>Ohm</c>, <c>mOhm</c>, <c>uF</c>, <c>s</c>, <c>ms</c>).</param>
/// <param name="QuantityKind">Optional SysML QUDV quantity kind (for example, <c>ElectricPotential</c> or <c>Frequency</c>).</param>
/// <param name="ValueType">Optional SysML QUDV value type name (for example, <c>Volts</c> or <c>Megahertz</c>).</param>
public sealed record CameoValueProperty(
    string Name,
    double Value,
    string Unit,
    string? QuantityKind = null,
    string? ValueType = null);

/// <summary>
/// SysML constraint expression attached to a Cameo instrument block (for example,
/// <c>voltage &lt;= 28.5 V</c>, <c>temperature &gt;= -40 degC</c>, or <c>resistance &gt;= 2.0 Ohm</c>).
/// </summary>
/// <param name="Id">Optional SysML requirement or constraint identifier.</param>
/// <param name="Expression">Simple comparison expression (<c>quantity &lt;= value [unit]</c> or <c>quantity &gt;= value [unit]</c>).</param>
/// <param name="Unit">Optional unit override when not inline in <paramref name="Expression"/>.</param>
public sealed record CameoConstraint(string Id, string Expression, string? Unit = null);

/// <summary>
/// Optional behavioral simulation parameters declared on a Cameo / SysML mock block.
/// Prefer the unit-explicit <see cref="CameoQuantity"/> properties (accepting either <c>{ "value": 10, "unit": "MHz" }</c>
/// or <c>"10 MHz"</c>); legacy bare numeric SI properties remain supported for backward compatibility.
/// </summary>
/// <param name="LoadResistanceOhm">Legacy simulated load resistance in ohms for SMU/PSU mocks.</param>
/// <param name="DefaultCurrentLimitA">Legacy initial current compliance limit in amperes.</param>
/// <param name="NominalVoltageV">Legacy initial simulated DC voltage in volts for DMM mocks.</param>
/// <param name="NominalResistanceOhm">Legacy initial simulated resistance in ohms for DMM/LCR mocks.</param>
/// <param name="InitialTemperatureC">Legacy initial simulated temperature in Celsius for thermal mocks.</param>
/// <param name="SourceVoltageV">Legacy simulated bus voltage in volts for electronic load mocks.</param>
/// <param name="DcOffsetV">Legacy simulated DC offset in volts for oscilloscope mocks.</param>
/// <param name="RippleAmplitudeV">Legacy simulated AC ripple amplitude in volts for oscilloscope mocks.</param>
/// <param name="MeasuredPowerW">Legacy simulated RF power in watts for RF power meter mocks.</param>
/// <param name="FrequencyHz">Legacy simulated frequency in hertz for frequency counter mocks.</param>
/// <param name="CapacitanceF">Legacy simulated capacitance in farads for LCR mocks.</param>
/// <param name="ChannelCount">Initial channel count for DAQ mocks.</param>
/// <param name="WatchdogTimeoutSeconds">Legacy watchdog timeout in seconds for safety interlock mocks.</param>
/// <param name="LoadResistance">Unit-explicit simulated load resistance (for example, <c>"14 Ohm"</c> or <c>{ "value": 14, "unit": "Ohm" }</c>).</param>
/// <param name="DefaultCurrentLimit">Unit-explicit initial compliance current limit (for example, <c>"500 mA"</c>).</param>
/// <param name="NominalVoltage">Unit-explicit initial simulated DC voltage (for example, <c>"28.0 V"</c>).</param>
/// <param name="NominalResistance">Unit-explicit initial simulated resistance (for example, <c>"100 Ohm"</c> or <c>"50 mOhm"</c>).</param>
/// <param name="InitialTemperature">Unit-explicit initial simulated temperature (for example, <c>"25 degC"</c> or <c>"298.15 K"</c>).</param>
/// <param name="SourceVoltage">Unit-explicit simulated bus voltage for electronic load mocks (for example, <c>"28 V"</c>).</param>
/// <param name="DcOffset">Unit-explicit simulated DC offset for oscilloscope mocks (for example, <c>"3.3 V"</c>).</param>
/// <param name="RippleAmplitude">Unit-explicit simulated AC ripple amplitude for oscilloscope mocks (for example, <c>"12 mV"</c>).</param>
/// <param name="MeasuredPower">Unit-explicit simulated RF power (for example, <c>"7 dBm"</c> or <c>"5 mW"</c>).</param>
/// <param name="Frequency">Unit-explicit simulated frequency (for example, <c>"10 MHz"</c>).</param>
/// <param name="Capacitance">Unit-explicit simulated capacitance (for example, <c>"1 uF"</c> or <c>"470 nF"</c>).</param>
/// <param name="WatchdogTimeout">Unit-explicit watchdog timeout duration (for example, <c>"30 s"</c> or <c>"500 ms"</c>).</param>
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
    double? WatchdogTimeoutSeconds = null,
    CameoQuantity? LoadResistance = null,
    CameoQuantity? DefaultCurrentLimit = null,
    CameoQuantity? NominalVoltage = null,
    CameoQuantity? NominalResistance = null,
    CameoQuantity? InitialTemperature = null,
    CameoQuantity? SourceVoltage = null,
    CameoQuantity? DcOffset = null,
    CameoQuantity? RippleAmplitude = null,
    CameoQuantity? MeasuredPower = null,
    CameoQuantity? Frequency = null,
    CameoQuantity? Capacitance = null,
    CameoQuantity? WatchdogTimeout = null);

/// <summary>Declarative simulation clock configuration exported from Cameo / SysML.</summary>
/// <param name="Mode">Clock mode (<c>Manual</c>, <c>Paused</c>, <c>RealTime</c>, <c>Accelerated</c>, <c>Slowed</c>).</param>
/// <param name="Speed">Logical seconds per wall second (0.001 to 1000000; default 1.0).</param>
public sealed record CameoSimulationTimeConfig(
    string Mode = "Manual",
    double Speed = 1.0);

/// <summary>
/// Declarative closed-loop spacecraft UUT digital-twin physics block exported from Cameo / SysML.
/// Prefer the unit-explicit <see cref="CameoQuantity"/> properties (for example, <c>"14 Ohm"</c>, <c>"250 mA"</c>,
/// <c>"80 mOhm"</c>, <c>"12 mV"</c>, <c>"25 degC"</c>, <c>"60 s"</c>, <c>"0.25 degC/W"</c>, <c>"3500 ppm/degC"</c>);
/// legacy bare SI numeric fields remain supported for backward compatibility.
/// </summary>
/// <param name="Enabled">True to couple PSU, Thermal, Electronic Load, DMM, and Oscilloscope blocks onto a shared <see cref="DigitalTwinPlant"/>.</param>
/// <param name="BaseLoadResistanceOhms">Legacy nominal UUT resistance at 25 C in ohms.</param>
/// <param name="TempCoeffPerCelsius">Legacy fractional resistance temperature coefficient per degree Celsius.</param>
/// <param name="StandbyCurrentAmperes">Legacy constant quiescent current draw above 3 V in amperes.</param>
/// <param name="BusSeriesResistanceOhms">Legacy harness IR series resistance in ohms.</param>
/// <param name="SwitchingRippleVolts">Legacy peak sinusoidal bus switching ripple in volts.</param>
/// <param name="InitialTemperatureCelsius">Legacy initial UUT/chamber temperature in degrees Celsius.</param>
/// <param name="ThermalTimeConstantSeconds">Legacy chamber first-order thermal time constant tau in seconds.</param>
/// <param name="SelfHeatingCelsiusPerWatt">Legacy UUT self-heating coefficient in C/W.</param>
/// <param name="DmmSwitchRole">Optional switch-matrix role gating DMM continuity to the UUT bus.</param>
/// <param name="DmmRequiredRoute">Optional closed relay route required for DMM readings (for example, <c>BUS_A-&gt;UUT_V</c>).</param>
/// <param name="BaseLoadResistance">Unit-explicit nominal UUT resistance at 25 C (for example, <c>"14 Ohm"</c>).</param>
/// <param name="TempCoeff">Unit-explicit resistance temperature coefficient (for example, <c>"3500 ppm/degC"</c> or <c>"0.0035 1/degC"</c>).</param>
/// <param name="StandbyCurrent">Unit-explicit constant quiescent current draw above 3 V (for example, <c>"250 mA"</c>).</param>
/// <param name="BusSeriesResistance">Unit-explicit harness IR series resistance (for example, <c>"80 mOhm"</c>).</param>
/// <param name="SwitchingRipple">Unit-explicit peak sinusoidal bus switching ripple (for example, <c>"12 mV"</c>).</param>
/// <param name="InitialTemperature">Unit-explicit initial UUT/chamber temperature (for example, <c>"25 degC"</c> or <c>"298.15 K"</c>).</param>
/// <param name="ThermalTimeConstant">Unit-explicit chamber first-order thermal time constant tau (for example, <c>"60 s"</c> or <c>"1 min"</c>).</param>
/// <param name="SelfHeating">Unit-explicit UUT self-heating thermal resistance (for example, <c>"0.25 degC/W"</c>).</param>
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
    string? DmmRequiredRoute = null,
    CameoQuantity? BaseLoadResistance = null,
    CameoQuantity? TempCoeff = null,
    CameoQuantity? StandbyCurrent = null,
    CameoQuantity? BusSeriesResistance = null,
    CameoQuantity? SwitchingRipple = null,
    CameoQuantity? InitialTemperature = null,
    CameoQuantity? ThermalTimeConstant = null,
    CameoQuantity? SelfHeating = null);

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
        @"^\s*(?<prop>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op><=|>=|<|>)\s*(?<num>[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*(?<unit>[A-Za-z°µΩ/%][A-Za-z0-9°µΩ/%._^-]*)?\s*$",
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

        if (manifest is null)
            return BenchResult<DeclarativeBenchPlan>.Fail(BenchError.Validation("BF-CAMEO-NAME", "Cameo manifest requires a non-empty benchName."));

        return CompileManifest(manifest);
    }

    /// <summary>Validate and compile an in-memory <see cref="CameoBenchManifest"/> into a <see cref="DeclarativeBenchPlan"/>.</summary>
    /// <param name="manifest">Typed Cameo / SysML bench manifest.</param>
    /// <returns>Railroad outcome containing the compiled plan or a structured validation error.</returns>
    public static BenchResult<DeclarativeBenchPlan> CompileManifest(CameoBenchManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.BenchName))
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
            var twinParamsRes = ResolveDigitalTwinParameters(dt);
            if (twinParamsRes.IsFailure)
                return BenchResult.Fail(twinParamsRes.Error);

            twin = new DigitalTwinPlant(twinParamsRes.Value);
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

        return BuildBenchFromPlan(planRes.Value, clock);
    }

    /// <summary>Build a ready-to-run <see cref="BenchSession"/> directly from an in-memory <see cref="CameoBenchManifest"/>.</summary>
    /// <param name="manifest">Typed Cameo / SysML bench manifest.</param>
    /// <param name="clock">Optional <see cref="TimeProvider"/>.</param>
    /// <returns>Railroad outcome containing the sealed <see cref="BenchSession"/>.</returns>
    public static BenchResult<BenchSession> BuildBenchFromManifest(CameoBenchManifest manifest, TimeProvider? clock = null)
    {
        var planRes = CompileManifest(manifest);
        if (planRes.IsFailure)
            return BenchResult<BenchSession>.Fail(planRes.Error);

        return BuildBenchFromPlan(planRes.Value, clock);
    }

    internal static BenchResult ApplySingleBlock(BenchBuilder builder, CameoInstrumentBlock block)
    {
        var planRes = CompileManifest(new CameoBenchManifest(
            BenchName: "single-block",
            RequireHardLimitsForPhysical: false,
            Blocks: [block]));
        if (planRes.IsFailure)
            return BenchResult.Fail(planRes.Error);

        return ApplyTo(builder, planRes.Value, registerMocks: true);
    }

    private static BenchResult<BenchSession> BuildBenchFromPlan(DeclarativeBenchPlan plan, TimeProvider? clock)
    {
        var builder = BenchSession.Create(plan.BenchName, clock);
        var applyRes = ApplyTo(builder, plan, registerMocks: true);
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

    private static BenchResult<DigitalTwinParameters> ResolveDigitalTwinParameters(CameoDigitalTwinConfig dt)
    {
        try
        {
            double baseLoadOhms = dt.BaseLoadResistance is not null
                ? CameoUnitConverter.ToElectricResistance(dt.BaseLoadResistance).Ohms
                : dt.BaseLoadResistanceOhms;
            double tempCoeff = dt.TempCoeff is not null
                ? CameoUnitConverter.ToPerCelsius(dt.TempCoeff)
                : dt.TempCoeffPerCelsius;
            double standbyAmps = dt.StandbyCurrent is not null
                ? CameoUnitConverter.ToElectricCurrent(dt.StandbyCurrent).Amperes
                : dt.StandbyCurrentAmperes;
            double busSeriesOhms = dt.BusSeriesResistance is not null
                ? CameoUnitConverter.ToElectricResistance(dt.BusSeriesResistance).Ohms
                : dt.BusSeriesResistanceOhms;
            double rippleVolts = dt.SwitchingRipple is not null
                ? CameoUnitConverter.ToElectricPotential(dt.SwitchingRipple).Volts
                : dt.SwitchingRippleVolts;
            double initTempC = dt.InitialTemperature is not null
                ? CameoUnitConverter.ToTemperature(dt.InitialTemperature).DegreesCelsius
                : dt.InitialTemperatureCelsius;
            double tauSeconds = dt.ThermalTimeConstant is not null
                ? CameoUnitConverter.ToDuration(dt.ThermalTimeConstant).TotalSeconds
                : dt.ThermalTimeConstantSeconds;
            double selfHeating = dt.SelfHeating is not null
                ? CameoUnitConverter.ToCelsiusPerWatt(dt.SelfHeating)
                : dt.SelfHeatingCelsiusPerWatt;

            return BenchResult<DigitalTwinParameters>.Ok(new DigitalTwinParameters(
                baseLoadOhms,
                tempCoeff,
                standbyAmps,
                busSeriesOhms,
                rippleVolts,
                initTempC,
                tauSeconds,
                selfHeating));
        }
        catch (ArgumentException ex)
        {
            return BenchResult<DigitalTwinParameters>.Fail(BenchError.Validation("BF-CAMEO-UNIT", $"Invalid digitalTwin unit: {ex.Message}"));
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
        var vpLookup = BuildValuePropertyLookup(block.ValueProperties);

        try
        {
            switch (block.InstrumentClass.Trim().ToUpperInvariant())
            {
                case "PSU":
                case "SMU":
                    if (twin is not null)
                    {
                        builder.AddInstrument(new CoupledTwinPowerSupply(block.Role, twin));
                    }
                    else
                    {
                        var loadR = ResolveResistance(m.LoadResistance, vpLookup, ["loadresistance", "loadohm"], m.LoadResistanceOhm, 10.0);
                        var defaultI = ResolveCurrent(m.DefaultCurrentLimit, vpLookup, ["defaultcurrentlimit", "defaultcompliance", "initialcurrentlimit"], m.DefaultCurrentLimitA, 0.1);
                        builder.AddMockSmu(block.Role, loadR, defaultI);
                    }
                    return BenchResult.Ok();
                case "DMM":
                    if (twin is not null)
                    {
                        IBenchSwitchMatrix? sw = twinCfg?.DmmSwitchRole is { } swRole && switches.TryGetValue(swRole, out var found) ? found : null;
                        builder.AddInstrument(new CoupledTwinDmm(block.Role, twin, sw, twinCfg?.DmmRequiredRoute));
                    }
                    else
                    {
                        var nomV = ResolveVoltage(m.NominalVoltage, vpLookup, ["nominalvoltage", "initialvoltage", "dcvoltage"], m.NominalVoltageV, 0.0);
                        var nomR = ResolveResistance(m.NominalResistance, vpLookup, ["nominalresistance", "initialresistance", "resistance"], m.NominalResistanceOhm, 100.0);
                        builder.AddMockDmm(block.Role, nomV, nomR);
                    }
                    return BenchResult.Ok();
                case "THERMAL":
                    if (twin is not null)
                    {
                        builder.AddInstrument(new CoupledTwinThermalController(block.Role, twin));
                    }
                    else
                    {
                        var initT = ResolveTemperature(m.InitialTemperature, vpLookup, ["initialtemperature", "nominaltemperature", "ambienttemperature"], m.InitialTemperatureC, 25.0);
                        builder.AddMockThermal(block.Role, initT);
                    }
                    return BenchResult.Ok();
                case "FGEN":
                    builder.AddMockFunctionGenerator(block.Role);
                    return BenchResult.Ok();
                case "SCOPE":
                    if (twin is not null)
                    {
                        builder.AddInstrument(new CoupledTwinOscilloscope(block.Role, twin));
                    }
                    else
                    {
                        var dcOffset = ResolveVoltage(m.DcOffset, vpLookup, ["dcoffset", "nominaloffset"], m.DcOffsetV, 3.3);
                        var ripple = ResolveVoltage(m.RippleAmplitude, vpLookup, ["rippleamplitude", "acripple"], m.RippleAmplitudeV, 0.008);
                        builder.AddMockOscilloscope(block.Role, dcOffset, ripple);
                    }
                    return BenchResult.Ok();
                case "ELOAD":
                    if (twin is not null)
                    {
                        builder.AddInstrument(new CoupledTwinElectronicLoad(block.Role, twin));
                    }
                    else
                    {
                        var srcV = ResolveVoltage(m.SourceVoltage, vpLookup, ["sourcevoltage", "busvoltage"], m.SourceVoltageV, 28.0);
                        builder.AddMockElectronicLoad(block.Role, srcV);
                    }
                    return BenchResult.Ok();
                case "RFSIGGEN":
                    builder.AddMockRfSignalGenerator(block.Role);
                    return BenchResult.Ok();
                case "SPECAN":
                    builder.AddMockSpectrumAnalyzer(block.Role);
                    return BenchResult.Ok();
                case "PWRMETER":
                    var pwr = ResolvePower(m.MeasuredPower, vpLookup, ["measuredpower", "nominalpower", "rfpower"], m.MeasuredPowerW, 0.001m);
                    builder.AddMockRfPowerMeter(block.Role, pwr);
                    return BenchResult.Ok();
                case "COUNTER":
                    var freq = ResolveFrequency(m.Frequency, vpLookup, ["frequency", "nominalfrequency", "referencefrequency"], m.FrequencyHz, 10_000_000.0);
                    builder.AddMockFrequencyCounter(block.Role, freq);
                    return BenchResult.Ok();
                case "LCR":
                    var cap = ResolveCapacitance(m.Capacitance, vpLookup, ["capacitance", "nominalcapacitance"], m.CapacitanceF, 1e-6);
                    var esr = ResolveResistance(m.NominalResistance, vpLookup, ["nominalresistance", "esr", "seriesresistance"], m.NominalResistanceOhm, 0.05);
                    builder.AddMockLcrMeter(block.Role, cap, esr);
                    return BenchResult.Ok();
                case "VNA":
                    builder.AddMockVna(block.Role);
                    return BenchResult.Ok();
                case "DAQ":
                    int channels = m.ChannelCount ?? (vpLookup.TryGetValue("channelcount", out var chProp) ? (int)Math.Round(chProp.Value) : 8);
                    builder.AddMockDaq(block.Role, channels);
                    return BenchResult.Ok();
                case "DIO":
                    builder.AddMockDigitalIo(block.Role);
                    return BenchResult.Ok();
                case "INTERLOCK":
                    TimeSpan? wd = ResolveOptionalDuration(m.WatchdogTimeout, vpLookup, ["watchdogtimeout", "deadmantimeout"], m.WatchdogTimeoutSeconds);
                    builder.AddMockSafetyInterlock(block.Role, wd);
                    return BenchResult.Ok();
                default:
                    return BenchResult.Fail(BenchError.Validation("BF-CAMEO-CLASS", $"Unsupported instrumentClass '{block.InstrumentClass}' on role '{block.Role}'.", block.Role));
            }
        }
        catch (ArgumentException ex)
        {
            return BenchResult.Fail(BenchError.Validation("BF-CAMEO-UNIT", $"Role '{block.Role}' mock parameter unit error: {ex.Message}", block.Role));
        }
    }

    private static Dictionary<string, CameoValueProperty> BuildValuePropertyLookup(IReadOnlyList<CameoValueProperty>? props)
    {
        var map = new Dictionary<string, CameoValueProperty>(StringComparer.OrdinalIgnoreCase);
        if (props is null) return map;
        foreach (var p in props)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            string key = p.Name.Trim().ToLowerInvariant().Replace("_", "");
            map[key] = p;
        }
        return map;
    }

    private static CameoValueProperty? FindProperty(Dictionary<string, CameoValueProperty> map, string[] keys)
    {
        foreach (string k in keys)
            if (map.TryGetValue(k, out var found))
                return found;
        return null;
    }

    private static ElectricPotential ResolveVoltage(CameoQuantity? q, Dictionary<string, CameoValueProperty> vps, string[] keys, double? legacyVolts, double fallbackVolts)
    {
        if (q is not null) return CameoUnitConverter.ToElectricPotential(q);
        if (FindProperty(vps, keys) is { } vp) return CameoUnitConverter.ToElectricPotential(vp.Value, vp.Unit, allowEmptyAsSi: false);
        return ElectricPotential.FromVolts(legacyVolts ?? fallbackVolts);
    }

    private static ElectricCurrent ResolveCurrent(CameoQuantity? q, Dictionary<string, CameoValueProperty> vps, string[] keys, double? legacyAmps, double fallbackAmps)
    {
        if (q is not null) return CameoUnitConverter.ToElectricCurrent(q);
        if (FindProperty(vps, keys) is { } vp) return CameoUnitConverter.ToElectricCurrent(vp.Value, vp.Unit, allowEmptyAsSi: false);
        return ElectricCurrent.FromAmperes(legacyAmps ?? fallbackAmps);
    }

    private static ElectricResistance ResolveResistance(CameoQuantity? q, Dictionary<string, CameoValueProperty> vps, string[] keys, double? legacyOhms, double fallbackOhms)
    {
        if (q is not null) return CameoUnitConverter.ToElectricResistance(q);
        if (FindProperty(vps, keys) is { } vp) return CameoUnitConverter.ToElectricResistance(vp.Value, vp.Unit, allowEmptyAsSi: false);
        return ElectricResistance.FromOhms(legacyOhms ?? fallbackOhms);
    }

    private static Temperature ResolveTemperature(CameoQuantity? q, Dictionary<string, CameoValueProperty> vps, string[] keys, double? legacyCelsius, double fallbackCelsius)
    {
        if (q is not null) return CameoUnitConverter.ToTemperature(q);
        if (FindProperty(vps, keys) is { } vp) return CameoUnitConverter.ToTemperature(vp.Value, vp.Unit, allowEmptyAsSi: false);
        return Temperature.FromDegreesCelsius(legacyCelsius ?? fallbackCelsius);
    }

    private static Power ResolvePower(CameoQuantity? q, Dictionary<string, CameoValueProperty> vps, string[] keys, decimal? legacyWatts, decimal fallbackWatts)
    {
        if (q is not null) return CameoUnitConverter.ToPower(q);
        if (FindProperty(vps, keys) is { } vp) return CameoUnitConverter.ToPower(vp.Value, vp.Unit, allowEmptyAsSi: false);
        return Power.FromWatts(legacyWatts ?? fallbackWatts);
    }

    private static Frequency ResolveFrequency(CameoQuantity? q, Dictionary<string, CameoValueProperty> vps, string[] keys, double? legacyHz, double fallbackHz)
    {
        if (q is not null) return CameoUnitConverter.ToFrequency(q);
        if (FindProperty(vps, keys) is { } vp) return CameoUnitConverter.ToFrequency(vp.Value, vp.Unit, allowEmptyAsSi: false);
        return Frequency.FromHertz(legacyHz ?? fallbackHz);
    }

    private static ElectricCapacitance ResolveCapacitance(CameoQuantity? q, Dictionary<string, CameoValueProperty> vps, string[] keys, double? legacyFarads, double fallbackFarads)
    {
        if (q is not null) return CameoUnitConverter.ToElectricCapacitance(q);
        if (FindProperty(vps, keys) is { } vp) return CameoUnitConverter.ToElectricCapacitance(vp.Value, vp.Unit, allowEmptyAsSi: false);
        return ElectricCapacitance.FromFarads(legacyFarads ?? fallbackFarads);
    }

    private static TimeSpan? ResolveOptionalDuration(CameoQuantity? q, Dictionary<string, CameoValueProperty> vps, string[] keys, double? legacySeconds)
    {
        if (q is not null) return CameoUnitConverter.ToDuration(q);
        if (FindProperty(vps, keys) is { } vp) return CameoUnitConverter.ToDuration(vp.Value, vp.Unit, allowEmptyAsSi: false);
        return legacySeconds is { } s ? TimeSpan.FromSeconds(s) : null;
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
        string u = (prop.Unit ?? string.Empty).Trim();
        try
        {
            InstrumentHardLimits? partial = key switch
            {
                "maxvoltage" or "maxv" or "ovp" => Make(block, maxV: CameoUnitConverter.ToElectricPotential(prop.Value, u, allowEmptyAsSi: true)),
                "minvoltage" or "minv" => Make(block, minV: CameoUnitConverter.ToElectricPotential(prop.Value, u, allowEmptyAsSi: true)),
                "maxcurrent" or "maxi" or "ocp" => Make(block, maxI: CameoUnitConverter.ToElectricCurrent(prop.Value, u, allowEmptyAsSi: true)),
                "mincurrent" or "mini" => Make(block, minI: CameoUnitConverter.ToElectricCurrent(prop.Value, u, allowEmptyAsSi: true)),
                "maxpower" or "maxw" or "opp" => Make(block, maxP: CameoUnitConverter.ToPower(prop.Value, u, allowEmptyAsSi: true)),
                "maxfrequency" or "maxfreq" => Make(block, maxF: CameoUnitConverter.ToFrequency(prop.Value, u, allowEmptyAsSi: true)),
                "minfrequency" or "minfreq" => Make(block, minF: CameoUnitConverter.ToFrequency(prop.Value, u, allowEmptyAsSi: true)),
                "maxtemperature" or "maxtemp" => Make(block, maxT: CameoUnitConverter.ToTemperature(prop.Value, u, allowEmptyAsSi: true)),
                "mintemperature" or "mintemp" => Make(block, minT: CameoUnitConverter.ToTemperature(prop.Value, u, allowEmptyAsSi: true)),
                "minresistance" or "minohm" => Make(block, minR: CameoUnitConverter.ToElectricResistance(prop.Value, u, allowEmptyAsSi: true)),
                "maxresistance" or "maxohm" => Make(block, maxR: CameoUnitConverter.ToElectricResistance(prop.Value, u, allowEmptyAsSi: true)),
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
                "voltage" or "v" or "amplitude" => isMax ? Make(block, maxV: CameoUnitConverter.ToElectricPotential(value, unit, allowEmptyAsSi: true)) : Make(block, minV: CameoUnitConverter.ToElectricPotential(value, unit, allowEmptyAsSi: true)),
                "current" or "i" => isMax ? Make(block, maxI: CameoUnitConverter.ToElectricCurrent(value, unit, allowEmptyAsSi: true)) : Make(block, minI: CameoUnitConverter.ToElectricCurrent(value, unit, allowEmptyAsSi: true)),
                "power" or "p" or "rfpower" when isMax => Make(block, maxP: CameoUnitConverter.ToPower(value, unit, allowEmptyAsSi: true)),
                "frequency" or "freq" or "f" => isMax ? Make(block, maxF: CameoUnitConverter.ToFrequency(value, unit, allowEmptyAsSi: true)) : Make(block, minF: CameoUnitConverter.ToFrequency(value, unit, allowEmptyAsSi: true)),
                "temperature" or "temp" or "t" => isMax ? Make(block, maxT: CameoUnitConverter.ToTemperature(value, unit, allowEmptyAsSi: true)) : Make(block, minT: CameoUnitConverter.ToTemperature(value, unit, allowEmptyAsSi: true)),
                "resistance" or "res" or "r" => isMax ? Make(block, maxR: CameoUnitConverter.ToElectricResistance(value, unit, allowEmptyAsSi: true)) : Make(block, minR: CameoUnitConverter.ToElectricResistance(value, unit, allowEmptyAsSi: true)),
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
