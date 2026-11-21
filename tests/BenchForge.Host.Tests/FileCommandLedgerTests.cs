using BenchForge.Contracts;
using BenchForge.Host.Server.Recovery;
using Xunit;

namespace BenchForge.Host.Tests;

/// <summary>Regression tests for durable single-writer FileCommandLedger crash recovery and quarantine.</summary>
public sealed class FileCommandLedgerTests
{
    /// <summary>Verify an in-flight Dispatched command survives restart and quarantines the resource until reconciled.</summary>
    [Fact]
    public void DispatchedCommandPersistsAcrossRestartAndQuarantinesUntilReconciled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "benchforge-cmd-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "commands.json");
        var cmdId = Guid.NewGuid();
        var lease = new LeaseFence(Guid.NewGuid(), HostEpoch: 3, Generation: 2);

        try
        {
            using (var ledger1 = new FileCommandLedger(path))
            {
                ledger1.Record(new CommandReceipt(
                    cmdId,
                    "corr-restart-1",
                    "PSU1",
                    lease,
                    "ABCDEF",
                    CommandExecutionState.Dispatched,
                    CommandFaultKind.None,
                    1,
                    null,
                    "Dispatched before simulated crash.",
                    DateTimeOffset.UtcNow));
            }

            // Re-open after simulated host crash: resource MUST remain quarantined.
            using (var ledger2 = new FileCommandLedger(path))
            {
                var recovered = ledger2.TryGet("PSU1", cmdId);
                Assert.NotNull(recovered);
                Assert.Equal(CommandExecutionState.Dispatched, recovered!.State);
                Assert.True(ledger2.RequiresRecovery("PSU1"));

                int reconciled = ledger2.ReconcileResource("PSU1", "Verified PSU output off after restart.");
                Assert.Equal(1, reconciled);
                Assert.False(ledger2.RequiresRecovery("PSU1"));
            }

            // Re-open again: reconciliation decision persists durably.
            using (var ledger3 = new FileCommandLedger(path))
            {
                Assert.False(ledger3.RequiresRecovery("PSU1"));
            }
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Verify single-writer owner lock rejects concurrent opens and corrupt ledger files fail closed.</summary>
    [Fact]
    public void RejectsConcurrentWriterAndCorruptLedgerFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "benchforge-cmd-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "commands.json");
        try
        {
            using (var first = new FileCommandLedger(path))
            {
                Assert.Throws<IOException>(() => new FileCommandLedger(path));
            }

            File.WriteAllText(path, "{corrupt json");
            Assert.ThrowsAny<Exception>(() => new FileCommandLedger(path));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
