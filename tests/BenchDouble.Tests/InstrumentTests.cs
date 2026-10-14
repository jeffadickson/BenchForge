using BenchDouble;
using NSubstitute;
using Xunit;

namespace BenchDouble.Tests;

public class InstrumentTests
{
    [Fact]
    public void GeneratorSimulatorTracksChannelState()
    {
        var simulated = new GeneratorSimulator();
        var generator = new FunctionGenerator(simulated);
        generator.SetSine(10_000, 2.5, 2);
        generator.Output(true, 2);
        Assert.Equal(10_000, simulated.Frequencies[2]);
        Assert.Equal(2.5, simulated.Amplitudes[2]);
        Assert.True(simulated.Outputs[2]);
    }

    [Fact]
    public void MultimeterSupportsInjectedReadings()
    {
        var simulated = new MultimeterSimulator { InjectedVoltage = 5.0123 };
        var meter = new DigitalMultimeter(simulated);
        meter.ConfigureDcVoltage();
        Assert.Equal(5.0123, meter.MeasureDcVoltage(), 6);
    }

    [Fact]
    public void StrictSessionFailsImmediatelyOnWrongOrder()
    {
        var mock = new StrictSession(new[]
        {
            new ExpectedExchange("CONF:VOLT:DC"),
            new ExpectedExchange("MEAS:VOLT:DC?", true, "5.0")
        });
        Assert.Throws<ScpiProtocolException>(() => mock.Query("MEAS:VOLT:DC?"));
        mock.Write("CONF:VOLT:DC");
        Assert.Equal("5.0", mock.Query("MEAS:VOLT:DC?"));
        mock.VerifyComplete();
    }

    [Fact]
    public void SharedTranscriptChecksAcrossInstruments()
    {
        var transcript = new SharedTranscript(
            ("source", new ExpectedExchange("OUTP1 ON")),
            ("meter", new ExpectedExchange("MEAS:VOLT:DC?", true, "1")),
            ("source", new ExpectedExchange("OUTP1 OFF")));
        var source = transcript.Instrument("source");
        var meter = transcript.Instrument("meter");
        Assert.Throws<ScpiProtocolException>(() => meter.Query("MEAS:VOLT:DC?"));
        source.Write("OUTP1 ON");
        Assert.Equal("1", meter.Query("MEAS:VOLT:DC?"));
        source.Write("OUTP1 OFF");
        transcript.VerifyComplete();
    }

    [Fact]
    public void TimeoutsAreOneShot()
    {
        var simulator = new MultimeterSimulator { InjectedVoltage = 1.2, FailNextOperation = true };
        Assert.Throws<TimeoutException>(() => simulator.Query("MEAS:VOLT:DC?"));
        Assert.Equal("1.2", simulator.Query("MEAS:VOLT:DC?"));
    }

    [Fact]
    public void CanUseNSubstituteForSimpleTransportMocking()
    {
        var session = Substitute.For<IScpiSession>();
        session.Query("MEAS:VOLT:DC?").Returns("2.5");
        var dmm = new DigitalMultimeter(session);
        Assert.Equal(2.5, dmm.MeasureDcVoltage());
        session.Received(1).Query("MEAS:VOLT:DC?");
    }
}
