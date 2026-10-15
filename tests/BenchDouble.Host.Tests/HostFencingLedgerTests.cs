using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Durable epoch and generation tests without real instruments.</summary>
public sealed class HostFencingLedgerTests
{
    [Fact]
    public void RestartAdvancesEpochAndPreservesGeneration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "benchforge-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fencing.json");
        try
        {
            var first = new HostFencingLedger(path);
            Assert.Equal(1, first.Epoch);
            Assert.Equal(1, first.NextGeneration("SCOPE1"));
            Assert.Equal(2, first.NextGeneration("SCOPE1"));
            var restarted = new HostFencingLedger(path);
            Assert.Equal(2, restarted.Epoch);
            Assert.Equal(3, restarted.NextGeneration("SCOPE1"));
            Assert.Equal(1, restarted.NextGeneration("DMM1"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void CorruptedLedgerFailsClosed()
    {
        var path = Path.Combine(Path.GetTempPath(), "benchforge-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "{broken");
            Assert.ThrowsAny<Exception>(() => new HostFencingLedger(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RegistryCanAllocateFromDurableLedger()
    {
        var path = Path.Combine(Path.GetTempPath(), "benchforge-generation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ledger = new HostFencingLedger(path);
            var host = new HostedOscilloscopeRegistry(ledger: ledger);
            var lease = host.Reserve("SCOPE1")!.Value;
            Assert.Equal(1, host.Generation("SCOPE1"));
            Assert.True(host.Release("SCOPE1", lease));
            host.Reserve("SCOPE1");
            Assert.Equal(2, host.Generation("SCOPE1"));
        }
        finally { File.Delete(path); }
    }
}
