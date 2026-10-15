namespace BenchDouble.TimeSeries;

/// <summary>
/// A canonical physical sample independent of UnitsNet's CLR serialization.
/// UnitCode and QuantityKind must be validated by a concrete backend against
/// a registered schema before accepting external network payloads.
/// </summary>
public sealed record TimeSeriesSample(
    string RunId,
    string InstrumentId,
    string SignalId,
    DateTimeOffset AcquiredUtc,
    double CanonicalValue,
    string QuantityKind,
    string UnitCode,
    long Sequence,
    TimeSpan? SimulationElapsed = null,
    string Quality = "Good",
    string? MeasurementBasis = null);

/// <summary>Bounded query over one run/signal and an acquisition-time interval.</summary>
public sealed record TimeSeriesQuery(
    string RunId,
    string SignalId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int MaximumPoints = 10000);

/// <summary>Append-only ingestion contract. Persistence and durability depend on the adapter.</summary>
public interface ITimeSeriesWriter
{
    ValueTask AppendAsync(
        IReadOnlyList<TimeSeriesSample> samples,
        CancellationToken cancellationToken = default);
}

/// <summary>Streaming, bounded retrieval for plotting, export and comparison.</summary>
public interface ITimeSeriesReader
{
    IAsyncEnumerable<TimeSeriesSample> QueryAsync(
        TimeSeriesQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Test/demo adapter only: in-memory data is lost on process exit.
/// Production should use a durable local spool and optional remote sink.
/// </summary>
public sealed class InMemoryTimeSeriesStore : ITimeSeriesWriter, ITimeSeriesReader
{
    private readonly object _gate = new();
    private readonly List<TimeSeriesSample> _samples = new();
    private readonly HashSet<(string RunId, string InstrumentId, string SignalId, long Sequence)> _keys = new();

    public ValueTask AppendAsync(
        IReadOnlyList<TimeSeriesSample> samples,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var sample in samples) Validate(sample);
        lock (_gate)
        {
            foreach (var sample in samples)
            {
                var key = (sample.RunId, sample.InstrumentId, sample.SignalId, sample.Sequence);
                if (_keys.Add(key)) _samples.Add(sample);
                else
                {
                    var prior = _samples.First(s =>
                        s.RunId == sample.RunId && s.InstrumentId == sample.InstrumentId &&
                        s.SignalId == sample.SignalId && s.Sequence == sample.Sequence);
                    if (prior != sample)
                        throw new InvalidOperationException("Conflicting time-series sample with duplicate identity.");
                }
            }
        }
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<TimeSeriesSample> QueryAsync(
        TimeSeriesQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaximumPoints < 1 || query.MaximumPoints > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(query.MaximumPoints));
        if (query.EndUtc < query.StartUtc)
            throw new ArgumentException("End time must not precede start time.");
        TimeSeriesSample[] snapshot;
        lock (_gate)
        {
            snapshot = _samples
                .Where(s => s.RunId == query.RunId && s.SignalId == query.SignalId &&
                    s.AcquiredUtc >= query.StartUtc && s.AcquiredUtc <= query.EndUtc)
                .OrderBy(s => s.AcquiredUtc)
                .ThenBy(s => s.Sequence)
                .Take(query.MaximumPoints)
                .ToArray();
        }
        foreach (var sample in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return sample;
            await Task.Yield();
        }
    }

    private static void Validate(TimeSeriesSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (string.IsNullOrWhiteSpace(sample.RunId) ||
            string.IsNullOrWhiteSpace(sample.InstrumentId) ||
            string.IsNullOrWhiteSpace(sample.SignalId) ||
            string.IsNullOrWhiteSpace(sample.QuantityKind) ||
            string.IsNullOrWhiteSpace(sample.UnitCode) ||
            !double.IsFinite(sample.CanonicalValue) ||
            sample.AcquiredUtc.Offset != TimeSpan.Zero ||
            sample.Sequence < 0 ||
            sample.SimulationElapsed < TimeSpan.Zero)
            throw new ArgumentException("Invalid time-series sample.");
    }
}
