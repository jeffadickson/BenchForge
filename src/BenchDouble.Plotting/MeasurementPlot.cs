using System.Text.Json;
using UnitsNet;

namespace BenchDouble.Plotting;

/// <summary>
/// A type-safe mapping from a UnitsNet quantity to the display unit of an axis.
/// The selector is defined once for the quantity and conversion (e.g., kHz).
/// </summary>
/// <param name="Title">Human-readable axis title.</param>
/// <param name="Symbol">Display unit symbol, such as Hz or V.</param>
/// <param name="Dimension">Physical dimension used for metadata and validation.</param>
/// <param name="Convert">Quantity-to-display-unit conversion function.</param>
public sealed record QuantityAxis<TQuantity>(
    string Title, string Symbol, string Dimension, Func<TQuantity, double> Convert)
    where TQuantity : struct
{
    /// <summary>Convert a typed physical quantity into a finite axis value.</summary>
    /// <param name="value">Typed quantity to convert.</param>
    /// <returns>Finite value in the axis's display unit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Conversion produced a nonfinite value.</exception>
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
    /// <summary>Create a frequency axis displaying values in hertz.</summary>
    /// <param name="title">Human-readable axis title.</param>
    /// <returns>A typed axis that converts measurements to hertz.</returns>
    public static QuantityAxis<Frequency> Hertz(string title = "Frequency") =>
        new(title, "Hz", "Frequency", q => q.Hertz);

    /// <summary>Create a frequency axis displaying values in kilohertz.</summary>
    /// <param name="title">Human-readable axis title.</param>
    /// <returns>A typed axis that converts measurements to kilohertz.</returns>
    public static QuantityAxis<Frequency> Kilohertz(string title = "Frequency") =>
        new(title, "kHz", "Frequency", q => q.Kilohertz);

    /// <summary>Create a voltage axis displaying values in volts.</summary>
    /// <param name="title">Human-readable axis title.</param>
    /// <returns>A typed axis that converts measurements to volts.</returns>
    public static QuantityAxis<ElectricPotential> Volts(string title = "Voltage") =>
        new(title, "V", "ElectricPotential", q => q.Volts);

    /// <summary>Create a voltage axis displaying values in millivolts.</summary>
    /// <param name="title">Human-readable axis title.</param>
    /// <returns>A typed axis that converts measurements to millivolts.</returns>
    public static QuantityAxis<ElectricPotential> Millivolts(string title = "Voltage") =>
        new(title, "mV", "ElectricPotential", q => q.Millivolts);

    /// <summary>Create a current axis displaying values in amperes.</summary>
    /// <param name="title">Human-readable axis title.</param>
    /// <returns>A typed axis that converts measurements to amperes.</returns>
    public static QuantityAxis<ElectricCurrent> Amperes(string title = "Current") =>
        new(title, "A", "ElectricCurrent", q => q.Amperes);

    /// <summary>Create a resistance axis displaying values in ohms.</summary>
    /// <param name="title">Human-readable axis title.</param>
    /// <returns>A typed axis that converts measurements to ohms.</returns>
    public static QuantityAxis<ElectricResistance> Ohms(string title = "Resistance") =>
        new(title, "Ω", "ElectricResistance", q => q.Ohms);

    /// <summary>Create a temperature axis displaying values in degrees Celsius.</summary>
    /// <param name="title">Human-readable axis title.</param>
    /// <returns>A typed axis that converts measurements to degrees Celsius.</returns>
    public static QuantityAxis<Temperature> Celsius(string title = "Temperature") =>
        new(title, "°C", "Temperature", q => q.DegreesCelsius);

    /// <summary>Create a elapsed time axis displaying values in seconds.</summary>
    /// <param name="title">Human-readable axis title.</param>
    /// <returns>A typed axis that converts measurements to seconds.</returns>
    public static QuantityAxis<Duration> Seconds(string title = "Elapsed time") =>
        new(title, "s", "Duration", q => q.Seconds);

    /// <summary>Create a elapsed time axis displaying values in hours.</summary>
    /// <param name="title">Human-readable axis title.</param>
    /// <returns>A typed axis that converts measurements to hours.</returns>
    public static QuantityAxis<Duration> Hours(string title = "Elapsed time") =>
        new(title, "h", "Duration", q => q.Hours);
}

/// <summary>
/// Immutable plot data independent of ScottPlot, Plotly and UI libraries.
/// Physical units, dimension, and basis are retained as axis metadata.
/// </summary>
/// <param name="Name">Plot series name.</param>
/// <param name="XLabel">Horizontal axis label including unit.</param>
/// <param name="YLabel">Vertical axis label including unit.</param>
/// <param name="XDimension">Horizontal physical dimension.</param>
/// <param name="YDimension">Vertical physical dimension.</param>
/// <param name="XUnit">Horizontal display unit symbol.</param>
/// <param name="YUnit">Vertical display unit symbol.</param>
/// <param name="X">Horizontal coordinates in the declared display unit.</param>
/// <param name="Y">Vertical coordinates in the declared display unit.</param>
/// <param name="MeasurementBasis">Optional measurement basis, such as RMS or peak.</param>
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
    /// <summary>Convert typed measurement pairs into immutable, unit-labeled plot coordinates.</summary>
    /// <typeparam name="TX">Horizontal UnitsNet quantity type.</typeparam>
    /// <typeparam name="TY">Vertical UnitsNet quantity type.</typeparam>
    /// <param name="name">Nonempty plot series name.</param>
    /// <param name="samples">Typed measurement pairs in acquisition order.</param>
    /// <param name="xAxis">Horizontal axis conversion and display metadata.</param>
    /// <param name="yAxis">Vertical axis conversion and display metadata.</param>
    /// <param name="measurementBasis">Optional basis such as RMS, peak, or DC.</param>
    /// <returns>Immutable plot coordinates and their physical-unit metadata.</returns>
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
    /// <summary>Serialize a validated measurement plot as Plotly-compatible JSON.</summary>
    /// <param name="series">Series containing matching horizontal and vertical coordinates.</param>
    /// <returns>JSON containing plot data, axis labels, and physical-unit metadata.</returns>
    /// <exception cref="ArgumentException">Horizontal and vertical sample counts differ.</exception>
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
