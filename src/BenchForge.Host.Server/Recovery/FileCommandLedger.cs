using System.Text.Json;
using BenchForge.Contracts;

namespace BenchForge.Host.Server.Recovery;

/// <summary>
/// Single-writer durable file-backed <see cref="ICommandLedger"/> using atomic write-through replacement.
/// Survives host restarts and quarantines any resource that crashed with an in-flight <see cref="CommandExecutionState.Dispatched"/>
/// or <see cref="CommandExecutionState.Uncertain"/> command until explicit reconciliation.
/// </summary>
public sealed class FileCommandLedger : ICommandLedger, IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly FileStream _ownerLock;
    private readonly LedgerDocument _state;
    private bool _disposed;

    private sealed class LedgerDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<CommandReceipt> Receipts { get; set; } = new();
        public List<ReconciliationEntry> Reconciliations { get; set; } = new();
    }

    private sealed record ReconciliationEntry(string ResourceId, string Note, DateTimeOffset ReconciledUtc);

    /// <summary>Open or create a single-writer durable command ledger at <paramref name="path"/>.</summary>
    /// <param name="path">Durable file path on local storage; parent directories are created automatically.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    /// <exception cref="InvalidDataException">Existing ledger file is corrupt or has an unsupported schema version.</exception>
    public FileCommandLedger(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Command ledger path is required.", nameof(path));

        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _ownerLock = new FileStream(_path + ".owner.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(_path))
            {
                _state = JsonSerializer.Deserialize<LedgerDocument>(File.ReadAllText(_path))
                    ?? throw new InvalidDataException("Empty command ledger.");
                if (_state.SchemaVersion != 1 || _state.Receipts is null || _state.Reconciliations is null)
                    throw new InvalidDataException("Unsupported or corrupt command ledger.");
            }
            else
            {
                _state = new LedgerDocument();
                Persist();
            }
        }
        catch
        {
            _ownerLock.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public CommandReceipt? TryGet(string resourceId, Guid commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _state.Receipts.LastOrDefault(r =>
                string.Equals(r.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) &&
                r.CommandId == commandId);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<CommandReceipt> ListForResource(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _state.Receipts
                .Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.UpdatedUtc)
                .ToArray();
        }
    }

    /// <inheritdoc/>
    public void Record(CommandReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int existingIndex = _state.Receipts.FindIndex(r =>
                string.Equals(r.ResourceId, receipt.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                r.CommandId == receipt.CommandId);
            if (existingIndex >= 0)
                _state.Receipts[existingIndex] = receipt;
            else
                _state.Receipts.Add(receipt);
            Persist();
        }
    }

    /// <inheritdoc/>
    public bool RequiresRecovery(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var latestReconcile = _state.Reconciliations
                .Where(e => string.Equals(e.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .Select(e => (DateTimeOffset?)e.ReconciledUtc)
                .Max();

            return _state.Receipts.Any(r =>
                string.Equals(r.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) &&
                r.State is CommandExecutionState.Dispatched or CommandExecutionState.Uncertain or CommandExecutionState.RecoveryRequired &&
                (latestReconcile is null || r.UpdatedUtc > latestReconcile.Value));
        }
    }

    /// <inheritdoc/>
    public int ReconcileResource(string resourceId, string reconciliationNote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationNote);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = DateTimeOffset.UtcNow;
            var latestReconcile = _state.Reconciliations
                .Where(e => string.Equals(e.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .Select(e => (DateTimeOffset?)e.ReconciledUtc)
                .Max();

            int count = _state.Receipts.Count(r =>
                string.Equals(r.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) &&
                r.State is CommandExecutionState.Dispatched or CommandExecutionState.Uncertain or CommandExecutionState.RecoveryRequired &&
                (latestReconcile is null || r.UpdatedUtc > latestReconcile.Value));

            _state.Reconciliations.Add(new ReconciliationEntry(resourceId, reconciliationNote.Trim(), now));
            Persist();
            return count;
        }
    }

    /// <summary>Release the single-writer owner lock.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _ownerLock.Dispose();
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
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
