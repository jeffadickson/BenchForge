using System.Security.Cryptography;
using System.Text.Json;
using BenchDouble.Artifacts;
using Microsoft.Data.Sqlite;

namespace BenchDouble.Artifacts.Sqlite;

/// <summary>Local immutable BLOB store with atomic metadata and outbox, bounded streaming, and SHA-256 validation.</summary>
/// <remarks>SQLite writes are synchronous and single-writer. Use away from safety-critical control threads. Each operation owns a connection; no store disposal is needed. Quotas bound logical payload bytes, not database/WAL overhead. Remote delivery is not implemented.</remarks>
public sealed class SqliteArtifactStore : IArtifactStore
{
    private const int BufferSize = 65536;
    private readonly string _connectionString;
    private readonly long _maxArtifactBytes;
    private readonly long _maxTotalBytes;

    private SqliteArtifactStore(string connectionString, long maxArtifactBytes, long maxTotalBytes)
        => (_connectionString, _maxArtifactBytes, _maxTotalBytes) = (connectionString, maxArtifactBytes, maxTotalBytes);

    /// <summary>Create or open a local schema-v1 store in WAL mode with synchronous FULL commits.</summary>
    /// <param name="path">Database path; parent directories are created. Use a local filesystem supporting SQLite locking.</param>
    /// <param name="maxArtifactBytes">Per-capture payload limit, 1 through 512 MiB; default 64 MiB is a configurable guard, not a benchmark result.</param>
    /// <param name="maxTotalBytes">Logical payload quota, at least the per-capture limit; excludes WAL and index overhead.</param>
    /// <returns>A stateless connection factory for the initialized store.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Limits are invalid.</exception>
    /// <exception cref="InvalidDataException">The database schema version is unsupported.</exception>
    /// <remarks>SQLite/IO exceptions propagate. FULL commits rely on the filesystem/device honoring flushes; power-loss testing is still required.</remarks>
    public static SqliteArtifactStore Open(string path, long maxArtifactBytes = 64L * 1024 * 1024,
        long maxTotalBytes = 1024L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxArtifactBytes is < 1 or > 512L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxArtifactBytes));
        if (maxTotalBytes < maxArtifactBytes) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var store = new SqliteArtifactStore(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath, Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, DefaultTimeout = 5
        }.ToString(), maxArtifactBytes, maxTotalBytes);
        using var connection = store.Connect();
        using var mode = connection.CreateCommand();
        mode.CommandText = "PRAGMA journal_mode=WAL;";
        if (!string.Equals(Convert.ToString(mode.ExecuteScalar()), "wal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The artifact database could not enable WAL mode.");
        using var transaction = connection.BeginTransaction();
        using var schema = connection.CreateCommand();
        schema.Transaction = transaction;
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS artifact_schema (id INTEGER PRIMARY KEY CHECK(id=1), version INTEGER NOT NULL);
            INSERT OR IGNORE INTO artifact_schema VALUES(1,1);
            SELECT version FROM artifact_schema WHERE id=1;
            """;
        if (Convert.ToInt32(schema.ExecuteScalar()) != 1) throw new InvalidDataException("Unsupported artifact schema version.");
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS artifacts (
                row_id INTEGER PRIMARY KEY, artifact_id TEXT NOT NULL UNIQUE,
                run_id TEXT NOT NULL, acquisition_id TEXT NOT NULL,
                descriptor_json TEXT NOT NULL, byte_length INTEGER NOT NULL CHECK(byte_length>=0),
                sha256 TEXT NOT NULL, payload BLOB NOT NULL);
            CREATE INDEX IF NOT EXISTS artifacts_acquisition ON artifacts(run_id, acquisition_id, artifact_id);
            CREATE TABLE IF NOT EXISTS artifact_outbox (
                artifact_id TEXT PRIMARY KEY REFERENCES artifacts(artifact_id),
                state TEXT NOT NULL CHECK(state='pending'));
            """;
        schema.ExecuteNonQuery();
        transaction.Commit();
        return store;
    }

    /// <inheritdoc />
    public async Task<ArtifactRecord> SaveAsync(Guid artifactId, CaptureDescriptor descriptor, Stream source,
        long byteLength, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Source must be readable.", nameof(source));
        if (artifactId == Guid.Empty) throw new ArgumentException("Artifact ID must not be empty.", nameof(artifactId));
        if (byteLength < 0 || byteLength > _maxArtifactBytes) throw new ArgumentOutOfRangeException(nameof(byteLength));
        var json = Validate(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Connect();
        // Immediate transaction serializes quota checks and duplicate decisions across processes.
        using var transaction = connection.BeginTransaction();
        var existing = Read(connection, transaction, artifactId);
        if (existing is { } old)
        {
            if (old.Record.ByteLength != byteLength || old.Json != json)
                throw new InvalidDataException("Artifact ID already has different metadata or length.");
            var replayHash = await Transfer(source, Stream.Null, byteLength, cancellationToken).ConfigureAwait(false);
            if (replayHash != old.Record.Sha256) throw new InvalidDataException("Artifact ID already has different bytes.");
            // Verify the persisted copy too; an intact replay must not mask corrupted storage.
            using var stored = new SqliteBlob(connection, "artifacts", "payload", old.RowId, readOnly: true);
            if (await Transfer(stored, Stream.Null, byteLength, cancellationToken).ConfigureAwait(false) != replayHash)
                throw new InvalidDataException("Stored artifact digest mismatch.");
            return old.Record;
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(SUM(byte_length),0) FROM artifacts;";
        long used = Convert.ToInt64(command.ExecuteScalar());
        if (used > _maxTotalBytes - byteLength) throw new IOException("Artifact payload quota exceeded.");
        command.CommandText = """
            INSERT INTO artifacts(artifact_id,run_id,acquisition_id,descriptor_json,byte_length,sha256,payload)
            VALUES($id,$run,$acquisition,$json,$length,'',zeroblob($length));
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$id", artifactId.ToString("D"));
        command.Parameters.AddWithValue("$run", descriptor.RunId);
        command.Parameters.AddWithValue("$acquisition", descriptor.AcquisitionId);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$length", byteLength);
        long rowId = Convert.ToInt64(command.ExecuteScalar());
        string digest;
        using (var blob = new SqliteBlob(connection, "artifacts", "payload", rowId))
            digest = await Transfer(source, blob, byteLength, cancellationToken).ConfigureAwait(false);
        command.CommandText = "UPDATE artifacts SET sha256=$hash WHERE artifact_id=$id; INSERT INTO artifact_outbox VALUES($id,'pending');";
        command.Parameters.AddWithValue("$hash", digest);
        command.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return new ArtifactRecord(artifactId, descriptor, byteLength, digest);
    }

    /// <inheritdoc />
    public async Task<ArtifactRecord> CopyToAsync(Guid artifactId, Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Destination must be writable.", nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Connect();
        using var transaction = connection.BeginTransaction(deferred: true);
        var entry = Read(connection, transaction, artifactId) ?? throw new KeyNotFoundException("Artifact not found.");
        using var blob = new SqliteBlob(connection, "artifacts", "payload", entry.RowId, readOnly: true);
        var digest = await Transfer(blob, destination, entry.Record.ByteLength, cancellationToken).ConfigureAwait(false);
        if (digest != entry.Record.Sha256) throw new InvalidDataException("Stored artifact digest mismatch; discard destination bytes.");
        return entry.Record;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ArtifactRecord>> FindAsync(string runId, string acquisitionId, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(acquisitionId);
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT artifact_id,descriptor_json,byte_length,sha256 FROM artifacts WHERE run_id=$run AND acquisition_id=$acquisition ORDER BY artifact_id LIMIT $limit;";
        command.Parameters.AddWithValue("$run", runId);
        command.Parameters.AddWithValue("$acquisition", acquisitionId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var records = new List<ArtifactRecord>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            records.Add(new ArtifactRecord(Guid.Parse(reader.GetString(0)), Decode(reader.GetString(1)), reader.GetInt64(2), reader.GetString(3)));
        }
        return Task.FromResult<IReadOnlyList<ArtifactRecord>>(records);
    }

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static (long RowId, string Json, ArtifactRecord Record)? Read(SqliteConnection connection,
        SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT row_id,descriptor_json,byte_length,sha256 FROM artifacts WHERE artifact_id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var json = reader.GetString(1);
        return (reader.GetInt64(0), json, new ArtifactRecord(id, Decode(json), reader.GetInt64(2), reader.GetString(3)));
    }

    private static CaptureDescriptor Decode(string json) =>
        JsonSerializer.Deserialize<CaptureDescriptor>(json) ?? throw new InvalidDataException("Missing capture descriptor.");

    private static string Validate(CaptureDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        foreach (var field in new[] { descriptor.RunId, descriptor.AcquisitionId, descriptor.InstrumentId, descriptor.MediaType })
            if (string.IsNullOrWhiteSpace(field) || field.Length > 256) throw new InvalidDataException("Missing or excessive descriptor identifier/media type.");
        if (descriptor.CapturedUtc.Offset != TimeSpan.Zero || descriptor.Kind is not ("waveform" or "screenshot" or "attachment"))
            throw new InvalidDataException("Capture requires offset-zero UTC and a supported kind.");
        if ((descriptor.Kind == "waveform") != (descriptor.Waveform is not null))
            throw new InvalidDataException("Waveform metadata is required only for waveform captures.");
        if (descriptor.Identification?.Length > 4096) throw new InvalidDataException("Identification string is too long.");
        if (descriptor.Waveform is { } wave)
        {
            foreach (var field in new[] { wave.Channel, wave.Encoding, wave.Unit })
                if (string.IsNullOrWhiteSpace(field) || field.Length > 256) throw new InvalidDataException("Waveform channel, encoding and unit are required.");
            if (wave.SampleCount < 1 || wave.SampleIntervalSeconds is <= 0 ||
                new[] { wave.SampleIntervalSeconds, wave.TimeOriginSeconds, wave.VerticalScale, wave.VerticalOffset }.Any(x => x.HasValue && !double.IsFinite(x.Value)))
                throw new InvalidDataException("Invalid waveform sample count or numeric metadata.");
            if (wave.SettingsJson is { } settings)
            {
                if (settings.Length > 32768) throw new InvalidDataException("Settings manifest exceeds 32 KiB characters.");
                using var parsed = JsonDocument.Parse(settings, new JsonDocumentOptions { MaxDepth = 16 });
                if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Settings must be a JSON object.");
            }
        }
        return JsonSerializer.Serialize(descriptor);
    }

    private static async Task<string> Transfer(Stream source, Stream destination, long length, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long remaining = length;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct).ConfigureAwait(false);
            if (read == 0) throw new InvalidDataException("Capture is shorter than its declared length.");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            remaining -= read;
        }
        if (await source.ReadAsync(buffer.AsMemory(0, 1), ct).ConfigureAwait(false) != 0)
            throw new InvalidDataException("Capture exceeds its declared length.");
        ct.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
