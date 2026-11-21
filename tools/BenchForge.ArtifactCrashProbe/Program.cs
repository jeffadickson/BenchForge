using System.Security.Cryptography;
using BenchForge.Artifacts;
using BenchForge.Artifacts.Sqlite;
using Microsoft.Data.Sqlite;

// Deliberately terminate without stack unwinding during a second transaction,
// or verify SQLite recovery integrity when invoked with --verify <database-path>.
if (args.Length == 2 && args[0] == "--verify")
{
    var dbPath = Path.GetFullPath(args[1]);
    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Database not found: {dbPath}");
        return 2;
    }
    using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
    connection.Open();
    using (var integrity = connection.CreateCommand())
    {
        integrity.CommandText = "PRAGMA integrity_check";
        if (!string.Equals(integrity.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("SQLite integrity check failed after process exit.");
            return 1;
        }
    }
    string? artifactId = null;
    byte[]? payload = null;
    string? digest = null;
    int artifactCount = 0;
    using (var query = connection.CreateCommand())
    {
        query.CommandText = "SELECT artifact_id, payload, sha256 FROM artifacts";
        using var reader = query.ExecuteReader();
        while (reader.Read())
        {
            artifactCount++;
            artifactId = reader.GetString(0);
            payload = (byte[])reader[1];
            digest = reader.GetString(2);
        }
    }
    if (artifactCount != 1 || payload is null || !payload.SequenceEqual(new byte[] { 1, 2, 3 }))
    {
        Console.Error.WriteLine("Interrupted capture leaked or committed capture was lost.");
        return 1;
    }
    var expectedDigest = Convert.ToHexString(SHA256.HashData(payload));
    if (!string.Equals(digest, expectedDigest, StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Committed payload digest mismatch.");
        return 1;
    }
    var outboxRows = new List<(string Id, string State)>();
    using (var outbox = connection.CreateCommand())
    {
        outbox.CommandText = "SELECT artifact_id, state FROM artifact_outbox";
        using var reader = outbox.ExecuteReader();
        while (reader.Read())
            outboxRows.Add((reader.GetString(0), reader.GetString(1)));
    }
    if (outboxRows.Count != 1 || outboxRows[0] != (artifactId!, "pending"))
    {
        Console.Error.WriteLine("Outbox is inconsistent with committed captures.");
        return 1;
    }
    return 0;
}

if (args.Length != 1 || File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: ArtifactCrashProbe <new-database-path> | --verify <database-path>");
    return 2;
}
var store = SqliteArtifactStore.Open(args[0]);
var descriptor = new CaptureDescriptor("CRASH-RUN", "ACQ", "SCOPE", DateTimeOffset.UtcNow,
    "application/octet-stream", "attachment");
await store.SaveAsync(Guid.NewGuid(), descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3);
await store.SaveAsync(Guid.NewGuid(), descriptor, new ExitStream(), 2 * 1024 * 1024);
return 98; // Reaching this line means the crash injection did not execute.

sealed class ExitStream : Stream
{
    int reads;
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (++reads == 3) Environment.Exit(97);
        buffer.Span.Fill(0x5a); return ValueTask.FromResult(buffer.Length);
    }
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
}
