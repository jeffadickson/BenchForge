using BenchDouble.Artifacts;
using BenchDouble.Artifacts.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BenchDouble.Artifacts.Tests;

/// <summary>File-backed artifact integrity, transaction rollback and bounded streaming tests.</summary>
public sealed class ArtifactStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "benchdouble-artifacts-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(_directory, "captures.sqlite");
    private static CaptureDescriptor Descriptor => new("RUN-1", "ACQ-1", "SCOPE-1", DateTimeOffset.UtcNow,
        "application/octet-stream", "waveform", "Example,Scope,123,1.0",
        new WaveformDescription("CH1", "uint8/raw/v1", 3, "V", 1e-6, -1e-6, 0.1, -1));

    /// <summary>Verify restart retrieval, original-byte hash and acquisition links for trace plus screenshot.</summary>
    /// <returns>Completion of assertions over reopened local storage.</returns>
    [Fact]
    public async Task RestartPreservesTraceAndScreenshot()
    {
        var store = SqliteArtifactStore.Open(Database);
        var descriptor = Descriptor;
        using var bytes = new MemoryStream(new byte[] { 1, 2, 3 });
        var trace = await store.SaveAsync(Guid.NewGuid(), descriptor, bytes, 3);
        Assert.True(bytes.CanRead);
        var screenshot = descriptor with { Kind = "screenshot", MediaType = "image/png", Waveform = null };
        await store.SaveAsync(Guid.NewGuid(), screenshot, new MemoryStream(new byte[] { 137, 80, 78, 71 }), 4);
        var reopened = SqliteArtifactStore.Open(Database);
        var found = await reopened.FindAsync("RUN-1", "ACQ-1");
        Assert.Equal(2, found.Count);
        using var output = new MemoryStream();
        Assert.Equal(trace, await reopened.CopyToAsync(trace.ArtifactId, output));
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
        Assert.Equal(2L, Scalar("SELECT COUNT(*) FROM artifact_outbox WHERE state='pending'"));
    }

    /// <summary>Identical retries are idempotent; byte and metadata conflicts cannot overwrite accepted data.</summary>
    /// <returns>Completion of replay and conflict assertions.</returns>
    [Fact]
    public async Task ReplayIsIdempotentAndConflictsAreRejected()
    {
        var store = SqliteArtifactStore.Open(Database);
        var id = Guid.NewGuid(); var descriptor = Descriptor;
        var first = await store.SaveAsync(id, descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3);
        Assert.Equal(first, await store.SaveAsync(id, descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(id, descriptor, new MemoryStream(new byte[] { 1, 2, 4 }), 3));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(id, descriptor with { InstrumentId = "other" }, new MemoryStream(new byte[] { 1, 2, 3 }), 3));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM artifacts"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM artifact_outbox"));
    }

    /// <summary>Length mismatches roll back payload, metadata and outbox as one transaction.</summary>
    /// <returns>Completion of truncated and extra-byte assertions.</returns>
    [Fact]
    public async Task LengthMismatchLeavesNoCapture()
    {
        var store = SqliteArtifactStore.Open(Database);
        foreach (int length in new[] { 2, 4 })
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(Guid.NewGuid(), Descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), length));
        Assert.Empty(await store.FindAsync("RUN-1", "ACQ-1"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM artifact_outbox"));
    }

    /// <summary>An input failure after partial transfer leaves no committed capture after reopening.</summary>
    /// <returns>Completion of rollback assertions.</returns>
    [Fact]
    public async Task MidStreamFailureRollsBack()
    {
        var store = SqliteArtifactStore.Open(Database);
        using var source = new PatternStream(200000, failAfter: 65536);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Guid.NewGuid(), Descriptor, source, 200000));
        Assert.Empty(await SqliteArtifactStore.Open(Database).FindAsync("RUN-1", "ACQ-1"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM artifact_outbox"));
    }

    /// <summary>Cancellation during a write rolls back; caller ownership of the stream is retained.</summary>
    /// <returns>Completion of cancellation assertions.</returns>
    [Fact]
    public async Task CancellationRollsBack()
    {
        var store = SqliteArtifactStore.Open(Database);
        using var cts = new CancellationTokenSource();
        using var source = new PatternStream(200000, cancel: cts);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Guid.NewGuid(), Descriptor, source, 200000, cts.Token));
        Assert.Empty(await store.FindAsync("RUN-1", "ACQ-1"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM artifact_outbox"));
    }

    /// <summary>Quotas reject before allocation and do not charge identical replay twice.</summary>
    /// <returns>Completion of quota assertions.</returns>
    [Fact]
    public async Task QuotasPreserveAcceptedData()
    {
        var store = SqliteArtifactStore.Open(Database, 3, 4);
        var id = Guid.NewGuid(); var descriptor = Descriptor;
        await store.SaveAsync(id, descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3);
        await store.SaveAsync(id, descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Guid.NewGuid(), descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(Guid.NewGuid(), descriptor, new MemoryStream(new byte[5]), 5));
        Assert.Single(await store.FindAsync("RUN-1", "ACQ-1"));
    }

    /// <summary>Corrupted bytes are detected during retrieval and cannot be hidden by an identical replay.</summary>
    /// <returns>Completion of integrity assertions.</returns>
    [Fact]
    public async Task CorruptionIsDetected()
    {
        var store = SqliteArtifactStore.Open(Database);
        var id = Guid.NewGuid(); var descriptor = Descriptor;
        await store.SaveAsync(id, descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3);
        Execute("UPDATE artifacts SET payload=x'010204'");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.CopyToAsync(id, Stream.Null));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(id, descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3));
    }

    /// <summary>Unseekable generated input is consumed in at most 64 KiB reads, without allocating a complete capture.</summary>
    /// <returns>Completion of streaming and digest assertions.</returns>
    [Fact]
    public async Task NonSeekableInputStreamsInBoundedChunks()
    {
        var store = SqliteArtifactStore.Open(Database);
        using var source = new PatternStream(2 * 1024 * 1024);
        var saved = await store.SaveAsync(Guid.NewGuid(), Descriptor, source, 2 * 1024 * 1024);
        Assert.InRange(source.MaximumRequest, 1, 65536);
        Assert.Equal(saved, await store.CopyToAsync(saved.ArtifactId, Stream.Null));
    }

    /// <summary>Invalid waveform metadata and unsupported future schemas fail explicitly.</summary>
    /// <returns>Completion of descriptor/schema assertions.</returns>
    [Fact]
    public async Task InvalidMetadataAndSchemaAreRejected()
    {
        var store = SqliteArtifactStore.Open(Database);
        foreach (var descriptor in new[] { Descriptor with { Waveform = null }, Descriptor with { Waveform = Descriptor.Waveform! with { SampleIntervalSeconds = double.NaN } } })
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(Guid.NewGuid(), descriptor, new MemoryStream(), 0));
        Execute("UPDATE artifact_schema SET version=2");
        Assert.Throws<InvalidDataException>(() => SqliteArtifactStore.Open(Database));
    }

    /// <summary>Missing IDs fail explicitly and acquisition lookup is scoped to the requested run.</summary>
    /// <returns>Completion of lookup assertions.</returns>
    [Fact]
    public async Task LookupDoesNotCrossRuns()
    {
        var store = SqliteArtifactStore.Open(Database);
        await store.SaveAsync(Guid.NewGuid(), Descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3);
        Assert.Empty(await store.FindAsync("OTHER-RUN", "ACQ-1"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.CopyToAsync(Guid.NewGuid(), Stream.Null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.FindAsync("RUN-1", "ACQ-1", 1001));
    }

    /// <summary>Two store instances cannot both consume the same remaining payload quota.</summary>
    /// <returns>Completion of concurrent-writer quota assertions.</returns>
    [Fact]
    public async Task ConcurrentStoresEnforceOneQuota()
    {
        var first = SqliteArtifactStore.Open(Database, 3, 3);
        var second = SqliteArtifactStore.Open(Database, 3, 3);
        var descriptor = Descriptor;
        async Task<bool> Save(IArtifactStore store)
        {
            try
            {
                await store.SaveAsync(Guid.NewGuid(), descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3);
                return true;
            }
            catch (IOException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Save(first)), Task.Run(() => Save(second)));
        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM artifacts"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM artifact_outbox"));
    }

    /// <summary>A quiescent database can move without changing application artifact identities.</summary>
    /// <returns>Completion of relocation and retrieval assertions.</returns>
    [Fact]
    public async Task QuiescentRelocationPreservesIds()
    {
        var store = SqliteArtifactStore.Open(Database);
        var saved = await store.SaveAsync(Guid.NewGuid(), Descriptor, new MemoryStream(new byte[] { 1, 2, 3 }), 3);
        // Every store operation closes its connection. This is not an online-backup example.
        var relocated = Path.Combine(_directory, "moved.sqlite");
        File.Copy(Database, relocated);
        var moved = SqliteArtifactStore.Open(relocated);
        Assert.Equal(saved, await moved.CopyToAsync(saved.ArtifactId, Stream.Null));
    }

    private object Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql;
        return command.ExecuteScalar()!;
    }
    private void Execute(string sql) => Scalar(sql);

    /// <summary>Remove this test's isolated database after all operations have closed their connections.</summary>
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class PatternStream(long length, long failAfter = long.MaxValue, CancellationTokenSource? cancel = null) : Stream
    {
        private long _position;
        public int MaximumRequest { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position >= failAfter) throw new IOException("Injected source failure.");
            MaximumRequest = Math.Max(MaximumRequest, buffer.Length);
            int count = (int)Math.Min(buffer.Length, length - _position);
            buffer.Span[..count].Fill(0x5a); _position += count;
            if (_position > 65536) cancel?.Cancel();
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
