using System.Security.Cryptography;
using System.Text.Json;

namespace BenchDouble;

/// <summary>
/// Durable host epoch and per-resource lease generations for local host fencing.
/// The state file must be on durable storage exclusively owned by one host process.
/// This is not a distributed consensus service or a hardware safety interlock.
/// </summary>
public sealed class HostFencingLedger
{
    private readonly object _gate = new();
    private readonly string _path;
    private LedgerState _state;

    private sealed class LedgerState
    {
        public long Epoch { get; set; }
        public Dictionary<string, long> Generations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Open the persisted ledger and advance its epoch before accepting any leases.
    /// A corrupt ledger fails closed instead of silently resetting ownership.
    /// </summary>
    public HostFencingLedger(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ledger path required.", nameof(path));
        _path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        if (File.Exists(_path))
        {
            _state = JsonSerializer.Deserialize<LedgerState>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("Empty host fencing ledger.");
            if (_state.Epoch < 0 || _state.Generations is null || _state.Generations.Values.Any(v => v < 0))
                throw new InvalidDataException("Invalid host fencing ledger.");
        }
        else _state = new LedgerState();
        _state.Epoch = checked(_state.Epoch + 1);
        Persist();
    }

    /// <summary>Durable epoch for this host process start.</summary>
    public long Epoch { get { lock (_gate) return _state.Epoch; } }

    /// <summary>Allocate and durably persist the next generation for a resource.</summary>
    public long NextGeneration(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) throw new ArgumentException("Resource required.", nameof(resourceId));
        lock (_gate)
        {
            var previous = _state.Generations.GetValueOrDefault(resourceId);
            _state.Generations[resourceId] = checked(previous + 1);
            Persist();
            return _state.Generations[resourceId];
        }
    }

    private void Persist()
    {
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, _state);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
