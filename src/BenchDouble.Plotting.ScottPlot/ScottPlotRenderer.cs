using ScottPlot;

namespace BenchDouble.Plotting.ScottPlot;

/// <summary>Optional headless PNG renderer; instrument hosts and the runner core do not reference ScottPlot.</summary>
public static class ScottPlotRenderer
{
    public static void SavePng(MeasurementPlotSeries series, string path, int width = 900, int height = 500)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (series.X.Count != series.Y.Count) throw new ArgumentException("Plot X and Y lengths differ.");
        var plot = new Plot();
        plot.Add.Scatter(series.X.ToArray(), series.Y.ToArray());
        plot.Title(series.Name);
        plot.XLabel(series.XLabel);
        plot.YLabel(series.YLabel);
        plot.SavePng(path, width, height);
    }
}
