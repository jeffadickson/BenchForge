using BenchDouble.Oscilloscopes;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Bounded waveform Dataflow verification.</summary>
public sealed class WaveformProcessingPipelineTests
{
    [Fact]
    public async Task ParallelRmsRetainsAcquisitionOrder()
    {
        var session = new SimulatedOscilloscopeSession(samples: 128);
        var scope = new CoordinatedOscilloscope(new OscilloscopeSessionCoordinator(session));
        var captures = new List<WaveformCapture>();
        for (int i = 0; i < 12; i++)
            captures.Add(await scope.AcquireAsync(i % 2 + 1, Guid.NewGuid()));
        var results = await WaveformProcessingPipeline.CalculateRmsAsync(captures, capacity: 2, parallelism: 4);
        Assert.Equal(captures.Select(c => c.AcquisitionId), results.Select(r => r.AcquisitionId));
        Assert.All(results, r => Assert.InRange(r.RmsVolts, 0.705, 0.709));
    }

    [Fact]
    public async Task EmptyCaptureFails()
    {
        var capture = new WaveformCapture(Guid.NewGuid(), 1,
            new WaveformPreamble(1e-6, 0, 0, 0.001, 0, 0), Array.Empty<byte>(), true);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await WaveformProcessingPipeline.CalculateRmsAsync(new[] { capture }));
    }
}
