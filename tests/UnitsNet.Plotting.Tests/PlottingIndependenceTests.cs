using BenchDouble.Plotting;
using UnitsNet;
using System.Text.Json;
using Xunit;

namespace UnitsNet.Plotting.Tests;

/// <summary>Tests the plotting core without a BenchDouble instrument library reference.</summary>
public sealed class PlottingIndependenceTests
{
    [Fact]
    public void ConvertsUnitsWithoutInstrumentDependencies()
    {
        var data = new[]
        {
            (Frequency.FromHertz(1000), ElectricPotential.FromMillivolts(250)),
            (Frequency.FromHertz(2000), ElectricPotential.FromMillivolts(500))
        };
        var series = MeasurementPlot.Create("Response", data,
            QuantityAxes.Kilohertz(), QuantityAxes.Volts());
        Assert.Equal(new[] { 1d, 2d }, series.X);
        Assert.Equal(new[] { .25d, .5d }, series.Y);
        Assert.Equal("kHz", series.XUnit);
        Assert.Equal("V", series.YUnit);
    }

    [Fact]
    public void PlotlyJsonCarriesDimensions()
    {
        var series = MeasurementPlot.Create("R", new[]
        {
            (Duration.FromSeconds(1), ElectricResistance.FromOhms(1000))
        }, QuantityAxes.Seconds(), QuantityAxes.Ohms());
        var json = PlotlyFigure.ToJson(series);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("meta");
        Assert.Equal("ElectricResistance", meta.GetProperty("yDimension").GetString());
        Assert.Equal("Ω", meta.GetProperty("yUnit").GetString());
    }
}
