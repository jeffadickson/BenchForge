using BenchDouble.TimeSeries;
using BenchDouble.TimeSeries.Sqlite;
using UnitsNet;
using Xunit;

namespace BenchDouble.TimeSeries.Sqlite.Tests;

public sealed class SqliteRecorderTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static TimeSeriesSample Sample(long seq, double amps) =>
        QuantitySeries.FromQuantity("RUN-1", "DMM-1", "current",
            Epoch.AddSeconds(seq), ElectricCurrent.FromAmperes(amps),
            seq, TimeSpan.FromSeconds(seq));

    private static string TempDatabase() => Path.Combine(
        Path.GetTempPath(), "benchdouble-" + Guid.NewGuid().ToString("N") + ".sqlite");

    [Fact]
    public async Task DataSurvivesCloseAndReopen()
    {
        var path = TempDatabase();
        try
        {
            await using (var store = await SqliteTimeSeriesStore.OpenAsync(path))
                await store.AppendAsync(new[] { Sample(0, 0.5), Sample(1, 0.55) });

            await using var reopened = await SqliteTimeSeriesStore.OpenAsync(path);
            var results = await ReadAllAsync(reopened);
            Assert.Equal(2, results.Count);
            Assert.Equal(0.5, results[0].CanonicalValue, 10);
            Assert.Equal(TimeSpan.FromSeconds(1), results[1].SimulationElapsed);
        }
        finally { DeleteFiles(path); }
    }

    [Fact]
    public async Task ReplayIsIdempotentAndConflictingBatchRollsBack()
    {
        var path = TempDatabase();
        try
        {
            await using var store = await SqliteTimeSeriesStore.OpenAsync(path);
            await store.AppendAsync(new[] { Sample(0, 0.5) });
            await store.AppendAsync(new[] { Sample(0, 0.5) });
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.AppendAsync(new[] { Sample(1, 0.6), Sample(0, 0.8) }));
            var results = await ReadAllAsync(store);
            Assert.Single(results);
            Assert.Equal(0.5, results[0].CanonicalValue, 10);
        }
        finally { DeleteFiles(path); }
    }

    [Fact]
    public async Task SameSignalCannotChangePhysicalDimension()
    {
        var path = TempDatabase();
        try
        {
            await using var store = await SqliteTimeSeriesStore.OpenAsync(path);
            await store.AppendAsync(new[] { Sample(0, 0.5) });
            var voltage = QuantitySeries.FromQuantity("RUN-1", "DMM-1", "current",
                Epoch.AddSeconds(1), ElectricPotential.FromVolts(12), 1);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.AppendAsync(new[] { voltage }));
            Assert.Single(await ReadAllAsync(store));
        }
        finally { DeleteFiles(path); }
    }

    [Fact]
    public async Task RejectsNonCanonicalUnits()
    {
        var path = TempDatabase();
        try
        {
            await using var store = await SqliteTimeSeriesStore.OpenAsync(path);
            var wrongUnit = Sample(0, 0.5) with { UnitCode = "V" };
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.AppendAsync(new[] { wrongUnit }));
        }
        finally { DeleteFiles(path); }
    }

    [Fact]
    public async Task SupportsBoundedHistoricalQuery()
    {
        var path = TempDatabase();
        try
        {
            await using var store = await SqliteTimeSeriesStore.OpenAsync(path);
            await store.AppendAsync(new[] { Sample(0, 0.5), Sample(1, 0.6), Sample(2, 0.7) });
            var query = new TimeSeriesQuery("RUN-1", "current", Epoch, Epoch.AddSeconds(2), 2);
            var count = 0;
            await foreach (var _ in store.QueryAsync(query)) count++;
            Assert.Equal(2, count);
        }
        finally { DeleteFiles(path); }
    }

    private static async Task<List<TimeSeriesSample>> ReadAllAsync(SqliteTimeSeriesStore store)
    {
        var result = new List<TimeSeriesSample>();
        await foreach (var sample in store.QueryAsync(
            new TimeSeriesQuery("RUN-1", "current", Epoch, Epoch.AddHours(1))))
            result.Add(sample);
        return result;
    }

    private static void DeleteFiles(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(path + suffix); }
            catch (IOException) { /* Test cleanup on Windows can be delayed. */ }
        }
    }
}
