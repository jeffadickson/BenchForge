using BenchDouble.Declarative;
using Xunit;

namespace BenchDouble.Tests;

public class DeclarativeValidationTests
{
    private enum Waveform { Sine, Square }

    [Fact]
    public void InclusiveRangeRejectsNonfiniteAndOutsideValues()
    {
        var rule = ParameterValidators.InclusiveRange(1, 100);
        Assert.True(rule(1));
        Assert.True(rule(100));
        Assert.False(rule(double.NaN));
        Assert.False(rule(double.PositiveInfinity));
        Assert.False(rule(0.9));
        Assert.False(rule(100.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ParameterValidators.InclusiveRange(5, 1));
    }

    [Fact]
    public void DiscreteValuesSupportDmmIntegrationSelections()
    {
        var supportedNplc = ParameterValidators.Discrete(0.02, 0.2, 1.0, 10.0);
        Assert.True(supportedNplc(1.0));
        Assert.False(supportedNplc(2.0));
        var combined = ParameterValidators.All(supportedNplc, ParameterValidators.InclusiveRange(0.1, 10.0));
        Assert.True(combined(1.0));
        Assert.False(combined(0.02));
    }

    [Fact]
    public void CodecMapsTypedWaveformToVendorScpiTokens()
    {
        var codec = new MappedCodec<Waveform>(
            new Dictionary<Waveform, string> { [Waveform.Sine] = "SIN", [Waveform.Square] = "SQU" });
        Assert.Equal("SIN", codec.Format(Waveform.Sine));
        Assert.Equal(Waveform.Square, codec.Parse("squ"));
        Assert.Throws<FormatException>(() => codec.Parse("TRI"));
    }
}
