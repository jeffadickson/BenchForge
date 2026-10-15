using System.Runtime.CompilerServices;
using BenchDouble.TimeSeries;
using Microsoft.Data.Sqlite;

namespace BenchDouble.TimeSeries.Sqlite;

/// <summary>
/// Durable, append-only local recorder. SQLite WAL supports recovery and concurrent
/// readers; batches are atomic and conflicting replays fail rather than overwrite.
/// Dispose after the run to release this store's resources.
/// </summary>
public sealed class SqliteTimeSeriesStore : ITimeSeriesWriter, ITimeSeriesReader, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private SqliteTimeSeriesStore(string connectionString) =>
        _connectionString = connectionString;

    /// <summary>Create/open a durable database and initialize the v1 schema.</summary>
    public static async Task<SqliteTimeSeriesStore> OpenAsync(
        string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 30
        };
        var store = new SqliteTimeSeriesStore(builder.ToString());
        try
        {
            await using var connection = await store.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=FULL;
                CREATE TABLE IF NOT EXISTS signal_schema (
                    run_id TEXT NOT NULL,
                    instrument_id TEXT NOT NULL,
                    signal_id TEXT NOT NULL,
                    quantity_kind TEXT NOT NULL,
                    unit_code TEXT NOT NULL,
                    measurement_basis TEXT,
                    PRIMARY KEY (run_id, instrument_id, signal_id)
                );
                CREATE TABLE IF NOT EXISTS samples (
                    run_id TEXT NOT NULL,
                    instrument_id TEXT NOT NULL,
                    signal_id TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    acquired_utc_ticks INTEGER NOT NULL,
                    canonical_value REAL NOT NULL,
                    quantity_kind TEXT NOT NULL,
                    unit_code TEXT NOT NULL,
                    simulated_ticks INTEGER,
                    quality TEXT NOT NULL,
                    measurement_basis TEXT,
                    ingested_utc_ticks INTEGER NOT NULL,
                    PRIMARY KEY (run_id, instrument_id, signal_id, sequence)
                );
                CREATE INDEX IF NOT EXISTS ix_samples_time
                  ON samples(run_id, signal_id, acquired_utc_ticks, sequence);
                PRAGMA user_version=1;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return store;
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<SqliteConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=30000; PRAGMA synchronous=FULL;";
        await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return connection;
    }

    public async ValueTask AppendAsync(
        IReadOnlyList<TimeSeriesSample> samples,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        foreach (var sample in samples) QuantitySeries.Validate(sample);
        if (samples.Count == 0) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await using var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            foreach (var sample in samples)
            {
                // One physical dimension and measurement basis per signal.
                await using (var schema = connection.CreateCommand())
                {
                    schema.Transaction = transaction;
                    schema.CommandText = """
                        INSERT INTO signal_schema
                        (run_id, instrument_id, signal_id, quantity_kind, unit_code, measurement_basis)
                        VALUES ($run, $instrument, $signal, $kind, $unit, $basis)
                        ON CONFLICT DO NOTHING;
                        """;
                    AddIdentity(schema, sample);
                    schema.Parameters.AddWithValue("$kind", sample.QuantityKind);
                    schema.Parameters.AddWithValue("$unit", sample.UnitCode);
                    schema.Parameters.AddWithValue("$basis", (object?)sample.MeasurementBasis ?? DBNull.Value);
                    await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                await using (var check = connection.CreateCommand())
                {
                    check.Transaction = transaction;
                    check.CommandText = """
                        SELECT quantity_kind, unit_code, measurement_basis FROM signal_schema
                        WHERE run_id=$run AND instrument_id=$instrument AND signal_id=$signal;
                        """;
                    AddIdentity(check, sample);
                    await using var reader = await check.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ||
                        reader.GetString(0) != sample.QuantityKind ||
                        reader.GetString(1) != sample.UnitCode ||
                        (reader.IsDBNull(2) ? null : reader.GetString(2)) != sample.MeasurementBasis)
                        throw new InvalidOperationException("Signal dimension or measurement basis changed within a run.");
                }
                await using (var insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT INTO samples
                        (run_id, instrument_id, signal_id, sequence, acquired_utc_ticks,
                         canonical_value, quantity_kind, unit_code, simulated_ticks,
                         quality, measurement_basis, ingested_utc_ticks)
                        VALUES ($run, $instrument, $signal, $seq, $time,
                                $value, $kind, $unit, $sim, $quality, $basis, $ingested)
                        ON CONFLICT DO NOTHING;
                        """;
                    AddIdentity(insert, sample);
                    insert.Parameters.AddWithValue("$seq", sample.Sequence);
                    insert.Parameters.AddWithValue("$time", sample.AcquiredUtc.UtcDateTime.Ticks);
                    insert.Parameters.AddWithValue("$value", sample.CanonicalValue);
                    insert.Parameters.AddWithValue("$kind", sample.QuantityKind);
                    insert.Parameters.AddWithValue("$unit", sample.UnitCode);
                    insert.Parameters.AddWithValue("$sim", sample.SimulationElapsed is { } elapsed ? (object)elapsed.Ticks : DBNull.Value);
                    insert.Parameters.AddWithValue("$quality", sample.Quality);
                    insert.Parameters.AddWithValue("$basis", (object?)sample.MeasurementBasis ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$ingested", DateTime.UtcNow.Ticks);
                    await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                // Confirm identical replay rather than silently ignoring a conflicting sample.
                await using (var read = connection.CreateCommand())
                {
                    read.Transaction = transaction;
                    read.CommandText = """
                        SELECT acquired_utc_ticks, canonical_value, quantity_kind, unit_code,
                               simulated_ticks, quality, measurement_basis
                        FROM samples WHERE run_id=$run AND instrument_id=$instrument
                           AND signal_id=$signal AND sequence=$seq;
                        """;
                    AddIdentity(read, sample);
                    read.Parameters.AddWithValue("$seq", sample.Sequence);
                    await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ||
                        reader.GetInt64(0) != sample.AcquiredUtc.UtcDateTime.Ticks ||
                        reader.GetDouble(1) != sample.CanonicalValue ||
                        reader.GetString(2) != sample.QuantityKind ||
                        reader.GetString(3) != sample.UnitCode ||
                        (reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4)) != sample.SimulationElapsed?.Ticks ||
                        reader.GetString(5) != sample.Quality ||
                        (reader.IsDBNull(6) ? null : reader.GetString(6)) != sample.MeasurementBasis)
                        throw new InvalidOperationException("Conflicting sample identity; batch rolled back.");
                }
            }
            transaction.Commit();
        }
        finally { _gate.Release(); }
    }

    public async IAsyncEnumerable<TimeSeriesSample> QueryAsync(
        TimeSeriesQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.RunId) || string.IsNullOrWhiteSpace(query.SignalId) ||
            query.MaximumPoints is < 1 or > 1_000_000 || query.EndUtc < query.StartUtc)
            throw new ArgumentException("Invalid time-series query.", nameof(query));
        ThrowIfDisposed();
        await using var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_id, instrument_id, signal_id, acquired_utc_ticks, canonical_value,
                   quantity_kind, unit_code, sequence, simulated_ticks, quality, measurement_basis
            FROM samples WHERE run_id=$run AND signal_id=$signal
               AND acquired_utc_ticks >= $start AND acquired_utc_ticks <= $end
            ORDER BY acquired_utc_ticks, sequence LIMIT $max;
            """;
        command.Parameters.AddWithValue("$run", query.RunId);
        command.Parameters.AddWithValue("$signal", query.SignalId);
        command.Parameters.AddWithValue("$start", query.StartUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$end", query.EndUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$max", query.MaximumPoints);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new TimeSeriesSample(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero),
                reader.GetDouble(4), reader.GetString(5), reader.GetString(6),
                reader.GetInt64(7),
                reader.IsDBNull(8) ? null : TimeSpan.FromTicks(reader.GetInt64(8)),
                reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10));
        }
    }

    private static void AddIdentity(SqliteCommand command, TimeSeriesSample sample)
    {
        command.Parameters.AddWithValue("$run", sample.RunId);
        command.Parameters.AddWithValue("$instrument", sample.InstrumentId);
        command.Parameters.AddWithValue("$signal", sample.SignalId);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SqliteTimeSeriesStore));
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
