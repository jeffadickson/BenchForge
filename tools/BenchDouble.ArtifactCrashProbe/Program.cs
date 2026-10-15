using BenchDouble.Artifacts;
using BenchDouble.Artifacts.Sqlite;

// Deliberately terminate without stack unwinding during a second transaction.
// Invoked only with a new temporary database by check_artifact_recovery.py.
if (args.Length != 1 || File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: ArtifactCrashProbe <new-database-path>");
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
    public override long Position {get => 0;set => throw new NotSupportedException();}
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct=default)
    {
        if (++reads == 3) Environment.Exit(97);
        buffer.Span.Fill(0x5a); return ValueTask.FromResult(buffer.Length);
    }
    public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();
    public override void Flush()=>throw new NotSupportedException();
    public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();
    public override void SetLength(long v)=>throw new NotSupportedException();
    public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
}
