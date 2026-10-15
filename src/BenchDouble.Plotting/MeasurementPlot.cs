using System.Text.Json;
using UnitsNet;

namespace BenchDouble.Plotting;

/// <summary>
/// A type-safe mapping from a UnitsNet quantity to the display unit of an axis.
/// The selector is defined once for the quantity and conversion (e.g., kHz).
/// </summary>
public sealed record QuantityAxis<TQuantity>(
    string Title, string Symbol, string Dimension, Func<TQuantity, double> Convert)
    where TQuantity : struct
{
    public double ToDisplayValue(TQuantity value)
    {
        double result = Convert(value);
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(nameof(value), "Plot values must be finite.");
        return result;
    }
}

/// <summary>Common physical quantity axes. Add others without changing plot backends.</summary>
public static class QuantityAxes
{
    public static QuantityAxis<Frequency> Hertz(string title = "Frequency") =>
        new(title, "Hz", "Frequency", q => q.Hertz);

    public static QuantityAxis<Frequency> Kilohertz(string title = "Frequency") =>
        new(title, "kHz", "Frequency", q => q.Kilohertz);

    public static QuantityAxis<ElectricPotential> Volts(string title = "Voltage") =>
        new(title, "V", "ElectricPotential", q => q.Volts);

    public static QuantityAxis<ElectricPotential> Millivolts(string title = "Voltage") =>
        new(title, "mV", "ElectricPotential", q => q.Millivolts);

    public static QuantityAxis<ElectricCurrent> Amperes(string title = "Current") =>
        new(title, "A", "ElectricCurrent", q => q.Amperes);

    public static QuantityAxis<ElectricResistance> Ohms(string title = "Resistance") =>
        new(title, "Ω", "ElectricResistance", q => q.Ohms);

    public static QuantityAxis<Temperature> Celsius(string title = "Temperature") =>
        new(title, "°C", "Temperature", q => q.DegreesCelsius);

    public static QuantityAxis<Duration> Seconds(string title = "Elapsed time") =>
        new(title, "s", "Duration", q => q.Seconds);

    public static QuantityAxis<Duration> Hours(string title = "Elapsed time") =>
        new(title, "h", "Duration", q => q.Hours);
}

/// <summary>
/// Immutable plot data independent of ScottPlot, Plotly and UI libraries.
/// Physical units, dimension, and basis are retained as axis metadata.
/// </summary>
public sealed record MeasurementPlotSeries(
    string Name,
    string XLabel,
    string YLabel,
    string XDimension,
    string YDimension,
    string XUnit,
    string YUnit,
    IReadOnlyList<double> X,
    IReadOnlyList<double> Y,
    string? MeasurementBasis = null);

/// <summary>Converts typed physical quantities into chart coordinates only at the presentation boundary.</summary>
public static class MeasurementPlot
{
    public static MeasurementPlotSeries Create<TX, TY>(
        string name,
        IEnumerable<(TX X, TY Y)> samples,
        QuantityAxis<TX> xAxis,
        QuantityAxis<TY> yAxis,
        string? measurementBasis = null)
        where TX : struct where TY : struct
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(xAxis);
        ArgumentNullException.ThrowIfNull(yAxis);
        var x = new List<double>();
        var y = new List<double>();
        foreach (var sample in samples)
        {
            x.Add(xAxis.ToDisplayValue(sample.X));
            y.Add(yAxis.ToDisplayValue(sample.Y));
        }
        return new MeasurementPlotSeries(name,
            $"{xAxis.Title} ({xAxis.Symbol})",
            $"{yAxis.Title} ({yAxis.Symbol})",
            xAxis.Dimension, yAxis.Dimension, xAxis.Symbol, yAxis.Symbol,
            x.ToArray(), y.ToArray(), measurementBasis);
    }
}

/// <summary>
/// Emits the standard Plotly.js figure JSON, without requiring Plotly.NET
/// in the runner or loading browser assets on instrument hosts.
/// </summary>
public static class PlotlyFigure
{
    public static string ToJson(MeasurementPlotSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.X.Count != series.Y.Count) throw new ArgumentException("Plot X and Y lengths differ.");
        return JsonSerializer.Serialize(new
        {
            data = new[] { new { type = "scatter", mode = "lines+markers", name = series.Name, x = series.X, y = series.Y } },
            layout = new { xaxis = new { title = series.XLabel }, yaxis = new { title = series.YLabel }, title = series.Name },
            meta = new { xDimension = series.XDimension, yDimension = series.YDimension,
                         xUnit = series.XUnit, yUnit = series.YUnit, measurementBasis = series.MeasurementBasis }
        });
    }
}
