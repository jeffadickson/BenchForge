namespace BenchDouble.TimeSeries;

/// <summary>
/// A canonical physical sample independent of UnitsNet's CLR serialization.
/// UnitCode and QuantityKind must be validated by a concrete backend against
/// a registered schema before accepting external network payloads.
/// </summary>
/// <param name="RunId">Experiment run identifier.</param>
/// <param name="InstrumentId">Instrument that produced the sample.</param>
/// <param name="SignalId">Named signal identifier.</param>
/// <param name="AcquiredUtc">UTC acquisition timestamp.</param>
/// <param name="CanonicalValue">Finite physical value in the canonical unit.</param>
/// <param name="QuantityKind">Registered physical quantity type.</param>
/// <param name="UnitCode">Registered canonical unit identifier.</param>
/// <param name="Sequence">Monotonic sample sequence within the signal.</param>
/// <param name="SimulationElapsed">Optional elapsed time in a virtual simulation.</param>
/// <param name="Quality">Quality status such as Good.</param>
/// <param name="MeasurementBasis">Optional RMS, peak, or DC measurement basis.</param>
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
/// <param name="RunId">Run identifier to query.</param>
/// <param name="SignalId">Signal identifier to query.</param>
/// <param name="StartUtc">Inclusive UTC interval start.</param>
/// <param name="EndUtc">Inclusive UTC interval end.</param>
/// <param name="MaximumPoints">Maximum points returned, from 1 through 1,000,000.</param>
public sealed record TimeSeriesQuery(
    string RunId,
    string SignalId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int MaximumPoints = 10000);

/// <summary>Append-only ingestion contract. Persistence and durability depend on the adapter.</summary>
public interface ITimeSeriesWriter
{
    /// <summary>Append a validated batch of canonical samples.</summary>
    /// <param name="samples">Batch of physical samples to append.</param>
    /// <param name="cancellationToken">Cancellation before or during ingestion.</param>
    /// <returns>Completion of the ingestion operation.</returns>
    ValueTask AppendAsync(
        IReadOnlyList<TimeSeriesSample> samples,
        CancellationToken cancellationToken = default);
}

/// <summary>Streaming, bounded retrieval for plotting, export and comparison.</summary>
public interface ITimeSeriesReader
{
    /// <summary>Stream a bounded UTC-time query of canonical samples.</summary>
    /// <param name="query">Run, signal, interval and point limit.</param>
    /// <param name="cancellationToken">Cancellation while enumerating results.</param>
    /// <returns>Asynchronous sequence of canonical samples.</returns>
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

    /// <summary>Atomically append a batch to the in-memory store without duplicate conflicts.</summary>
    /// <param name="samples">Samples validated before any state mutation.</param>
    /// <param name="cancellationToken">Cancellation checked before committing the batch.</param>
    /// <returns>Completion after the in-memory batch is stored.</returns>
    public ValueTask AppendAsync(
        IReadOnlyList<TimeSeriesSample> samples,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var sample in samples) QuantitySeries.Validate(sample);
        lock (_gate)
        {
            // Validate the entire batch before changing stored state.
            // Conflicting duplicates must never cause a partially committed batch.
            var incoming = new Dictionary<(string, string, string, long), TimeSeriesSample>();
            foreach (var sample in samples)
            {
                var key = (sample.RunId, sample.InstrumentId, sample.SignalId, sample.Sequence);
                if (incoming.TryGetValue(key, out var duplicate) && duplicate != sample)
                    throw new InvalidOperationException("Conflicting duplicate inside incoming batch.");
                incoming[key] = sample;
                if (_keys.Contains(key))
                {
                    var prior = _samples.First(s =>
                        s.RunId == sample.RunId && s.InstrumentId == sample.InstrumentId &&
                        s.SignalId == sample.SignalId && s.Sequence == sample.Sequence);
                    if (prior != sample)
                        throw new InvalidOperationException("Conflicting time-series sample with duplicate identity.");
                }
            }
            foreach (var pair in incoming)
            {
                if (_keys.Add(pair.Key)) _samples.Add(pair.Value);
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Return a bounded chronological snapshot of the in-memory samples.</summary>
    /// <param name="query">Validated UTC-time query and result limit.</param>
    /// <param name="cancellationToken">Cancellation checked while yielding each sample.</param>
    /// <returns>Chronologically ordered asynchronous sample stream.</returns>
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

}


/// <summary>Query logical elapsed time rather than UTC; for accelerated virtual runs.</summary>
/// <param name="RunId">Run identifier to query.</param>
/// <param name="SignalId">Signal identifier to query.</param>
/// <param name="Start">Inclusive elapsed-time interval start.</param>
/// <param name="End">Inclusive elapsed-time interval end.</param>
/// <param name="MaximumPoints">Maximum returned samples.</param>
public sealed record SimulationTimeSeriesQuery(
    string RunId,
    string SignalId,
    TimeSpan Start,
    TimeSpan End,
    int MaximumPoints = 10000);

/// <summary>Optional capability for querying deterministic virtual time directly.</summary>
public interface ISimulationTimeSeriesReader
{
    /// <summary>Stream samples selected by virtual elapsed time.</summary>
    /// <param name="query">Virtual-time interval and point limit.</param>
    /// <param name="cancellationToken">Cancellation while enumerating the sample stream.</param>
    /// <returns>Asynchronous sequence of canonical samples.</returns>
    IAsyncEnumerable<TimeSeriesSample> QuerySimulationAsync(
        SimulationTimeSeriesQuery query,
        CancellationToken cancellationToken = default);
}
