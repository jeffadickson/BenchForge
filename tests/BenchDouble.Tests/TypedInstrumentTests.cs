using BenchDouble;
using BenchDouble.Quantities;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class TypedInstrumentTests
{
    [Fact]
    public void VoltageMeasurementHasUnitsAndConverts()
    {
        var device = new MultimeterSimulator { InjectedVoltage = 5.012 };
        var meter = new TypedDmmAdapter(new DigitalMultimeter(device));
        ElectricPotential voltage = meter.MeasureDcVoltage();
        Assert.Equal(5.012, voltage.Volts, 6);
        Assert.Equal(5012, voltage.Millivolts, 3);
    }

    [Fact]
    public void GeneratorConvertsKilohertzAndMillivoltsToScpiBaseUnits()
    {
        var device = new GeneratorSimulator();
        var source = new TypedFunctionGeneratorAdapter(new FunctionGenerator(device));
        source.ConfigureSine(Frequency.FromKilohertz(10), ElectricPotential.FromMillivolts(500), 2);
        Assert.Equal(10000, device.Frequencies[2]);
        Assert.Equal(0.5, device.Amplitudes[2], 6);
    }

    [Fact]
    public void UnsupportedMeterFunctionsFailExplicitly()
    {
        var meter = new TypedDmmAdapter(new DigitalMultimeter(new MultimeterSimulator()));
        Assert.Throws<NotSupportedException>(() => meter.MeasureResistance());
        Assert.Throws<NotSupportedException>(() => meter.MeasureDcCurrent());
    }
}
