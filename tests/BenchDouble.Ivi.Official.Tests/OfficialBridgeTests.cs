using BenchDouble.Ivi;
using BenchDouble.Ivi.Official;
using NSubstitute;
using Xunit;

namespace BenchDouble.Ivi.Official.Tests;

/// <summary>
/// Tests using the actual official IVI Foundation DMM/FGEN interfaces;
/// no native VISA or physical instruments are required.
/// </summary>
public sealed class OfficialBridgeTests
{
    [Fact]
    public void ActualOfficialInterfacesAreLoaded()
    {
        Assert.True(OfficialIviCompatibility.DmmInterface.IsInterface);
        Assert.True(OfficialIviCompatibility.FgenInterface.IsInterface);
        Assert.NotEmpty(OfficialIviCompatibility.Describe(OfficialIviCompatibility.DmmInterface));
        Assert.NotEmpty(OfficialIviCompatibility.Describe(OfficialIviCompatibility.FgenInterface));
    }

    [Fact]
    public void RejectsNonIviObjects()
    {
        Assert.Throws<ArgumentException>(() => OfficialIviCompatibility.RequireOfficialDmm(new object()));
        Assert.Throws<ArgumentException>(() => OfficialIviCompatibility.RequireOfficialFunctionGenerator(new object()));
    }

    [Fact]
    public void DmmBridgeInvokesInjectedOfficialOperations()
    {
        var driver = Substitute.For<Ivi.Dmm.IIviDmm>();
        int configured = 0;
        int measured = 0;
        using IIviDigitalMultimeter meter = new OfficialDmmBridge(
            driver,
            (d, function, range, resolution) =>
            {
                Assert.Same(driver, d);
                Assert.Equal(IviMeasurementFunction.DcVoltage, function);
                configured++;
            },
            d => { Assert.Same(driver, d); measured++; return 4.997; },
            d => Assert.Same(driver, d),
            d => "Official IVI DMM");
        Assert.Equal("Official IVI DMM", meter.Identity);
        meter.ConfigureMeasurement(IviMeasurementFunction.DcVoltage);
        Assert.Equal(4.997, meter.Measure(), 6);
        meter.Reset();
        Assert.Equal(1, configured);
        Assert.Equal(1, measured);
    }

    [Fact]
    public void FgenBridgePreservesTypedCallsAndRejectsInvalidValues()
    {
        var driver = Substitute.For<Ivi.Fgen.IIviFgen>();
        int configured = 0;
        int enabled = 0;
        using IIviFunctionGenerator source = new OfficialFgenBridge(
            driver,
            (d, channel, waveform, hz, vpp) =>
            {
                Assert.Same(driver, d);
                Assert.Equal(2, channel);
                Assert.Equal(IviWaveform.Sine, waveform);
                Assert.Equal(1000.0, hz);
                Assert.Equal(1.25, vpp);
                configured++;
            },
            (d, channel, state) =>
            {
                Assert.Same(driver, d);
                Assert.Equal(2, channel);
                Assert.True(state);
                enabled++;
            },
            d => Assert.Same(driver, d),
            d => "Official IVI FGEN");
        source.ConfigureWaveform(2, IviWaveform.Sine, 1000, 1.25);
        source.SetOutputEnabled(2, true);
        Assert.Equal("Official IVI FGEN", source.Identity);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.ConfigureWaveform(0, IviWaveform.Sine, 1000, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.ConfigureWaveform(1, IviWaveform.Sine, double.NaN, 1));
        Assert.Equal(1, configured);
        Assert.Equal(1, enabled);
    }
}
