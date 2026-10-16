using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Host epoch and generation rejection tests.</summary>
public sealed class HostedScopeFenceTests
{
    /// <summary>Verify wrong epoch or generation rejects acquisition.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task WrongEpochOrGenerationRejectsAcquisition()
    {
        var path = Path.Combine(Path.GetTempPath(), "benchforge-fence-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var ledger = new HostFencingLedger(path);
            var host = new HostedOscilloscopeRegistry(ledger: ledger);
            var lease = host.Reserve("SCOPE1")!.Value;
            var fence = host.Fence("SCOPE1", lease)!.Value;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await host.AcquireAsync("SCOPE1", lease, 1, expectedEpoch: fence.Epoch + 1, expectedGeneration: fence.Generation));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await host.AcquireAsync("SCOPE1", lease, 1, expectedEpoch: fence.Epoch, expectedGeneration: fence.Generation + 1));
            var capture = await host.AcquireAsync("SCOPE1", lease, 1,
                expectedEpoch: fence.Epoch, expectedGeneration: fence.Generation);
            Assert.Equal(128, capture.SampleCount);
        }
        finally { File.Delete(path); File.Delete(path + ".owner.lock"); }
    }
}
