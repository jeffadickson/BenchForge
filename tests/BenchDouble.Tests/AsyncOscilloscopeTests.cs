using BenchDouble.Oscilloscopes;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Hardware-free acquisition, framing, cancellation and mock-state tests.</summary>
public sealed class AsyncOscilloscopeTests
{
    /// <summary>Verify single acquisition produces typed waveform.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task SingleAcquisitionProducesTypedWaveform()
    {
        var scope = new AsyncOscilloscope(new SimulatedOscilloscopeSession(samples: 128));
        await scope.ConfigureTimebaseAsync(Duration.FromMicroseconds(2));
        await scope.SingleAsync();
        var capture = await scope.ReadWaveformAsync(1, Guid.NewGuid());
        Assert.Equal(128, capture.SampleCount);
        Assert.Equal(2e-6, capture.TimeAt(1).Seconds, 12);
        Assert.Equal(0, capture.VoltageAt(0).Volts, 12);
        Assert.Equal(1, capture.VoltageAt(16).Volts, 12);
    }
    /// <summary>Verify acquisition required and channels independent.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task AcquisitionRequiredAndChannelsIndependent()
    {
        var session = new SimulatedOscilloscopeSession(samples: 128);
        var scope = new AsyncOscilloscope(session);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scope.ReadWaveformAsync(1, Guid.NewGuid()));
        await scope.SingleAsync();
        var first = await scope.ReadWaveformAsync(1, Guid.NewGuid());
        var second = await scope.ReadWaveformAsync(2, Guid.NewGuid());
        Assert.NotEqual(first.VoltageAt(0).Volts, second.VoltageAt(0).Volts);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await scope.ReadWaveformAsync(3, Guid.NewGuid()));
    }
    /// <summary>Verify truncated streaming block is rejected.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task TruncatedStreamingBlockIsRejected()
    {
        await using var stream = new MemoryStream("#14ab"u8.ToArray());
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await StreamingScpiBinaryBlock.ReadAsync(stream));
    }
    /// <summary>Verify streaming limit is enforced before allocation.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task StreamingLimitIsEnforcedBeforeAllocation()
    {
        await using var stream = new MemoryStream("#14abcd"u8.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await StreamingScpiBinaryBlock.ReadAsync(stream, maxPayloadBytes: 2));
    }
    /// <summary>Verify cancellation before command does not acquire.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task CancellationBeforeCommandDoesNotAcquire()
    {
        var session = new SimulatedOscilloscopeSession();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await session.WriteAsync("SING", source.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.QueryAsync("WAV:PRE?"));
    }
}
