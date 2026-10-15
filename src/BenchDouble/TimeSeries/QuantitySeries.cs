using UnitsNet;

namespace BenchDouble.TimeSeries;

/// <summary>
/// Explicit allow-list of canonical physical dimensions accepted by storage.
/// UnitsNet remains the source of conversions; the wire representation is stable.
/// </summary>
public static class QuantitySeries
{
    private static readonly IReadOnlyDictionary<string, string> CanonicalUnits =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Temperature"] = "K",
            ["ElectricPotential"] = "V",
            ["ElectricCurrent"] = "A",
            ["ElectricResistance"] = "Ohm",
            ["Frequency"] = "Hz",
            ["Duration"] = "s"
        };

    public static void Validate(TimeSeriesSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (string.IsNullOrWhiteSpace(sample.RunId) ||
            string.IsNullOrWhiteSpace(sample.InstrumentId) ||
            string.IsNullOrWhiteSpace(sample.SignalId) ||
            sample.AcquiredUtc.Offset != TimeSpan.Zero ||
            sample.Sequence < 0 ||
            sample.SimulationElapsed is { } elapsed && elapsed < TimeSpan.Zero ||
            !double.IsFinite(sample.CanonicalValue) ||
            !CanonicalUnits.TryGetValue(sample.QuantityKind, out string? unit) ||
            !string.Equals(unit, sample.UnitCode, StringComparison.Ordinal))
            throw new ArgumentException("Invalid identity, timestamp, quantity or canonical unit.", nameof(sample));
        if (sample.QuantityKind == "Temperature" && sample.CanonicalValue < 0)
            throw new ArgumentOutOfRangeException(nameof(sample), "Absolute temperature cannot be negative.");
    }

    public static TimeSeriesSample FromQuantity<T>(
        string runId, string instrumentId, string signalId,
        DateTimeOffset acquiredUtc, T quantity, long sequence,
        TimeSpan? simulationElapsed = null,
        string quality = "Good", string? measurementBasis = null)
        where T : struct, IQuantity
    {
        var (kind, unit, value) = quantity switch
        {
            Temperature q => ("Temperature", "K", q.Kelvins),
            ElectricPotential q => ("ElectricPotential", "V", q.Volts),
            ElectricCurrent q => ("ElectricCurrent", "A", q.Amperes),
            ElectricResistance q => ("ElectricResistance", "Ohm", q.Ohms),
            Frequency q => ("Frequency", "Hz", q.Hertz),
            Duration q => ("Duration", "s", q.Seconds),
            _ => throw new NotSupportedException(
                $"No canonical storage mapping for {typeof(T).Name}.")
        };
        var sample = new TimeSeriesSample(
            runId, instrumentId, signalId, acquiredUtc.ToUniversalTime(),
            value, kind, unit, sequence, simulationElapsed, quality, measurementBasis);
        Validate(sample);
        return sample;
    }
}
