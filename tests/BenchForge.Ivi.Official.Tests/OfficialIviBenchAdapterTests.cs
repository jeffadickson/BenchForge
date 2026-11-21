using BenchForge.Client;
using BenchForge.Contracts;
using BenchForge.Ivi.Official;
using BenchForge.Outcomes;
using NSubstitute;
using UnitsNet;
using Xunit;

namespace BenchForge.Ivi.Official.Tests;

/// <summary>
/// Verifies all nine official IVI Foundation .NET class interfaces load cleanly on .NET 11
/// and bridge seamlessly into <see cref="BenchSession"/> through railroad-oriented <c>IBench*</c> adapters.
/// </summary>
public sealed class OfficialIviBenchAdapterTests
{
    /// <summary>Verify all 9 official IVI Foundation class interfaces are loaded and inspectable.</summary>
    [Fact]
    public void AllNineOfficialIviInterfacesAreLoaded()
    {
        Assert.Equal(9, OfficialIviClassCatalog.AllInterfaces.Count);
        var described = OfficialIviClassCatalog.DescribeAll();
        foreach (var (classTag, members) in described)
        {
            Assert.True(OfficialIviClassCatalog.AllInterfaces[classTag].IsInterface, $"{classTag} must be an interface.");
            Assert.NotEmpty(members);
        }

        Assert.Throws<ArgumentException>(() => OfficialIviClassCatalog.RequireOfficial<global::Ivi.DCPwr.IIviDCPwr>(new object()));
    }

    /// <summary>Verify all 9 official IVI adapters plug into BenchSession, operate identically to mocks, and translate faults safely.</summary>
    /// <returns>Completion of all official IVI adapter assertions.</returns>
    [Fact]
    public async Task AllNineOfficialIviAdaptersOperateInsideBenchSession()
    {
        var dmm = Substitute.For<global::Ivi.Dmm.IIviDmm>();
        var psu = Substitute.For<global::Ivi.DCPwr.IIviDCPwr>();
        var fgen = Substitute.For<global::Ivi.Fgen.IIviFgen>();
        var scope = Substitute.For<global::Ivi.Scope.IIviScope>();
        var sw = Substitute.For<global::Ivi.Swtch.IIviSwtch>();
        var rfsg = Substitute.For<global::Ivi.RFSigGen.IIviRFSigGen>();
        var specan = Substitute.For<global::Ivi.SpecAn.IIviSpecAn>();
        var pm = Substitute.For<global::Ivi.PwrMeter.IIviPwrMeter>();
        var counter = Substitute.For<global::Ivi.Counter.IIviCounter>();

        double psuV = 0.0, psuI = 1.0, fgenHz = 1000.0, fgenVpp = 1.0, rfHz = 1e9, rfW = 0.001, saCenter = 1e9;
        bool psuOn = false;
        var routes = new List<string>();
        bool failNextPsuWrite = false;

        var id = new InstrumentIdentity("IVI-Vendor", "Model-X", "SN-001", "2.1");

        await using var bench = BenchSession.Create("official-ivi-bench")
            .AddInstrument(new OfficialIviDmmBenchAdapter("dmm", dmm, id, _ => 28.05, _ => 1.25, _ => 49.9))
            .AddInstrument(new OfficialIviDcPwrBenchAdapter(
                "psu", psu, id,
                (_, _, v) => { if (failNextPsuWrite) { failNextPsuWrite = false; throw new TimeoutException("VISA write timed out"); } psuV = v; },
                (_, _) => psuV,
                (_, _, i) => psuI = i,
                (_, _) => psuI,
                (_, _) => psuOn ? psuV : 0.0,
                (_, _) => psuOn ? 0.5 : 0.0,
                (_, _, on) => psuOn = on,
                (_, _) => psuOn))
            .AddInstrument(new OfficialIviFgenBenchAdapter("fgen", fgen, id, (_, _, f) => fgenHz = f, (_, _) => fgenHz, (_, _, a) => fgenVpp = a, (_, _) => fgenVpp, (_, _, _) => { }))
            .AddInstrument(new OfficialIviScopeBenchAdapter("scope", scope, id, (_, _, _) => { }, (_, _, _) => { }, (_, _) => [0.0, 1.0, 0.0, -1.0]))
            .AddInstrument(new OfficialIviSwitchBenchAdapter("sw", sw, id, (_, a, b) => routes.Add($"{a}->{b}"), (_, a, b) => routes.Remove($"{a}->{b}"), _ => routes.Clear(), _ => routes.ToArray()))
            .AddInstrument(new OfficialIviRfSigGenBenchAdapter("rfsg", rfsg, id, (_, f) => rfHz = f, _ => rfHz, (_, w) => rfW = w, _ => rfW, (_, _) => { }))
            .AddInstrument(new OfficialIviSpecAnBenchAdapter("specan", specan, id, (_, c) => saCenter = c, _ => saCenter, (_, _) => { }, (_, _) => { }))
            .AddInstrument(new OfficialIviRfPowerMeterBenchAdapter("pwrmeter", pm, id, (_, _, _) => { }, (_, _) => 0.002))
            .AddInstrument(new OfficialIviCounterBenchAdapter("counter", counter, id, (_, _) => 10e6, (_, _) => 1e-7))
            .Build();

        Assert.Equal(28.05, (await bench.Dmm("dmm").MeasureDcVoltageAsync()).Value.Volts, 3);
        Assert.True((await bench.PowerSupply("psu").SetVoltageAsync(ElectricPotential.FromVolts(28.0))).IsSuccess);
        Assert.True((await bench.PowerSupply("psu").SetOutputEnabledAsync(true)).IsSuccess);
        Assert.Equal(28.0, (await bench.PowerSupply("psu").MeasureVoltageAsync()).Value.Volts, 3);

        Assert.True((await bench.FunctionGenerator("fgen").SetFrequencyAsync(Frequency.FromKilohertz(50))).IsSuccess);
        Assert.Equal(50e3, (await bench.FunctionGenerator("fgen").GetFrequencyAsync()).Value.Hertz, 1);

        Assert.Equal(4, (await bench.Oscilloscope("scope").AcquireWaveformVoltsAsync()).Value.Length);
        Assert.True((await bench.SwitchMatrix("sw").ConnectAsync("P1", "P2")).IsSuccess);
        Assert.Single((await bench.SwitchMatrix("sw").GetClosedRoutesAsync()).Value);

        Assert.True((await bench.RfSignalGenerator("rfsg").SetFrequencyAsync(Frequency.FromGigahertz(5.8))).IsSuccess);
        Assert.Equal(5.8e9, (await bench.RfSignalGenerator("rfsg").GetFrequencyAsync()).Value.Hertz, 1);

        Assert.True((await bench.SpectrumAnalyzer("specan").SetCenterFrequencyAsync(Frequency.FromGigahertz(5.8))).IsSuccess);
        Assert.Equal(5.8e9, (await bench.SpectrumAnalyzer("specan").GetCenterFrequencyAsync()).Value.Hertz, 1);

        Assert.Equal(0.002, (double)(await bench.RfPowerMeter("pwrmeter").MeasurePowerAsync()).Value.Watts, 6);
        Assert.Equal(10e6, (await bench.FrequencyCounter("counter").MeasureFrequencyAsync()).Value.Hertz, 1);

        // Verify vendor timeout translates honestly to RequiresRecovery = true with uncertain physical state
        failNextPsuWrite = true;
        var fault = await bench.PowerSupply("psu").SetVoltageAsync(ElectricPotential.FromVolts(12.0));
        Assert.True(fault.IsFailure);
        Assert.Equal(BenchErrorKind.Timeout, fault.Error.Kind);
        Assert.True(fault.Error.RequiresRecovery);
        Assert.Equal(PhysicalStateConfidence.UnknownRequiresRecovery, fault.Error.Confidence);

        // EmergencySafeAllAsync opens relays and disables PSU output cleanly
        Assert.True((await bench.EmergencySafeAllAsync()).IsSuccess);
        Assert.Empty((await bench.SwitchMatrix("sw").GetClosedRoutesAsync()).Value);
        Assert.False((await bench.PowerSupply("psu").GetOutputEnabledAsync()).Value);
    }
}
