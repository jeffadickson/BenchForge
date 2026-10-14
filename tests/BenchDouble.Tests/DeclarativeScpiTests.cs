using BenchDouble;
using BenchDouble.Declarative;
using NSubstitute;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Tests command generation, validation, codec conversion and channel binding.</summary>
public class DeclarativeScpiTests
{
    private static readonly ScpiParameterDefinition<double> Frequency =
        ScpiParameter.Control(
            new ParameterMetadata("frequency", "Channel frequency", "Hz", 1, 20_000_000),
            "SOUR{channel}:FREQ?", "SOUR{channel}:FREQ {value}",
            DoubleCodec.Instance);

    [Fact]
    public void ChannelBindingGeneratesAndParsesScpi()
    {
        var bus = Substitute.For<IScpiSession>();
        bus.Query("SOUR2:FREQ?").Returns("1.25E3");
        var frequency = new ScpiChannelGroup(bus).Channel("2").Parameter(Frequency);
        frequency.Set(1250);
        Assert.Equal(1250, frequency.Get());
        bus.Received(1).Write("SOUR2:FREQ 1250");
        bus.Received(1).Query("SOUR2:FREQ?");
    }

    [Fact]
    public void OutOfRangeValuesAreRejectedWithoutWriting()
    {
        var bus = Substitute.For<IScpiSession>();
        var frequency = Frequency.Bind(bus, ScpiAddress.Root.Child("SOUR", "1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => frequency.Set(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => frequency.Set(30_000_000));
        bus.DidNotReceive().Write(Arg.Any<string>());
    }

    [Fact]
    public void ReadOnlyAndWriteOnlyParametersEnforceAccess()
    {
        var bus = Substitute.For<IScpiSession>();
        var voltage = ScpiParameter.Measurement(
            new ParameterMetadata("voltage", "Measured voltage", "V"),
            "MEAS:VOLT:DC?", DoubleCodec.Instance).Bind(bus);
        Assert.Throws<NotSupportedException>(() => voltage.Set(5));

        var output = ScpiParameter.Setting(
            new ParameterMetadata("output", "Output state"),
            "OUTP1 {value}", BooleanCodec.Instance).Bind(bus);
        Assert.Throws<NotSupportedException>(() => output.Get());
        output.Set(true);
        bus.Received(1).Write("OUTP1 ON");
    }

    [Fact]
    public void BooleanParserAcceptsStandardScpiForms()
    {
        Assert.True(BooleanCodec.Instance.Parse("1"));
        Assert.True(BooleanCodec.Instance.Parse("ON"));
        Assert.False(BooleanCodec.Instance.Parse("0"));
        Assert.False(BooleanCodec.Instance.Parse("OFF"));
        Assert.Throws<FormatException>(() => BooleanCodec.Instance.Parse("UNKNOWN"));
    }

    [Fact]
    public void MissingChannelAndUnsafeIdentifiersFail()
    {
        var bus = Substitute.For<IScpiSession>();
        Assert.Throws<InvalidOperationException>(() => Frequency.Bind(bus).Get());
        Assert.Throws<ArgumentException>(() => ScpiAddress.Root.Child("SOUR", "1;*RST"));
    }
}
