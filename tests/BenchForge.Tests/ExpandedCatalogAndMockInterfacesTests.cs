using BenchForge.Client;
using BenchForge.Contracts;
using BenchForge.Generated;
using BenchForge.Interfaces;
using BenchForge.Oscilloscopes;
using BenchForge.Outcomes;
using BenchForge.Simulation;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>
/// End-to-end verification of the expanded 81-instrument clean-room SCPI library,
/// all 12 IVI-aligned symmetric <see cref="IBenchInstrument"/> interfaces and stateful mocks,
/// and all 10 auto-generated <c>AsBench*()</c> adapters inside <see cref="BenchSession"/>.
/// </summary>
public sealed class ExpandedCatalogAndMockInterfacesTests
{
    /// <summary>Verify at least 80 clean-room generated SCPI driver/mock pairs are compiled into BenchForge.</summary>
    [Fact]
    public void GeneratedCatalogHasAtLeast80CleanRoomInstruments()
    {
        var mockTypes = typeof(Keysight34470A).Assembly.GetTypes()
            .Where(t => t.Namespace == "BenchForge.Generated" &&
                        t.IsClass && !t.IsAbstract &&
                        typeof(DefinitionDrivenScpiMockSession).IsAssignableFrom(t))
            .ToArray();

        Assert.True(mockTypes.Length >= 80, $"Expected >= 80 generated SCPI instrument pairs, found {mockTypes.Length}.");
    }

    /// <summary>Exercise all 12 symmetric IBench* interfaces and StatefulMock* classes via BenchBuilder and BenchSession.</summary>
    /// <returns>Completion of all stateful mock interface assertions.</returns>
    [Fact]
    public async Task All12SymmetricMockInterfacesOperateAndSafeStateCleanly()
    {
        var builder = BenchSession.Create("full-ivi-mock-bench")
            .AddMockSmu("psu")
            .AddMockDmm("dmm")
            .AddMockSmu("smu")
            .AddMockThermal("thermal")
            .AddMockFunctionGenerator("fgen")
            .AddMockOscilloscope("scope")
            .AddMockSwitchMatrix("sw")
            .AddMockElectronicLoad("eload")
            .AddMockRfSignalGenerator("rfsg")
            .AddMockSpectrumAnalyzer("specan")
            .AddMockRfPowerMeter("pwrmeter")
            .AddMockFrequencyCounter("counter")
            .AddMockLcrMeter("lcr");

        await using var bench = builder.Build();
        Assert.Equal(13, bench.Describe().Count);

        // Function generator
        var fgen = bench.FunctionGenerator("fgen");
        Assert.True((await fgen.SetFrequencyAsync(Frequency.FromMegahertz(10))).IsSuccess);
        Assert.True((await fgen.SetAmplitudeAsync(ElectricPotential.FromVolts(2.5))).IsSuccess);
        Assert.True((await fgen.SetOutputEnabledAsync(true)).IsSuccess);
        Assert.Equal(10e6, (await fgen.GetFrequencyAsync()).Value.Hertz, 3);
        Assert.Equal(2.5, (await fgen.GetAmplitudeAsync()).Value.Volts, 3);

        // Oscilloscope + SIMD waveform analysis
        var scope = bench.Oscilloscope("scope");
        Assert.True((await scope.SetVerticalScaleAsync(ElectricPotential.FromVolts(0.5))).IsSuccess);
        Assert.True((await scope.SetTriggerLevelAsync(ElectricPotential.FromVolts(0.1))).IsSuccess);
        var wf = await scope.AcquireWaveformVoltsAsync();
        Assert.True(wf.IsSuccess);
        Assert.True(wf.Value.Length >= 64);
        var stats = WaveformAnalyzer.Analyze(wf.Value);
        Assert.True(stats.IsSuccess && stats.Value.Rms > 0);

        // Switch matrix
        var sw = bench.SwitchMatrix("sw");
        Assert.True((await sw.ConnectAsync("BUS_A", "UUT_PIN1")).IsSuccess);
        var routes = await sw.GetClosedRoutesAsync();
        Assert.True(routes.IsSuccess);
        Assert.Single(routes.Value);
        Assert.True((await sw.DisconnectAsync("BUS_A", "UUT_PIN1")).IsSuccess);
        Assert.Empty((await sw.GetClosedRoutesAsync()).Value);
        Assert.True((await sw.ConnectAsync("BUS_A", "UUT_PIN2")).IsSuccess);

        // Electronic load
        var eload = bench.ElectronicLoad("eload");
        Assert.True((await eload.SetCurrentSinkAsync(ElectricCurrent.FromAmperes(4.0))).IsSuccess);
        Assert.True((await eload.SetInputEnabledAsync(true)).IsSuccess);
        Assert.True((await eload.MeasurePowerAsync()).Value.Watts > 0m);

        // RF Signal Generator
        var rfsg = bench.RfSignalGenerator("rfsg");
        Assert.True((await rfsg.SetFrequencyAsync(Frequency.FromGigahertz(2.4))).IsSuccess);
        Assert.True((await rfsg.SetOutputPowerAsync(Power.FromMilliwatts(10m))).IsSuccess);
        Assert.True((await rfsg.SetRfOutputEnabledAsync(true)).IsSuccess);
        Assert.Equal(2.4e9, (await rfsg.GetFrequencyAsync()).Value.Hertz, 1);

        // Spectrum Analyzer
        var specan = bench.SpectrumAnalyzer("specan");
        Assert.True((await specan.SetCenterFrequencyAsync(Frequency.FromGigahertz(2.4))).IsSuccess);
        Assert.True((await specan.SetSpanAsync(Frequency.FromMegahertz(20))).IsSuccess);
        Assert.True((await specan.SetResolutionBandwidthAsync(Frequency.FromKilohertz(10))).IsSuccess);
        Assert.Equal(2.4e9, (await specan.GetCenterFrequencyAsync()).Value.Hertz, 1);

        // RF Power Meter
        var pm = bench.RfPowerMeter("pwrmeter");
        Assert.True((await pm.SetFrequencyAsync(Frequency.FromGigahertz(2.4))).IsSuccess);
        Assert.True((await pm.MeasurePowerAsync()).IsSuccess);

        // Frequency Counter
        var counter = bench.FrequencyCounter("counter");
        var freq = await counter.MeasureFrequencyAsync();
        var period = await counter.MeasurePeriodAsync();
        Assert.True(freq.IsSuccess && freq.Value.Hertz > 0);
        Assert.True(period.IsSuccess && period.Value.Seconds > 0);

        // LCR Meter
        var lcr = bench.LcrMeter("lcr");
        Assert.True((await lcr.SetTestFrequencyAsync(Frequency.FromKilohertz(100))).IsSuccess);
        Assert.True((await lcr.SetTestVoltageAsync(ElectricPotential.FromVolts(0.5))).IsSuccess);
        Assert.True((await lcr.MeasureCapacitanceAsync()).Value.Farads > 0);
        Assert.True((await lcr.MeasureResistanceAsync()).Value.Ohms > 0);

        // Fault injection on mock control
        var mockCtl = Assert.IsAssignableFrom<IBenchMockControl>(rfsg);
        mockCtl.InjectNextFault(new BenchError("BF-SIM-FAULT", BenchErrorKind.TransportFault, "Simulated RF timeout", "rfsg", Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, RequiresRecovery: true));
        var faulted = await rfsg.SetFrequencyAsync(Frequency.FromGigahertz(3.0));
        Assert.True(faulted.IsFailure);
        Assert.Equal("BF-SIM-FAULT", faulted.Error.Code);

        // EmergencySafeAllAsync across every registered mock
        var safeReport = await bench.EmergencySafeAllAsync();
        Assert.True(safeReport.IsSuccess);
        Assert.Empty((await sw.GetClosedRoutesAsync()).Value);
    }

    /// <summary>Verify all 10 auto-generated AsBench*() adapters plug cleanly into BenchSession and operate via railroad BenchResult.</summary>
    /// <returns>Completion of all generated adapter assertions.</returns>
    [Fact]
    public async Task All10GeneratedSymmetricAdaptersWorkInsideBenchSession()
    {
        var dmmMock = new Keysight34470AMock();
        dmmMock.SetMockDcVoltage(1, ElectricPotential.FromVolts(28.012));
        var psuMock = new KeysightN8921AMock();
        var thermalMock = new LakeShore350Mock();
        var fgenMock = new TektronixAfg31000Mock();
        var rfsgMock = new KeysightN5181BMock();
        var specanMock = new KeysightN9020BMock();
        var eloadMock = new KeysightN3300AMock();
        var pmMock = new KeysightN1912AMock();
        var counterMock = new Keysight53230AMock();
        var lcrMock = new HiokiIm3536Mock();

        await using var bench = BenchSession.Create("generated-adapters-bench")
            .AddInstrument(dmmMock.CreateDriver().AsBenchDmm("dmm", InstrumentBackend.Mock))
            .AddInstrument(psuMock.CreateDriver().AsBenchPowerSupply("psu", InstrumentBackend.Mock))
            .AddInstrument(thermalMock.CreateDriver().AsBenchThermalController("thermal", InstrumentBackend.Mock))
            .AddInstrument(fgenMock.CreateDriver().AsBenchFunctionGenerator("fgen", InstrumentBackend.Mock))
            .AddInstrument(rfsgMock.CreateDriver().AsBenchRfSignalGenerator("rfsg", InstrumentBackend.Mock))
            .AddInstrument(specanMock.CreateDriver().AsBenchSpectrumAnalyzer("specan", InstrumentBackend.Mock))
            .AddInstrument(eloadMock.CreateDriver().AsBenchElectronicLoad("eload", InstrumentBackend.Mock))
            .AddInstrument(pmMock.CreateDriver().AsBenchRfPowerMeter("pwrmeter", InstrumentBackend.Mock))
            .AddInstrument(counterMock.CreateDriver().AsBenchFrequencyCounter("counter", InstrumentBackend.Mock))
            .AddInstrument(lcrMock.CreateDriver().AsBenchLcrMeter("lcr", InstrumentBackend.Mock))
            .Build();

        Assert.Equal(28.012, (await bench.Dmm("dmm").MeasureDcVoltageAsync()).Value.Volts, 3);
        Assert.True((await bench.PowerSupply("psu").SetVoltageAsync(ElectricPotential.FromVolts(28.0))).IsSuccess);
        Assert.True((await bench.Thermal("thermal").SetSetpointAsync(Temperature.FromDegreesCelsius(-40.0))).IsSuccess);
        Assert.True((await bench.FunctionGenerator("fgen").SetFrequencyAsync(Frequency.FromMegahertz(5.0), channel: 2)).IsSuccess);
        Assert.Equal(5e6, (await bench.FunctionGenerator("fgen").GetFrequencyAsync(channel: 2)).Value.Hertz, 1);
        Assert.True((await bench.RfSignalGenerator("rfsg").SetFrequencyAsync(Frequency.FromGigahertz(2.2))).IsSuccess);
        Assert.True((await bench.SpectrumAnalyzer("specan").SetCenterFrequencyAsync(Frequency.FromGigahertz(2.2))).IsSuccess);
        Assert.True((await bench.ElectronicLoad("eload").SetCurrentSinkAsync(ElectricCurrent.FromAmperes(12.5), channel: 2)).IsSuccess);
        Assert.True((await bench.RfPowerMeter("pwrmeter").SetFrequencyAsync(Frequency.FromGigahertz(2.2), channel: 2)).IsSuccess);
        Assert.True((await bench.FrequencyCounter("counter").MeasureFrequencyAsync(channel: 2)).IsSuccess);
        Assert.True((await bench.LcrMeter("lcr").SetTestFrequencyAsync(Frequency.FromKilohertz(10))).IsSuccess);

        // Bounds enforcement surfaces cleanly as a typed railroad validation error
        var overSpec = await bench.PowerSupply("psu").SetVoltageAsync(ElectricPotential.FromVolts(500.0));
        Assert.True(overSpec.IsFailure);
        Assert.Equal(BenchErrorKind.Validation, overSpec.Error.Kind);

        Assert.True((await bench.EmergencySafeAllAsync()).IsSuccess);
    }
}
