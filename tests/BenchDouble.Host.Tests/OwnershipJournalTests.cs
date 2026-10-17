using BenchDouble.Host.Server.Recovery;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Persistent unresolved-ownership format and filesystem behavior.</summary>
public sealed class OwnershipJournalTests
{
    /// <summary>Route fingerprints ignore descriptor order and case but preserve distinct controllers.</summary>
    [Fact]
    public void Fingerprints_preserve_controller_scope()
    {
        Assert.Equal(OwnershipRecord.DigestRoutes(["GPIB0::22::INSTR", "TCPIP0::meter::INSTR"]),
            OwnershipRecord.DigestRoutes(["tcpip0::METER::instr", "gpib0::22::instr", "GPIB0::22::INSTR"]));
        Assert.NotEqual(OwnershipRecord.DigestRoutes(["GPIB0::22::INSTR"]), OwnershipRecord.DigestRoutes(["GPIB1::22::INSTR"]));
        var record = NewRecord();record.Validate();
        Assert.Throws<ArgumentException>(() => (record with { ResourceId = "../unsafe" }).Validate());
        Assert.Throws<ArgumentException>(() => (record with { RecordId = Guid.Empty }).Validate());
    }

    /// <summary>Ownership survives reopening and is removed only using the reviewed record ID.</summary>
    [Fact]
    public void Atomic_snapshot_roundtrips_and_checks_review()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ownership-" + Guid.NewGuid());
        try
        {
            var record = NewRecord();
            using (var journal = new FileHostOwnershipJournal(directory))
            {
                journal.Add(record);
                Assert.Throws<InvalidOperationException>(() => journal.Remove(record.ResourceId, Guid.NewGuid()));
                Assert.Equal(record, Assert.Single(journal.Read()));
            }
            using (var journal = new FileHostOwnershipJournal(directory))
            {
                Assert.Equal(record, Assert.Single(journal.Read()));
                journal.Remove(record.ResourceId, record.RecordId);
            }
            using var empty = new FileHostOwnershipJournal(directory);
            Assert.Empty(empty.Read());
            Assert.DoesNotContain(Directory.GetFiles(directory), f => f.EndsWith(".tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Only one owner can write a journal directory, and disposal never clears its evidence.</summary>
    [Fact]
    public void Single_writer_lock_is_released_on_disposal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ownership-" + Guid.NewGuid());
        try
        {
            var first = new FileHostOwnershipJournal(directory);
            first.Add(NewRecord());
            Assert.Throws<InvalidOperationException>(() => new FileHostOwnershipJournal(directory));
            first.Dispose();first.Dispose();
            Assert.Throws<ObjectDisposedException>(() => first.Read());
            using var second = new FileHostOwnershipJournal(directory);
            Assert.Single(second.Read());
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>Corrupt, duplicate, future-schema, oversized and missing state never initialize an empty journal.</summary>
    [Fact]
    public void Invalid_existing_state_fails_closed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ownership-" + Guid.NewGuid());
        try
        {
            using (var journal = new FileHostOwnershipJournal(directory)) journal.Add(NewRecord());
            var path = Path.Combine(directory, "ownership.json");
            var original = File.ReadAllText(path);
            foreach (var text in new[] { "broken", "{\"version\":1,\"Version\":1,\"records\":[]}",
                "{\"version\":2,\"records\":[]}", new string('x', 2 * 1024 * 1024 + 1) })
            {
                File.WriteAllText(path, text);
                Assert.ThrowsAny<Exception>(() => new FileHostOwnershipJournal(directory));
            }
            File.Delete(path);
            Assert.Throws<InvalidOperationException>(() => new FileHostOwnershipJournal(directory));
            File.WriteAllText(path, original);
            using var restored = new FileHostOwnershipJournal(directory);
            Assert.Single(restored.Read());
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>A failed atomic replacement poisons the writer until restart, even if the filesystem later recovers.</summary>
    [Fact]
    public void Write_failure_latches_closed_without_exposing_paths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SECRET-ownership-" + Guid.NewGuid());
        try
        {
            using var journal = new FileHostOwnershipJournal(directory);
            var path = Path.Combine(directory, "ownership.json");var original = File.ReadAllText(path);
            File.Delete(path);Directory.CreateDirectory(path);
            var error = Assert.Throws<InvalidOperationException>(() => journal.Add(NewRecord()));
            Assert.DoesNotContain("SECRET", error.Message);
            Directory.Delete(path);File.WriteAllText(path, original);
            Assert.Throws<InvalidOperationException>(() => journal.Add(NewRecord()));
            Assert.Throws<InvalidOperationException>(() => journal.Read());
        }
        finally { Directory.Delete(directory, true); }
    }

    private static OwnershipRecord NewRecord(string id = "METER") =>
        new(id, Guid.NewGuid(), OwnershipRecord.DigestRoutes(["GPIB0::22::INSTR"]), new string('a', 64), DateTimeOffset.UtcNow);
}
