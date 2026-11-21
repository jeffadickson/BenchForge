using BenchForge.Client;
using BenchForge.Contracts;
using BenchForge.Flight;
using BenchForge.Generated;
using BenchForge.Outcomes;
using BenchForge.Procedures;
using UnitsNet;
using Xunit;

namespace BenchForge.Procedures.Tests;

/// <summary>
/// Verifies declarative Starlark (<c>.star</c>) bench limit registration and Starlark-scripted stateful mocks
/// (SMU/PSU nonlinear loads, thermal lag dynamics, DMM readings, and raw SCPI mock sessions driving generated drivers).
/// </summary>
public sealed class StarlarkDeclarativeBenchTests
{
    /// <summary>Verify a concise Starlark file declares sealed hard limits and dynamic scripted mocks across multiple instrument classes.</summary>
    /// <returns>Completion of all Starlark declarative bench assertions.</returns>
    [Fact]
    public async Task StarlarkBenchDeclaresHardLimitsAndScriptedStatefulMocks()
    {
        const string benchStar = """
        # Final fail-safe hard ceilings
        hard_limit(
            role = "eps_psu",
            max_v = 28.0,
            min_v = 0.0,
            max_i = 5.0,
            max_w = 100.0,
            auto_safe = True,
            note = "EPS Flight ICD",
        )
        hard_limit(role = "chamber", min_temp_c = -40.0, max_temp_c = 85.0)
        hard_limit(role = "rf_tx", min_freq_hz = 2.0e9, max_freq_hz = 2.5e9, max_dbm = 10.0)

        # Nonlinear spacecraft EPS load modeled in Starlark
        def eps_load(voltage_v, current_limit_a, output_on, step):
            if not output_on:
                return 0.0
            drawn = 0.4 + voltage_v / 14.0
            return current_limit_a if drawn > current_limit_a else drawn

        # First-order thermal chamber lag modeled in Starlark
        def chamber_lag(setpoint_c, current_c, step):
            return current_c + 0.5 * (setpoint_c - current_c)

        # Ramp DMM telemetry modeled in Starlark
        def telemetry_dmm(step):
            return 3.3 + 0.01 * step

        mock_psu(role = "eps_psu", load_fn = eps_load)
        mock_thermal(role = "chamber", initial_c = 25.0, step_fn = chamber_lag)
        mock_dmm(role = "dmm", measure_v_fn = telemetry_dmm)
        mock_rf_siggen(role = "rf_tx")
        """;

        var res = StarlarkBenchLoader.BuildBench("starlark-flight-bench", benchStar);
        Assert.True(res.IsSuccess, res.IsFailure ? res.Error.Message : null);
        await using var bench = res.Value;

        Assert.True(bench.HardLimits.IsSealed);

        // Starlark nonlinear PSU load: 0.4 + 14 / 14 = 1.4 A
        Assert.True((await bench.PowerSupply("eps_psu").SetCurrentLimitAsync(ElectricCurrent.FromAmperes(3.0))).IsSuccess);
        Assert.True((await bench.PowerSupply("eps_psu").SetVoltageAsync(ElectricPotential.FromVolts(14.0))).IsSuccess);
        Assert.True((await bench.PowerSupply("eps_psu").SetOutputEnabledAsync(true)).IsSuccess);
        Assert.Equal(1.4, (await bench.PowerSupply("eps_psu").MeasureCurrentAsync()).Value.Amperes, 4);

        // Final fail-safe blocks exceeding 28 V and auto-safes output
        var overV = await bench.PowerSupply("eps_psu").SetVoltageAsync(ElectricPotential.FromVolts(32.0));
        Assert.True(overV.IsFailure);
        Assert.Equal("BF-HARDLIMIT-VOLT", overV.Error.Code);
        Assert.False((await bench.PowerSupply("eps_psu").GetOutputEnabledAsync()).Value);

        // Starlark first-order thermal lag: 25 -> 65 -> half way = 45 C, then 55 C
        Assert.True((await bench.Thermal("chamber").SetSetpointAsync(Temperature.FromDegreesCelsius(65.0))).IsSuccess);
        Assert.Equal(45.0, (await bench.Thermal("chamber").MeasureTemperatureAsync()).Value.DegreesCelsius, 4);
        Assert.Equal(55.0, (await bench.Thermal("chamber").MeasureTemperatureAsync()).Value.DegreesCelsius, 4);
        Assert.True((await bench.Thermal("chamber").SetSetpointAsync(Temperature.FromDegreesCelsius(110.0))).IsFailure);

        // Starlark DMM step readings
        Assert.Equal(3.31, (await bench.Dmm("dmm").MeasureDcVoltageAsync()).Value.Volts, 4);
        Assert.Equal(3.32, (await bench.Dmm("dmm").MeasureDcVoltageAsync()).Value.Volts, 4);

        // Starlark max_dbm = 10.0 (10 mW) enforced on RF generator
        Assert.True((await bench.RfSignalGenerator("rf_tx").SetOutputPowerAsync(Power.FromMilliwatts(20m))).IsFailure);
    }

    /// <summary>Verify a Starlark-scripted SCPI mock session can back any generated SCPI driver behind HardLimitScpiGuard.</summary>
    /// <returns>Completion of Starlark SCPI mock assertions.</returns>
    [Fact]
    public async Task StarlarkScriptedScpiSessionDrivesGeneratedDriverBehindHardLimitGuard()
    {
        const string scpiStar = """
        hard_limit(role = "n8921a", max_v = 28.0, max_i = 10.0)

        def scpi_handler(cmd, is_query, state):
            if cmd == "SOUR:VOLT?":
                return state.get("v", "0.0")
            if cmd.startswith("SOUR:VOLT "):
                state["v"] = cmd[10:]
                return ""
            if cmd == "MEAS:VOLT?":
                return state.get("v", "0.0")
            return "0.0"

        mock_scpi(role = "n8921a", handler_fn = scpi_handler)
        """;

        var builder = BenchSession.Create("star-scpi-bench");
        var apply = StarlarkBenchLoader.ApplyTo(builder, scpiStar);
        Assert.True(apply.IsSuccess, apply.IsFailure ? apply.Error.Message : null);
        var scpiSession = apply.Value["n8921a"];
        await using var bench = builder.Build();

        // GuardedScpiSession enforces the Starlark-declared hard_limit right before bytes hit the Starlark SCPI mock
        var guarded = new GuardedScpiSession("n8921a", scpiSession, bench.HardLimits.ToScpiGuard(), isPhysicalHardware: false);
        var driver = new KeysightN8921A(guarded);

        Assert.True((await driver.TrySetVoltageAsync(1, ElectricPotential.FromVolts(24.5))).IsSuccess);
        Assert.Equal(24.5, (await driver.TryGetVoltageAsync(1)).Value.Volts, 4);

        // Attempting 35 V is blocked by the Starlark-registered hard ceiling (28 V)
        var denied = await driver.TrySetVoltageAsync(1, ElectricPotential.FromVolts(35.0));
        Assert.True(denied.IsFailure);
        Assert.Equal(BenchErrorKind.GuardDenied, denied.Error.Kind);
        Assert.Equal("BF-HARDLIMIT-VOLT", denied.Error.Code);
    }

    /// <summary>Verify Starlark scripts cannot relax hard limits already registered from a Cameo manifest.</summary>
    [Fact]
    public void StarlarkScriptCannotRelaxCameoHardLimits()
    {
        const string cameoJson = """
        {
          "benchName": "cameo-plus-starlark",
          "blocks": [
            {
              "role": "psu",
              "instrumentClass": "PSU",
              "backend": "Mock",
              "valueProperties": [
                { "name": "maxVoltage", "value": 28.0, "unit": "V" }
              ]
            }
          ]
        }
        """;

        const string rogueStar = """
        hard_limit(role = "psu", max_v = 50.0)
        """;

        var builder = BenchSession.Create("cameo-plus-starlark").UseCameoManifestJson(cameoJson);
        var res = StarlarkBenchLoader.ApplyTo(builder, rogueStar);
        Assert.True(res.IsFailure);
        Assert.Equal("BF-HARDLIMIT-RELAX-DENIED", res.Error.Code);
    }
}
