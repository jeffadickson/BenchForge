using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenchDouble.Host.Server.Recovery;

/// <summary>Atomic local snapshot of unresolved ownership. Flushes data before replacement; not a power-loss guarantee.</summary>
public sealed class FileHostOwnershipJournal : IHostOwnershipJournal, IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly FileStream? _writer;
    private Dictionary<string, OwnershipRecord> _records;
    private bool _disposed;
    private bool _faulted;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
    private sealed record Document(int Version, OwnershipRecord[] Records);

    /// <summary>Load unresolved records from a host-owned directory; malformed state prevents startup.</summary>
    /// <param name="directory">Local journal directory, never a browser-supplied path.</param>
    public FileHostOwnershipJournal(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var existing = Directory.EnumerateFileSystemEntries(directory).Any();
            _path = Path.Combine(Path.GetFullPath(directory), "ownership.json");
            _writer = new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _records = new(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Ownership journal exceeds its size bound.");
                var text = File.ReadAllText(_path);
                using var parsed = JsonDocument.Parse(text);
                RejectDuplicates(parsed.RootElement);
                var document = JsonSerializer.Deserialize<Document>(text, Json)
                    ?? throw new InvalidDataException("Missing ownership journal.");
                if (document.Version != 1 || document.Records is null || document.Records.Length > 4096 || document.Records.Any(r => r is null)) throw new InvalidDataException("Unsupported ownership journal.");
                foreach (var record in document.Records) { record.Validate(); _records.Add(record.ResourceId, record); }
            }
            else
            {
                if (existing) throw new InvalidDataException("Ownership journal is missing from an existing directory.");
                Write(_records);
            }
        }
        catch
        {
            _writer?.Dispose();
            throw new InvalidOperationException("Ownership journal unavailable or invalid; inspect host-owned state before restarting.");
        }
    }

    /// <inheritdoc />
    public OwnershipRecord[] Read() { lock (_gate) { EnsureActive(); return _records.Values.ToArray(); } }

    /// <inheritdoc />
    public void Add(OwnershipRecord record)
    {
        record.Validate();
        lock (_gate)
        {
            EnsureActive();
            if (_records.Count >= 4096) throw new InvalidOperationException("Ownership record limit reached.");
            var next = new Dictionary<string, OwnershipRecord>(_records, StringComparer.OrdinalIgnoreCase);
            if (!next.TryAdd(record.ResourceId, record)) throw new InvalidOperationException("Unresolved ownership already exists.");
            Write(next); _records = next;
        }
    }

    /// <inheritdoc />
    public void Remove(string resourceId, Guid recordId)
    {
        lock (_gate)
        {
            EnsureActive();
            if (!_records.TryGetValue(resourceId, out var record) || record.RecordId != recordId)
                throw new InvalidOperationException("Ownership review is stale.");
            var next = new Dictionary<string, OwnershipRecord>(_records, StringComparer.OrdinalIgnoreCase);
            next.Remove(resourceId); Write(next); _records = next;
        }
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate ownership field.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }

    private void Write(Dictionary<string, OwnershipRecord> records)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Document(1, records.Values.OrderBy(r => r.ResourceId, StringComparer.Ordinal).ToArray()), Json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        catch
        {
            _faulted = true;
            throw new InvalidOperationException("Ownership journal write failed; restart and reconcile before accepting new ownership.");
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { /* Do not mask the durable write outcome. */ } }
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted) throw new InvalidOperationException("Ownership journal is faulted; restart and reconcile before reuse.");
    }

    /// <summary>Close this journal; does not clear unresolved ownership evidence.</summary>
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _writer?.Dispose(); } }
}
