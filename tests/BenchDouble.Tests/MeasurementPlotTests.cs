using BenchDouble.Plotting;
using System.Text.Json;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Verify unit-aware plotting, serialization, and invalid-value rejection.</summary>
public sealed class MeasurementPlotTests
{
    [Fact]
    public void UnitConversionAndLabelsAreConsistent()
    {
        var samples = new[]
        {
            (Frequency.FromHertz(1000), ElectricPotential.FromMillivolts(500)),
            (Frequency.FromHertz(2000), ElectricPotential.FromMillivolts(750))
        };
        var plot = MeasurementPlot.Create("Frequency response", samples,
            QuantityAxes.Kilohertz(), QuantityAxes.Volts(), "RMS");
        Assert.Equal(new double[] { 1, 2 }, plot.X);
        Assert.Equal(new double[] { 0.5, 0.75 }, plot.Y);
        Assert.Equal("Frequency (kHz)", plot.XLabel);
        Assert.Equal("Voltage (V)", plot.YLabel);
        Assert.Equal("ElectricPotential", plot.YDimension);
        Assert.Equal("RMS", plot.MeasurementBasis);
    }

    [Fact]
    public void PlotlyFigureContainsUnitsAndNumbers()
    {
        var plot = MeasurementPlot.Create("DMM", new[]
        {
            (Duration.FromSeconds(0), ElectricResistance.FromOhms(1000))
        }, QuantityAxes.Seconds(), QuantityAxes.Ohms());
        using var doc = JsonDocument.Parse(PlotlyFigure.ToJson(plot));
        Assert.Equal("scatter", doc.RootElement.GetProperty("data")[0].GetProperty("type").GetString());
        Assert.Equal(1000, doc.RootElement.GetProperty("data")[0].GetProperty("y")[0].GetDouble());
        Assert.Equal("Ω", doc.RootElement.GetProperty("meta").GetProperty("yUnit").GetString());
    }

    /// <summary>Reject nonfinite display values without relying on invalid UnitsNet construction.</summary>
    [Fact]
    public void NonFiniteMeasurementsAreRejected()
    {
        // Use a deliberately invalid presentation conversion: UnitsNet rejects NaN at construction.
        var invalidAxis = new QuantityAxis<ElectricPotential>(
            "Voltage", "V", "ElectricPotential", _ => double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MeasurementPlot.Create("Bad", new[] { (Frequency.FromHertz(1), ElectricPotential.FromVolts(1)) },
                QuantityAxes.Hertz(), invalidAxis));
    }
}
