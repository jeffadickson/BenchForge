using BenchDouble;
using BenchDouble.Contracts;
using Xunit;

namespace BenchDouble.Tests;

public class InstrumentContractTests
{
    [Fact]
    public void GenericDmmWrapsStatefulScpiSimulator()
    {
        var simulation = new MultimeterSimulator { InjectedVoltage = 4.999 };
        using IDigitalMultimeter meter = new BasicDmmAdapter(simulation);
        Assert.Equal(InstrumentBackend.Simulated, meter.Backend);
        Assert.Equal("34461A", meter.Identity.Model);
        meter.Configure(MeasurementFunction.DcVoltage);
        Assert.Equal(4.999, meter.Measure(), 6);
    }

    [Fact]
    public void GenericGeneratorExposesIndependentChannels()
    {
        var simulation = new GeneratorSimulator();
        using IFunctionGenerator generator = new BasicFunctionGeneratorAdapter(simulation);
        Assert.Equal(2, generator.Channels.Count);
        generator.Channels[1].ConfigureSine(1000, 1.5);
        generator.Channels[1].SetEnabled(true);
        Assert.True(simulation.Outputs[2]);
        Assert.Equal(1.5, simulation.Amplitudes[2]);
    }

    [Fact]
    public void UnsupportedCapabilitiesFailExplicitly()
    {
        using IDigitalMultimeter meter = new BasicDmmAdapter(new MultimeterSimulator());
        Assert.Throws<NotSupportedException>(() => meter.Configure(MeasurementFunction.Resistance));
        using IFunctionGenerator generator = new BasicFunctionGeneratorAdapter(new GeneratorSimulator());
        Assert.Throws<NotSupportedException>(() => generator.Channels[0].ConfigureSine(1000, 1, 0.1));
    }
}
