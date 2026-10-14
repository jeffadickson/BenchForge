using BenchDouble;
using BenchDouble.Ivi;
using Xunit;

namespace BenchDouble.Tests;

public class IviAdapterTests
{
    [Fact]
    public void GeneratorCanBeControlledThroughIviInspiredClassContract()
    {
        var physicalModel = new GeneratorSimulator();
        IIviFunctionGenerator generator = new ScpiFunctionGeneratorAdapter(physicalModel, simulationEnabled: true);
        generator.ConfigureWaveform(2, IviWaveform.Sine, 10000, 2.5);
        generator.SetOutputEnabled(2, true);
        Assert.Equal(10000, physicalModel.Frequencies[2]);
        Assert.Equal(2.5, physicalModel.Amplitudes[2]);
        Assert.True(physicalModel.Outputs[2]);
        Assert.True(generator.SimulationEnabled);
    }

    [Fact]
    public void DmmMeasuresThroughIviInspiredClassContract()
    {
        var model = new MultimeterSimulator { InjectedVoltage = 4.998 };
        IIviDigitalMultimeter meter = new ScpiDigitalMultimeterAdapter(model, true);
        meter.ConfigureMeasurement(IviMeasurementFunction.DcVoltage);
        Assert.Equal(4.998, meter.Measure(), 6);
    }

    [Fact]
    public void UnsupportedFeaturesAreRejectedInsteadOfSilentlyAccepted()
    {
        IIviFunctionGenerator generator = new ScpiFunctionGeneratorAdapter(new GeneratorSimulator());
        Assert.Throws<NotSupportedException>(() => generator.ConfigureWaveform(1, IviWaveform.Square, 1000, 1));
        IIviDigitalMultimeter meter = new ScpiDigitalMultimeterAdapter(new MultimeterSimulator());
        Assert.Throws<NotSupportedException>(() => meter.ConfigureMeasurement(IviMeasurementFunction.Resistance));
        Assert.Throws<NotSupportedException>(() => meter.ConfigureMeasurement(IviMeasurementFunction.DcVoltage, range: 10));
    }

    [Fact]
    public void ClassLevelSequenceCanBeVerifiedAtScpiBoundary()
    {
        var replay = new StrictSession(new[]
        {
            new ExpectedExchange("CONF:VOLT:DC"),
            new ExpectedExchange("MEAS:VOLT:DC?", true, "5.01")
        });
        IIviDigitalMultimeter meter = new ScpiDigitalMultimeterAdapter(replay);
        meter.ConfigureMeasurement(IviMeasurementFunction.DcVoltage);
        Assert.Equal(5.01, meter.Measure(), 6);
        replay.VerifyComplete();
    }
    [Fact]
    public void GeneratorResetRestoresSafeDefaultState()
    {
        var model = new GeneratorSimulator();
        IIviFunctionGenerator generator = new ScpiFunctionGeneratorAdapter(model, true);
        generator.ConfigureWaveform(1, IviWaveform.Sine, 12345, 2);
        generator.SetOutputEnabled(1, true);
        generator.Reset();
        Assert.False(model.Outputs[1]);
        Assert.Equal(1000, model.Frequencies[1]);
        Assert.Equal(1, model.Amplitudes[1]);
    }

    [Fact]
    public void DmmResetInvalidatesConfiguration()
    {
        var model = new MultimeterSimulator { InjectedVoltage = 1.2 };
        IIviDigitalMultimeter meter = new ScpiDigitalMultimeterAdapter(model, true);
        meter.ConfigureMeasurement(IviMeasurementFunction.DcVoltage);
        meter.Reset();
        Assert.False(model.Configured);
        Assert.Throws<InvalidOperationException>(() => meter.Measure());
    }

}
