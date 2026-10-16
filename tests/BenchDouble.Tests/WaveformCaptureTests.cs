using System.Buffers.Binary;
using BenchDouble.Oscilloscopes;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Hardware-free verification of SCPI binary framing and waveform scaling.</summary>
public sealed class WaveformCaptureTests
{
    /// <summary>Verify binary block decodes exact payload.</summary>
    [Fact]
    public void BinaryBlockDecodesExactPayload()
    {
        Assert.Equal(new byte[] { 1, 2, 3 }, ScpiBinaryBlock.Decode("#13\x01\x02\x03\n"u8));
    }
    /// <summary>Verify invalid blocks are rejected.</summary>
    /// <param name="input">Binary framing test input.</param>

    [Theory]
    [InlineData("#0")]
    [InlineData("#13ab")]
    [InlineData("#1x")]
    [InlineData("#11ax")]
    public void InvalidBlocksAreRejected(string input)
    {
        Assert.ThrowsAny<Exception>(() => ScpiBinaryBlock.Decode(System.Text.Encoding.ASCII.GetBytes(input)));
    }
    /// <summary>Verify binary block enforces limit.</summary>

    [Fact]
    public void BinaryBlockEnforcesLimit()
    {
        Assert.Throws<InvalidDataException>(() => ScpiBinaryBlock.Decode("#13abc"u8, 2));
    }
    /// <summary>Verify samples preserve trigger relative time and voltage.</summary>
    /// <param name="littleEndian">Whether sample bytes are little-endian.</param>

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SamplesPreserveTriggerRelativeTimeAndVoltage(bool littleEndian)
    {
        var raw = new byte[4];
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt16LittleEndian(raw, 100);
            BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(2), -100);
        }
        else
        {
            BinaryPrimitives.WriteInt16BigEndian(raw, 100);
            BinaryPrimitives.WriteInt16BigEndian(raw.AsSpan(2), -100);
        }
        var capture = new WaveformCapture(Guid.NewGuid(), 1,
            new WaveformPreamble(1e-6, -2e-6, 0, 0.01, 0, 0), raw, littleEndian);
        Assert.Equal(2, capture.SampleCount);
        Assert.Equal(-2e-6, capture.TimeAt(0).Seconds, 12);
        Assert.Equal(-1e-6, capture.TimeAt(1).Seconds, 12);
        Assert.Equal(1, capture.VoltageAt(0).Volts, 12);
        Assert.Equal(-1, capture.VoltageAt(1).Volts, 12);
        raw[0] = 0; // constructor must copy caller-owned bytes
        Assert.Equal(1, capture.VoltageAt(0).Volts, 12);
    }
    /// <summary>Verify odd sample bytes are rejected.</summary>

    [Fact]
    public void OddSampleBytesAreRejected()
    {
        Assert.Throws<FormatException>(() => new WaveformCapture(Guid.NewGuid(), 1,
            new WaveformPreamble(1, 0, 0, 1, 0, 0), new byte[] { 1 }, true));
    }
}
