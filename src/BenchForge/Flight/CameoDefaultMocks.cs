using System.Text.Json;
using System.Text.Json.Serialization;
using BenchForge.Client;
using BenchForge.Outcomes;

namespace BenchForge.Flight;

/// <summary>
/// Canonical library of Cameo Systems Modeler / SysML default mock definitions authored with explicit QUDV physical
/// units for all 16 BenchForge aerospace EGSE instrument classes. Every default mock carries explicit typed units
/// (<see cref="CameoValueProperty"/> and <see cref="CameoQuantity"/>) for both nominal physics and conservative
/// fail-safe hard limits, eliminating unitless default mock drift.
/// </summary>
public static class CameoDefaultMocks
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Ordered list of all 16 canonical aerospace EGSE instrument classes supported by Cameo default mocks.</summary>
    public static IReadOnlyList<string> CanonicalClasses { get; } =
    [
        "PSU",
        "SMU",
        "DMM",
        "THERMAL",
        "FGEN",
        "SCOPE",
        "SWITCH",
        "ELOAD",
        "RFSIGGEN",
        "SPECAN",
        "PWRMETER",
        "COUNTER",
        "LCR",
        "VNA",
        "DAQ",
        "DIO",
        "INTERLOCK"
    ];

    /// <summary>
    /// Create a unit-explicit Cameo / SysML default mock block for any canonical BenchForge instrument class,
    /// optionally merging additional unit-typed SysML <paramref name="valuePropertyOverrides"/>.
    /// </summary>
    /// <param name="role">Logical bench role identifier (for example, <c>eps_psu</c>, <c>bus_dmm</c>, <c>rf_vna</c>).</param>
    /// <param name="instrumentClass">Canonical class tag (<c>PSU</c>, <c>SMU</c>, <c>DMM</c>, <c>THERMAL</c>, <c>FGEN</c>, <c>SCOPE</c>, <c>SWITCH</c>, <c>ELOAD</c>, <c>RFSIGGEN</c>, <c>SPECAN</c>, <c>PWRMETER</c>, <c>COUNTER</c>, <c>LCR</c>, <c>VNA</c>, <c>DAQ</c>, <c>DIO</c>, <c>INTERLOCK</c>).</param>
    /// <param name="valuePropertyOverrides">Optional SysML value properties with explicit units overriding default physics or tightening fail-safe ceilings.</param>
    /// <returns>Railroad outcome containing the unit-annotated <see cref="CameoInstrumentBlock"/> or a validation error.</returns>
    public static BenchResult<CameoInstrumentBlock> CreateDefaultBlock(
        string role,
        string instrumentClass,
        IReadOnlyList<CameoValueProperty>? valuePropertyOverrides = null)
    {
        if (string.IsNullOrWhiteSpace(role))
            return BenchResult<CameoInstrumentBlock>.Fail(BenchError.Validation("BF-CAMEO-ROLE", "Default mock role must not be empty."));
        if (string.IsNullOrWhiteSpace(instrumentClass))
            return BenchResult<CameoInstrumentBlock>.Fail(BenchError.Validation("BF-CAMEO-CLASS", "Default mock instrumentClass must not be empty.", role));

        string cls = instrumentClass.Trim().ToUpperInvariant();
        CameoInstrumentBlock? block = cls switch
        {
            "PSU" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "PSU",
                Backend: "Mock",
                ValueProperties:
                [
                    new("loadResistance", 14.0, "Ohm", "ElectricResistance", "Ohms"),
                    new("defaultCurrentLimit", 500.0, "mA", "ElectricCurrent", "Milliamperes"),
                    new("minVoltage", 0.0, "V", "ElectricPotential", "Volts"),
                    new("maxVoltage", 32.0, "V", "ElectricPotential", "Volts"),
                    new("maxCurrent", 5.0, "A", "ElectricCurrent", "Amperes"),
                    new("maxPower", 150.0, "W", "Power", "Watts")
                ],
                RequireComplianceBeforeOutputOn: true,
                AutoSafeOnViolation: true,
                PolicyNote: "Default Cameo-with-units spacecraft DC Power Supply mock"),

            "SMU" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "SMU",
                Backend: "Mock",
                ValueProperties:
                [
                    new("loadResistance", 10.0, "Ohm", "ElectricResistance", "Ohms"),
                    new("defaultCurrentLimit", 100.0, "mA", "ElectricCurrent", "Milliamperes"),
                    new("minVoltage", 0.0, "V", "ElectricPotential", "Volts"),
                    new("maxVoltage", 28.0, "V", "ElectricPotential", "Volts"),
                    new("maxCurrent", 2.0, "A", "ElectricCurrent", "Amperes"),
                    new("maxPower", 40.0, "W", "Power", "Watts")
                ],
                RequireComplianceBeforeOutputOn: true,
                AutoSafeOnViolation: true,
                PolicyNote: "Default Cameo-with-units Source-Measure Unit mock"),

            "DMM" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "DMM",
                Backend: "Mock",
                ValueProperties:
                [
                    new("nominalVoltage", 28.0, "V", "ElectricPotential", "Volts"),
                    new("nominalResistance", 100.0, "Ohm", "ElectricResistance", "Ohms")
                ],
                PolicyNote: "Default Cameo-with-units 6.5-digit DMM mock"),

            "THERMAL" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "THERMAL",
                Backend: "Mock",
                ValueProperties:
                [
                    new("initialTemperature", 25.0, "degC", "ThermodynamicTemperature", "DegreesCelsius"),
                    new("minTemperature", -55.0, "degC", "ThermodynamicTemperature", "DegreesCelsius"),
                    new("maxTemperature", 125.0, "degC", "ThermodynamicTemperature", "DegreesCelsius")
                ],
                PolicyNote: "Default Cameo-with-units TVAC / Thermal Chamber mock"),

            "FGEN" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "FGEN",
                Backend: "Mock",
                ValueProperties:
                [
                    new("minFrequency", 1.0, "Hz", "Frequency", "Hertz"),
                    new("maxFrequency", 25.0, "MHz", "Frequency", "Megahertz"),
                    new("maxVoltage", 10.0, "V", "ElectricPotential", "Volts")
                ],
                PolicyNote: "Default Cameo-with-units Function / Arbitrary Waveform Generator mock"),

            "SCOPE" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "SCOPE",
                Backend: "Mock",
                ValueProperties:
                [
                    new("dcOffset", 3.3, "V", "ElectricPotential", "Volts"),
                    new("rippleAmplitude", 8.0, "mV", "ElectricPotential", "Millivolts")
                ],
                PolicyNote: "Default Cameo-with-units Oscilloscope mock"),

            "SWITCH" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "SWITCH",
                Backend: "Mock",
                PolicyNote: "Default Cameo-with-units Relay / Switch Matrix mock"),

            "ELOAD" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "ELOAD",
                Backend: "Mock",
                ValueProperties:
                [
                    new("sourceVoltage", 28.0, "V", "ElectricPotential", "Volts"),
                    new("maxCurrent", 10.0, "A", "ElectricCurrent", "Amperes"),
                    new("minResistance", 1.0, "Ohm", "ElectricResistance", "Ohms"),
                    new("maxPower", 250.0, "W", "Power", "Watts")
                ],
                PolicyNote: "Default Cameo-with-units Programmable DC Electronic Load mock"),

            "RFSIGGEN" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "RFSIGGEN",
                Backend: "Mock",
                ValueProperties:
                [
                    new("minFrequency", 100.0, "MHz", "Frequency", "Megahertz"),
                    new("maxFrequency", 6.0, "GHz", "Frequency", "Gigahertz"),
                    new("maxPower", 10.0, "dBm", "Power", "DecibelMilliwatts")
                ],
                PolicyNote: "Default Cameo-with-units RF Signal Generator mock"),

            "SPECAN" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "SPECAN",
                Backend: "Mock",
                PolicyNote: "Default Cameo-with-units Spectrum Analyzer mock"),

            "PWRMETER" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "PWRMETER",
                Backend: "Mock",
                ValueProperties:
                [
                    new("measuredPower", 0.0, "dBm", "Power", "DecibelMilliwatts")
                ],
                PolicyNote: "Default Cameo-with-units RF Power Meter mock"),

            "COUNTER" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "COUNTER",
                Backend: "Mock",
                ValueProperties:
                [
                    new("frequency", 10.0, "MHz", "Frequency", "Megahertz")
                ],
                PolicyNote: "Default Cameo-with-units 10 MHz Reference Frequency Counter mock"),

            "LCR" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "LCR",
                Backend: "Mock",
                ValueProperties:
                [
                    new("capacitance", 1.0, "uF", "ElectricCapacitance", "Microfarads"),
                    new("nominalResistance", 50.0, "mOhm", "ElectricResistance", "Milliohms"),
                    new("minFrequency", 20.0, "Hz", "Frequency", "Hertz"),
                    new("maxFrequency", 2.0, "MHz", "Frequency", "Megahertz")
                ],
                PolicyNote: "Default Cameo-with-units LCR / Impedance Meter mock"),

            "VNA" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "VNA",
                Backend: "Mock",
                ValueProperties:
                [
                    new("minFrequency", 100.0, "MHz", "Frequency", "Megahertz"),
                    new("maxFrequency", 8.5, "GHz", "Frequency", "Gigahertz"),
                    new("maxPower", 0.0, "dBm", "Power", "DecibelMilliwatts")
                ],
                PolicyNote: "Default Cameo-with-units Vector Network Analyzer mock"),

            "DAQ" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "DAQ",
                Backend: "Mock",
                Mock: new CameoMockParameters(ChannelCount: 8),
                PolicyNote: "Default Cameo-with-units 8-channel Multiplexed DAQ mock"),

            "DIO" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "DIO",
                Backend: "Mock",
                PolicyNote: "Default Cameo-with-units Discrete Digital I/O mock"),

            "INTERLOCK" => new CameoInstrumentBlock(
                Role: role,
                InstrumentClass: "INTERLOCK",
                Backend: "Mock",
                ValueProperties:
                [
                    new("watchdogTimeout", 30.0, "s", "Time", "Seconds")
                ],
                PolicyNote: "Default Cameo-with-units E-Stop & Dead-Man Watchdog Safety Interlock mock"),

            _ => null
        };

        if (block is null)
            return BenchResult<CameoInstrumentBlock>.Fail(BenchError.Validation("BF-CAMEO-CLASS", $"Unsupported canonical default mock class '{instrumentClass}'.", role));

        if (valuePropertyOverrides is { Count: > 0 })
        {
            var merged = new List<CameoValueProperty>();
            if (block.ValueProperties is not null)
                merged.AddRange(block.ValueProperties);

            foreach (var ov in valuePropertyOverrides)
            {
                string ovKey = ov.Name.Trim().ToLowerInvariant().Replace("_", "");
                // Hard-limit properties are appended so CompileBlockLimits monotonically intersects (tightens) them.
                // Mock initial physics properties replace any default entry with the same normalized key.
                bool isLimitProp = ovKey is "maxvoltage" or "maxv" or "ovp" or "minvoltage" or "minv"
                    or "maxcurrent" or "maxi" or "ocp" or "mincurrent" or "mini"
                    or "maxpower" or "maxw" or "opp"
                    or "maxfrequency" or "maxfreq" or "minfrequency" or "minfreq"
                    or "maxtemperature" or "maxtemp" or "mintemperature" or "mintemp"
                    or "minresistance" or "minohm" or "maxresistance" or "maxohm";

                if (!isLimitProp)
                    merged.RemoveAll(existing => string.Equals(existing.Name.Trim().ToLowerInvariant().Replace("_", ""), ovKey, StringComparison.OrdinalIgnoreCase));

                merged.Add(ov);
            }

            block = block with { ValueProperties = merged };
        }

        return BenchResult<CameoInstrumentBlock>.Ok(block);
    }

    /// <summary>
    /// Build a complete default Cameo / SysML bench manifest containing unit-explicit mocks for all 16 canonical
    /// aerospace EGSE instrument classes, canonical <c>simulationTime</c>, and optional unit-explicit <c>digitalTwin</c> physics.
    /// </summary>
    /// <param name="benchName">Logical bench name.</param>
    /// <param name="enableDigitalTwin">When true, couples PSU, Thermal, ELoad, DMM, and Scope onto a unit-explicit <see cref="DigitalTwinPlant"/>.</param>
    /// <returns>Complete <see cref="CameoBenchManifest"/> authored with QUDV units.</returns>
    public static CameoBenchManifest CreateDefaultBenchManifest(
        string benchName = "benchforge-default-egse-bench",
        bool enableDigitalTwin = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchName);

        var blocks = new List<CameoInstrumentBlock>
        {
            CreateDefaultBlock("psu", "PSU").Value,
            CreateDefaultBlock("smu", "SMU").Value,
            CreateDefaultBlock("dmm", "DMM").Value,
            CreateDefaultBlock("thermal", "THERMAL").Value,
            CreateDefaultBlock("fgen", "FGEN").Value,
            CreateDefaultBlock("scope", "SCOPE").Value,
            CreateDefaultBlock("switch", "SWITCH").Value,
            CreateDefaultBlock("eload", "ELOAD").Value,
            CreateDefaultBlock("rf_siggen", "RFSIGGEN").Value,
            CreateDefaultBlock("spec_an", "SPECAN").Value,
            CreateDefaultBlock("rf_pwrmeter", "PWRMETER").Value,
            CreateDefaultBlock("counter", "COUNTER").Value,
            CreateDefaultBlock("lcr", "LCR").Value,
            CreateDefaultBlock("vna", "VNA").Value,
            CreateDefaultBlock("daq", "DAQ").Value,
            CreateDefaultBlock("dio", "DIO").Value,
            CreateDefaultBlock("interlock", "INTERLOCK").Value
        };

        var twin = enableDigitalTwin
            ? new CameoDigitalTwinConfig(
                Enabled: true,
                DmmSwitchRole: "switch",
                DmmRequiredRoute: "BUS_A->UUT_V",
                BaseLoadResistance: new CameoQuantity(14.0, "Ohm", "ElectricResistance", "Ohms"),
                TempCoeff: new CameoQuantity(3500.0, "ppm/degC", "TemperatureCoefficient", "PartsPerMillionPerDegreeCelsius"),
                StandbyCurrent: new CameoQuantity(250.0, "mA", "ElectricCurrent", "Milliamperes"),
                BusSeriesResistance: new CameoQuantity(80.0, "mOhm", "ElectricResistance", "Milliohms"),
                SwitchingRipple: new CameoQuantity(12.0, "mV", "ElectricPotential", "Millivolts"),
                InitialTemperature: new CameoQuantity(25.0, "degC", "ThermodynamicTemperature", "DegreesCelsius"),
                ThermalTimeConstant: new CameoQuantity(60.0, "s", "Time", "Seconds"),
                SelfHeating: new CameoQuantity(0.25, "degC/W", "ThermalResistance", "DegreesCelsiusPerWatt"))
            : null;

        return new CameoBenchManifest(
            BenchName: benchName,
            ModelVersion: "Cameo-SysML-QUDV-DefaultMocks-v1",
            RequireHardLimitsForPhysical: true,
            SimulationTime: new CameoSimulationTimeConfig(Mode: "Manual", Speed: 100.0),
            DigitalTwin: twin,
            Blocks: blocks);
    }

    /// <summary>Serialize any <see cref="CameoBenchManifest"/> to indented Cameo/SysML QUDV JSON.</summary>
    /// <param name="manifest">Typed Cameo bench manifest.</param>
    /// <returns>Indented JSON string preserving explicit physical units and QUDV metadata.</returns>
    public static string ExportManifestJson(CameoBenchManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, ExportJsonOptions);
    }

    /// <summary>Export the full 16-class default Cameo-with-units mock bench manifest as indented JSON.</summary>
    /// <param name="benchName">Logical bench name.</param>
    /// <param name="enableDigitalTwin">When true, includes unit-explicit coupled digital-twin physics.</param>
    /// <returns>Indented Cameo/SysML QUDV JSON manifest.</returns>
    public static string ExportDefaultManifestJson(
        string benchName = "benchforge-default-egse-bench",
        bool enableDigitalTwin = true) =>
        ExportManifestJson(CreateDefaultBenchManifest(benchName, enableDigitalTwin));

    /// <summary>Instantiate a ready-to-run <see cref="BenchSession"/> populated with all 16 unit-explicit Cameo default mocks.</summary>
    /// <param name="benchName">Logical bench name.</param>
    /// <param name="enableDigitalTwin">When true, couples PSU, Thermal, ELoad, DMM, and Scope onto a unit-explicit <see cref="DigitalTwinPlant"/>.</param>
    /// <param name="clock">Optional <see cref="TimeProvider"/>.</param>
    /// <returns>Railroad outcome containing the sealed <see cref="BenchSession"/>.</returns>
    public static BenchResult<BenchSession> CreateDefaultBench(
        string benchName = "benchforge-default-egse-bench",
        bool enableDigitalTwin = true,
        TimeProvider? clock = null) =>
        DeclarativeBenchLoader.BuildBenchFromManifest(CreateDefaultBenchManifest(benchName, enableDigitalTwin), clock);
}
