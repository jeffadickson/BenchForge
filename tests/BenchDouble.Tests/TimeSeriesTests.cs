using BenchDouble.TimeSeries;
using Xunit;

namespace BenchDouble.Tests;

public sealed class TimeSeriesTests
{
    private static TimeSeriesSample Sample(long sequence, double value) =>
        new("RUN-1", "DMM-1", "supply.current",
            new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)
                .AddSeconds(sequence),
            value, "ElectricCurrent", "A", sequence,
            TimeSpan.FromSeconds(sequence));

    [Fact]
    public async Task AppendQueryAndIdempotentReplay()
    {
        var store = new InMemoryTimeSeriesStore();
        await store.AppendAsync(new[] { Sample(0, 0.5), Sample(1, 0.55) });
        await store.AppendAsync(new[] { Sample(1, 0.55) });

        var query = new TimeSeriesQuery("RUN-1", "supply.current",
            Sample(0, 0).AcquiredUtc, Sample(2, 0).AcquiredUtc);
        var values = new List<double>();
        await foreach (var sample in store.QueryAsync(query))
            values.Add(sample.CanonicalValue);
        Assert.Equal(new[] { 0.5, 0.55 }, values);
    }

    [Fact]
    public async Task ConflictingDuplicateIsRejected()
    {
        var store = new InMemoryTimeSeriesStore();
        await store.AppendAsync(new[] { Sample(1, 0.5) });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.AppendAsync(new[] { Sample(1, 0.8) }));
    }

    [Fact]
    public async Task InvalidSampleIsRejected()
    {
        var store = new InMemoryTimeSeriesStore();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.AppendAsync(new[] { Sample(0, double.NaN) }));
    }

    [Fact]
    public async Task QueryRespectsBoundedPointCount()
    {
        var store = new InMemoryTimeSeriesStore();
        await store.AppendAsync(new[] { Sample(0, 1), Sample(1, 2), Sample(2, 3) });
        var results = new List<TimeSeriesSample>();
        await foreach (var point in store.QueryAsync(new TimeSeriesQuery(
            "RUN-1", "supply.current",
            Sample(0, 0).AcquiredUtc, Sample(2, 0).AcquiredUtc, 2)))
            results.Add(point);
        Assert.Equal(2, results.Count);
    }
}
