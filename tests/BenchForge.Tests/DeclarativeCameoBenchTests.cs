using BenchForge.Client;
using BenchForge.Flight;
using BenchForge.Outcomes;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>
/// Verifies declarative Cameo Systems Modeler / SysML JSON bench manifests compile value properties
/// (including dBm/K/GHz/mA units) and SysML constraint expressions into monotonically-tightened
/// fail-safe <see cref="InstrumentHardLimits"/> and symmetric mock instruments.
/// </summary>
public sealed class DeclarativeCameoBenchTests
{
    /// <summary>Verify a multi-instrument Cameo/SysML JSON manifest builds a sealed bench and enforces strictest constraints.</summary>
    /// <returns>Completion of all Cameo manifest assertions.</returns>
    [Fact]
    public async Task CameoManifestCompilesUnitsConstraintsAndMocksIntoSealedBench()
    {
        const string cameoJson = """
        {
          "benchName": "spacecraft-eps-tvac",
          "modelVersion": "SysML-ICD-Rev4",
          "requireHardLimitsForPhysical": true,
          "blocks": [
            {
              "role": "eps_psu",
              "instrumentClass": "PSU",
              "backend": "Mock",
              "autoSafeOnViolation": true,
              "policyNote": "EPS Primary 28V Bus",
              "valueProperties": [
                { "name": "maxVoltage", "value": 28.5, "unit": "V" },
                { "name": "maxCurrent", "value": 4500, "unit": "mA" },
                { "name": "maxPower", "value": 100, "unit": "W" }
              ],
              "constraints": [
                { "id": "REQ-EPS-1", "expression": "voltage <= 24.0 V" }
              ],
              "mock": { "loadResistanceOhm": 12.0, "defaultCurrentLimitA": 1.0 }
            },
            {
              "role": "chamber",
              "instrumentClass": "THERMAL",
              "backend": "Mock",
              "valueProperties": [
                { "name": "minTemperature", "value": -40.0, "unit": "degC" },
                { "name": "maxTemperature", "value": 358.15, "unit": "K" }
              ],
              "mock": { "initialTemperatureC": 22.0 }
            },
            {
              "role": "rf_tx",
              "instrumentClass": "RFSIGGEN",
              "backend": "Mock",
              "valueProperties": [
                { "name": "minFrequency", "value": 2.0, "unit": "GHz" },
                { "name": "maxFrequency", "value": 2.5, "unit": "GHz" },
                { "name": "maxPower", "value": 10.0, "unit": "dBm" }
              ]
            },
            {
              "role": "eload",
              "instrumentClass": "ELOAD",
              "backend": "Mock",
              "constraints": [
                { "id": "REQ-LOAD-SHORT", "expression": "resistance >= 2.5 Ohm" },
                { "id": "REQ-LOAD-CURR", "expression": "current <= 8.0 A" }
              ],
              "mock": { "sourceVoltageV": 24.0 }
            }
          ]
        }
        """;

        var benchRes = DeclarativeBenchLoader.BuildBenchFromCameoJson(cameoJson);
        Assert.True(benchRes.IsSuccess, benchRes.IsFailure ? benchRes.Error.Message : null);
        await using var bench = benchRes.Value;

        Assert.True(bench.HardLimits.IsSealed);
        Assert.Equal(4, bench.Describe().Count);

        // Strictest constraint (24.0 V) tightened the 28.5 V valueProperty!
        var psuLimits = bench.HardLimits.TryGet("eps_psu");
        Assert.NotNull(psuLimits);
        Assert.Equal(24.0, psuLimits!.MaxVoltage!.Value.Volts, 6);
        Assert.Equal(4.5, psuLimits.MaxCurrent!.Value.Amperes, 6);

        Assert.True((await bench.PowerSupply("eps_psu").SetCurrentLimitAsync(ElectricCurrent.FromAmperes(2.0))).IsSuccess);
        Assert.True((await bench.PowerSupply("eps_psu").SetVoltageAsync(ElectricPotential.FromVolts(24.0))).IsSuccess);
        var overV = await bench.PowerSupply("eps_psu").SetVoltageAsync(ElectricPotential.FromVolts(25.0));
        Assert.True(overV.IsFailure);
        Assert.Equal("BF-HARDLIMIT-VOLT", overV.Error.Code);

        // Kelvin 358.15 K == 85.0 degC
        Assert.True((await bench.Thermal("chamber").SetSetpointAsync(Temperature.FromDegreesCelsius(85.0))).IsSuccess);
        Assert.True((await bench.Thermal("chamber").SetSetpointAsync(Temperature.FromDegreesCelsius(90.0))).IsFailure);

        // 10 dBm == 10 mW (0.01 W)
        Assert.True((await bench.RfSignalGenerator("rf_tx").SetFrequencyAsync(Frequency.FromGigahertz(2.2))).IsSuccess);
        Assert.True((await bench.RfSignalGenerator("rf_tx").SetOutputPowerAsync(Power.FromMilliwatts(9.5m))).IsSuccess);
        var overRf = await bench.RfSignalGenerator("rf_tx").SetOutputPowerAsync(Power.FromMilliwatts(15m));
        Assert.True(overRf.IsFailure);
        Assert.Equal("BF-HARDLIMIT-POWER", overRf.Error.Code);

        // Electronic load short-circuit constraint >= 2.5 Ohm
        Assert.True((await bench.ElectronicLoad("eload").SetResistanceLoadAsync(ElectricResistance.FromOhms(1.0))).IsFailure);
        Assert.True((await bench.ElectronicLoad("eload").SetResistanceLoadAsync(ElectricResistance.FromOhms(5.0))).IsSuccess);
    }

    /// <summary>Verify Cameo manifest cannot relax limits already registered on BenchBuilder.</summary>
    [Fact]
    public void CameoManifestCannotRelaxPreRegisteredHardLimits()
    {
        const string looserCameo = """
        {
          "benchName": "attempt-relax",
          "blocks": [
            {
              "role": "psu",
              "instrumentClass": "PSU",
              "backend": "Mock",
              "valueProperties": [
                { "name": "maxVoltage", "value": 40.0, "unit": "V" }
              ]
            }
          ]
        }
        """;

        var ex = Assert.Throws<BenchOperationException>(() =>
            BenchSession.Create("strict-bench")
                .WithHardLimits("psu", new InstrumentHardLimits(MaxVoltage: ElectricPotential.FromVolts(28.0)))
                .UseCameoManifestJson(looserCameo));

        Assert.Equal("BF-HARDLIMIT-RELAX-DENIED", ex.Error.Code);
    }
}
