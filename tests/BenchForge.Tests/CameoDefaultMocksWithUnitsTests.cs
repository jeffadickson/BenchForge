using BenchForge.Client;
using BenchForge.Flight;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>
/// Verification of Cameo-with-units (SysML QUDV) default mocks across all 16 canonical aerospace EGSE instrument
/// classes, strict <see cref="CameoUnitConverter"/> dimensional safety, round-trip Cameo export/import, and
/// strongly-typed <c>UnitsNet</c> default mock builders.
/// </summary>
public sealed class CameoDefaultMocksWithUnitsTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>Verify CameoDefaultMocks.CreateDefaultBench provisions all 16 canonical classes with unit-converted defaults and fail-safe limits.</summary>
    /// <returns>Completion of default Cameo mock assertions.</returns>
    [Fact]
    public async Task CameoDefaultBenchProvisionsAllCanonicalClassesWithUnitsAndHardLimits()
    {
        var benchRes = CameoDefaultMocks.CreateDefaultBench("unit-default-egse", enableDigitalTwin: true);
        Assert.True(benchRes.IsSuccess, benchRes.IsFailure ? benchRes.Error.Message : null);
        await using var bench = benchRes.Value;

        Assert.Equal(CameoDefaultMocks.CanonicalClasses.Count, bench.Describe().Count);
        Assert.NotNull(bench.DigitalTwin);

        // Counter default: 10 MHz
        var freq = await bench.FrequencyCounter("counter").MeasureFrequencyAsync();
        Assert.True(freq.IsSuccess);
        Assert.Equal(10.0, freq.Value.Megahertz, 6);

        // RF Power Meter default: 0 dBm == 1 mW
        var pwr = await bench.RfPowerMeter("rf_pwrmeter").MeasurePowerAsync();
        Assert.True(pwr.IsSuccess);
        Assert.Equal(1.0, (double)pwr.Value.Milliwatts, 4);

        // LCR default: 1 uF and 50 mOhm ESR
        var cap = await bench.LcrMeter("lcr").MeasureCapacitanceAsync();
        var esr = await bench.LcrMeter("lcr").MeasureResistanceAsync();
        Assert.True(cap.IsSuccess);
        Assert.True(esr.IsSuccess);
        Assert.Equal(1.0, cap.Value.Microfarads, 6);
        Assert.Equal(50.0, esr.Value.Milliohms, 6);

        // PSU hard limit: 32 V max (33 V rejected fail-closed)
        var overV = await bench.PowerSupply("psu").SetVoltageAsync(ElectricPotential.FromVolts(33.0));
        Assert.True(overV.IsFailure);
        Assert.Equal("BF-HARDLIMIT-VOLT", overV.Error.Code);

        // VNA hard limit: 0 dBm max (+3 dBm rejected fail-closed)
        var overVna = await bench.Vna("vna").SetSourcePowerAsync(Power.FromMilliwatts(2.0m));
        Assert.True(overVna.IsFailure);
        Assert.Equal("BF-HARDLIMIT-POWER", overVna.Error.Code);

        // Interlock default watchdog: 30 s (kicked at t=0, alive at 25 s, trips at 35 s)
        Assert.True((await bench.SafetyInterlock("interlock").KickWatchdogAsync()).IsSuccess);
        Assert.True(bench.AdvanceSimulationTime(TimeSpan.FromSeconds(25)).IsSuccess);
        Assert.True((await bench.SafetyInterlock("interlock").GetStatusAsync()).Value.IsArmed);
        Assert.True(bench.AdvanceSimulationTime(TimeSpan.FromSeconds(10)).IsSuccess);
        Assert.True((await bench.SafetyInterlock("interlock").GetStatusAsync()).Value.IsTripped);
    }

    /// <summary>Verify curated default-egse-mocks.cameo.json and ExportDefaultManifestJson round-trip cleanly with identical inventory.</summary>
    /// <returns>Completion of round-trip Cameo export/import assertions.</returns>
    [Fact]
    public async Task CuratedDefaultCameoManifestAndExportRoundTripCleanly()
    {
        string examplePath = Path.Combine(RepoRoot, "examples", "cameo", "default-egse-mocks.cameo.json");
        Assert.True(File.Exists(examplePath), $"Expected curated Cameo default manifest at {examplePath}");

        var fromFileRes = DeclarativeBenchLoader.BuildBenchFromCameoJson(await File.ReadAllTextAsync(examplePath));
        Assert.True(fromFileRes.IsSuccess, fromFileRes.IsFailure ? fromFileRes.Error.Message : null);
        await using var fromFile = fromFileRes.Value;

        string exportedJson = CameoDefaultMocks.ExportDefaultManifestJson("exported-defaults", enableDigitalTwin: true);
        Assert.Contains("\"quantityKind\": \"ElectricResistance\"", exportedJson);
        Assert.Contains("\"unit\": \"ppm/degC\"", exportedJson);
        Assert.Contains("\"unit\": \"mOhm\"", exportedJson);
        Assert.Contains("\"unit\": \"dBm\"", exportedJson);

        var fromExportRes = DeclarativeBenchLoader.BuildBenchFromCameoJson(exportedJson);
        Assert.True(fromExportRes.IsSuccess, fromExportRes.IsFailure ? fromExportRes.Error.Message : null);
        await using var fromExport = fromExportRes.Value;

        Assert.Equal(fromFile.Describe().Count, fromExport.Describe().Count);
        Assert.NotNull(fromExport.DigitalTwin);
        Assert.Equal(fromFile.HardLimits.Snapshot().Count, fromExport.HardLimits.Snapshot().Count);
    }

    /// <summary>Verify CameoQuantity parses both SysML QUDV objects and compact quantity strings, and rejects dimensional mismatches fail-closed.</summary>
    /// <returns>Completion of unit conversion and dimensional guard assertions.</returns>
    [Fact]
    public async Task CameoUnitConverterHandlesFullUnitsAndRejectsDimensionalMismatches()
    {
        // Compact string and object forms across mixed physical units (degF, mOhm, uF, dBm, MHz, ms)
        string mixedUnitsJson = """
        {
          "benchName": "mixed-units-check",
          "blocks": [
            {
              "role": "ch_therm",
              "instrumentClass": "THERMAL",
              "mock": { "initialTemperature": "77 degF" }
            },
            {
              "role": "cap_lcr",
              "instrumentClass": "LCR",
              "mock": {
                "capacitance": { "value": 470, "unit": "nF", "quantityKind": "ElectricCapacitance" },
                "nominalResistance": "25 mOhm"
              }
            },
            {
              "role": "rf_sensor",
              "instrumentClass": "PWRMETER",
              "mock": { "measuredPower": "10 dBm" }
            },
            {
              "role": "wd_fast",
              "instrumentClass": "INTERLOCK",
              "mock": { "watchdogTimeout": "500 ms" }
            }
          ]
        }
        """;

        var okRes = DeclarativeBenchLoader.BuildBenchFromCameoJson(mixedUnitsJson);
        Assert.True(okRes.IsSuccess, okRes.IsFailure ? okRes.Error.Message : null);
        await using var bench = okRes.Value;

        Assert.Equal(25.0, (await bench.Thermal("ch_therm").MeasureTemperatureAsync()).Value.DegreesCelsius, 3);
        Assert.Equal(470.0, (await bench.LcrMeter("cap_lcr").MeasureCapacitanceAsync()).Value.Nanofarads, 3);
        Assert.Equal(25.0, (await bench.LcrMeter("cap_lcr").MeasureResistanceAsync()).Value.Milliohms, 3);
        Assert.Equal(10.0, (double)(await bench.RfPowerMeter("rf_sensor").MeasurePowerAsync()).Value.Milliwatts, 3);

        Assert.True((await bench.SafetyInterlock("wd_fast").KickWatchdogAsync()).IsSuccess);
        Assert.True(bench.AdvanceSimulationTime(TimeSpan.FromMilliseconds(600)).IsSuccess);
        Assert.True((await bench.SafetyInterlock("wd_fast").GetStatusAsync()).Value.IsTripped);

        // Dimensional mismatch: passing frequency unit "10 MHz" into a voltage field must fail closed with BF-CAMEO-UNIT!
        string badUnitJson = """
        {
          "benchName": "bad-unit-check",
          "blocks": [
            {
              "role": "bad_dmm",
              "instrumentClass": "DMM",
              "mock": { "nominalVoltage": "10 MHz" }
            }
          ]
        }
        """;
        var badRes = DeclarativeBenchLoader.BuildBenchFromCameoJson(badUnitJson);
        Assert.True(badRes.IsFailure);
        Assert.Equal("BF-CAMEO-UNIT", badRes.Error.Code);

        // Case-sensitive resistance distinction: mOhm vs MOhm
        Assert.Equal(0.05, CameoUnitConverter.ToElectricResistance(50, "mOhm").Ohms, 6);
        Assert.Equal(50_000_000.0, CameoUnitConverter.ToElectricResistance(50, "MOhm").Ohms, 1);
    }

    /// <summary>Verify BenchBuilder.AddCameoDefaultMock with unit overrides and strongly-typed UnitsNet default mock overloads.</summary>
    /// <returns>Completion of builder unit-typed mock assertions.</returns>
    [Fact]
    public async Task BenchBuilderSupportsCameoDefaultMocksWithOverridesAndUnitsNetOverloads()
    {
        await using var bench = BenchSession.Create("typed-builder-bench")
            .AddCameoDefaultMock("psu1", "PSU",
                new CameoValueProperty("loadResistance", 20.0, "Ohm"),
                new CameoValueProperty("maxVoltage", 12.0, "V"))
            .AddCameoDefaultMock("cnt1", "COUNTER",
                new CameoValueProperty("frequency", 25.0, "MHz"))
            .AddMockDmm("dmm1", ElectricPotential.FromMillivolts(3300), ElectricResistance.FromKiloohms(4.7))
            .AddMockThermal("thm1", Temperature.FromKelvins(300.15))
            .AddMockRfPowerMeter("pm1", Power.FromMilliwatts(5m))
            .AddMockLcrMeter("lcr1", ElectricCapacitance.FromNanofarads(100), ElectricResistance.FromMilliohms(120))
            .Build();

        // Tightened Cameo maxVoltage (12 V < 32 V default) blocks 15 V
        var over12 = await bench.PowerSupply("psu1").SetVoltageAsync(ElectricPotential.FromVolts(15.0));
        Assert.True(over12.IsFailure);
        Assert.Equal("BF-HARDLIMIT-VOLT", over12.Error.Code);

        // Overridden Cameo counter frequency = 25 MHz
        Assert.Equal(25.0, (await bench.FrequencyCounter("cnt1").MeasureFrequencyAsync()).Value.Megahertz, 6);

        // UnitsNet typed builder mocks preserve exact physical units
        Assert.Equal(3.3, (await bench.Dmm("dmm1").MeasureDcVoltageAsync()).Value.Volts, 6);
        Assert.Equal(4700.0, (await bench.Dmm("dmm1").MeasureResistanceAsync()).Value.Ohms, 3);
        Assert.Equal(27.0, (await bench.Thermal("thm1").MeasureTemperatureAsync()).Value.DegreesCelsius, 3);
        Assert.Equal(5.0, (double)(await bench.RfPowerMeter("pm1").MeasurePowerAsync()).Value.Milliwatts, 4);
        Assert.Equal(100.0, (await bench.LcrMeter("lcr1").MeasureCapacitanceAsync()).Value.Nanofarads, 4);
        Assert.Equal(120.0, (await bench.LcrMeter("lcr1").MeasureResistanceAsync()).Value.Milliohms, 4);
    }
}
