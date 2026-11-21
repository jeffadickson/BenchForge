using BenchForge.Client;
using BenchForge.Contracts;
using BenchForge.Generated;
using BenchForge.Interfaces;
using BenchForge.Oscilloscopes;
using BenchForge.Outcomes;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>
/// Regression tests for generated railroad TryGet/TrySet methods, auto-generated symmetric
/// IBenchDigitalMultimeter / IBenchPowerSupply / IBenchThermalController adapters across
/// the clean-room instrument catalog, and .NET 11 SIMD WaveformAnalyzer metrics.
/// </summary>
public sealed class GeneratedAdaptersAndSimdTests
{
    /// <summary>Verify generated Keysight34461A, Keithley2450, and LakeShore336 plug symmetrically into BenchSession via AsBench* adapters.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task GeneratedDriversPlugSymmetricallyIntoBenchSessionAndReturnRailroadOutcomes()
    {
        var dmmMock = new Keysight34461AMock();
        dmmMock.SetMockDcVoltage(1, ElectricPotential.FromVolts(3.304));
        dmmMock.SetMockResistance(1, ElectricResistance.FromOhms(120.5));

        var smuMock = new Keithley2450Mock();
        smuMock.SetMockMeasuredVoltage(1, ElectricPotential.FromVolts(5.0));
        smuMock.SetMockMeasuredCurrent(1, ElectricCurrent.FromAmperes(0.25));

        var thermalMock = new LakeShore336Mock();
        thermalMock.SetMockMeasuredTemperature(1, Temperature.FromDegreesCelsius(-40.0));

        var dmmDriver = dmmMock.CreateDriver();
        var smuDriver = smuMock.CreateDriver();
        var thermalDriver = thermalMock.CreateDriver();

        // Direct railroad TrySet validation error on out-of-range setpoint (no exception thrown!).
        var outOfRange = await smuDriver.TrySetSourceVoltageAsync(1, ElectricPotential.FromVolts(500.0));
        Assert.True(outOfRange.IsFailure);
        Assert.Equal(BenchErrorKind.Validation, outOfRange.Error.Kind);
        Assert.Equal("BF-GEN-RANGE", outOfRange.Error.Code);

        // Plug generated drivers straight into BenchSession via auto-generated AsBench* adapters!
        await using var bench = BenchSession.Create("GeneratedCatalogBench")
            .AddInstrument(dmmDriver.AsBenchDmm("DMM1", InstrumentBackend.Simulated))
            .AddInstrument(smuDriver.AsBenchPowerSupply("PSU1", InstrumentBackend.Simulated))
            .AddInstrument(thermalDriver.AsBenchThermalController("THERM1", InstrumentBackend.Simulated))
            .Build();

        var vRead = await bench.Dmm("DMM1").MeasureDcVoltageAsync();
        Assert.True(vRead.IsSuccess);
        Assert.Equal(3.304, vRead.Value.Volts, precision: 6);

        var setPsu = await bench.PowerSupply("PSU1").SetVoltageAsync(ElectricPotential.FromVolts(5.0));
        Assert.True(setPsu.IsSuccess);
        var getPsu = await bench.PowerSupply("PSU1").GetVoltageAsync();
        Assert.Equal(5.0, getPsu.Value.Volts, precision: 6);

        var setTemp = await bench.Thermal("THERM1").SetSetpointAsync(Temperature.FromDegreesCelsius(-40.0));
        Assert.True(setTemp.IsSuccess);
        var readTemp = await bench.Thermal("THERM1").MeasureTemperatureAsync();
        Assert.Equal(-40.0, readTemp.Value.DegreesCelsius, precision: 6);

        var manifest = bench.DescribeJson();
        Assert.Contains("Keysight34461A", manifest);
        Assert.Contains("Keithley2450", manifest);
        Assert.Contains("LakeShore336", manifest);
    }

    /// <summary>Verify SIMD WaveformAnalyzer computes accurate DC mean, Vpp, RMS, and AC ripple and catches bad waveforms on the failure track.</summary>
    [Fact]
    public void SimdWaveformAnalyzerComputesAccurateMetricsAndEnforcesFlightRippleLimits()
    {
        Assert.Equal(BenchErrorKind.Validation, WaveformAnalyzer.Analyze([]).Error.Kind);
        Assert.Equal(BenchErrorKind.Validation, WaveformAnalyzer.Analyze([1.0, double.NaN, 2.0]).Error.Kind);

        // Construct a 3.300 V rail with a clean 20 mVpp (10 mV amplitude) sine ripple over 4096 samples.
        const int n = 4096;
        var samples = new double[n];
        for (int i = 0; i < n; i++)
            samples[i] = 3.300 + 0.010 * Math.Sin(2.0 * Math.PI * i / 64.0);

        var pass = WaveformAnalyzer.VerifyPowerRailRipple(
            samples,
            minMeanVolts: 3.25,
            maxMeanVolts: 3.35,
            maxPeakToPeakVolts: 0.025,
            maxAcRmsVolts: 0.010,
            resourceId: "SCOPE_CH1");

        Assert.True(pass.IsSuccess);
        Assert.Equal(n, pass.Value.SampleCount);
        Assert.Equal(3.300, pass.Value.Mean, precision: 5);
        Assert.InRange(pass.Value.PeakToPeak, 0.0199, 0.0201);
        Assert.InRange(pass.Value.AcRms, 0.0070, 0.00715); // 0.010 / sqrt(2) ~= 0.007071

        // Tighter 10 mVpp limit fails with structured LimitExceeded error.
        var fail = WaveformAnalyzer.VerifyPowerRailRipple(
            samples,
            minMeanVolts: 3.25,
            maxMeanVolts: 3.35,
            maxPeakToPeakVolts: 0.010,
            resourceId: "SCOPE_CH1");

        Assert.True(fail.IsFailure);
        Assert.Equal(BenchErrorKind.LimitExceeded, fail.Error.Kind);
        Assert.Equal("BF-WAVE-RIPPLE-VPP", fail.Error.Code);
    }
}
