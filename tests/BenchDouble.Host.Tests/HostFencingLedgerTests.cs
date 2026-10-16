using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Durable epoch and generation tests without real instruments.</summary>
public sealed class HostFencingLedgerTests
{
    /// <summary>Verify restart advances epoch and preserves generation.</summary>
    [Fact]
    public void RestartAdvancesEpochAndPreservesGeneration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "benchforge-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fencing.json");
        try
        {
            using var first = new HostFencingLedger(path);
            Assert.Equal(1, first.Epoch);
            Assert.Equal(1, first.NextGeneration("SCOPE1"));
            Assert.Equal(2, first.NextGeneration("SCOPE1"));
            first.Dispose();
            using var restarted = new HostFencingLedger(path);
            Assert.Equal(2, restarted.Epoch);
            Assert.Equal(3, restarted.NextGeneration("SCOPE1"));
            Assert.Equal(1, restarted.NextGeneration("DMM1"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    /// <summary>Verify concurrent ledger owner is rejected.</summary>
    [Fact]
    public void ConcurrentLedgerOwnerIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "benchforge-lock-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new HostFencingLedger(path);
            Assert.Throws<IOException>(() => new HostFencingLedger(path));
        }
        finally { File.Delete(path); File.Delete(path + ".owner.lock"); }
    }
    /// <summary>Verify corrupted ledger fails closed.</summary>
    [Fact]
    public void CorruptedLedgerFailsClosed()
    {
        var path = Path.Combine(Path.GetTempPath(), "benchforge-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "{broken");
            Assert.ThrowsAny<Exception>(() => new HostFencingLedger(path));
        }
        finally { File.Delete(path); File.Delete(path + ".owner.lock"); }
    }
    /// <summary>Verify registry can allocate from durable ledger.</summary>
    [Fact]
    public void RegistryCanAllocateFromDurableLedger()
    {
        var path = Path.Combine(Path.GetTempPath(), "benchforge-generation-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var ledger = new HostFencingLedger(path);
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
