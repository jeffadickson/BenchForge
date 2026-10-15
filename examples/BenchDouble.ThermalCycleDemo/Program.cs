using BenchDouble.Plotting;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;

// Seven hours of virtual chamber cycling can be evaluated immediately.
// The console version advances a manual clock without sleeping.
var services = new ServiceCollection();
services.AddScoped<ManualSimulationClock>();
services.AddScoped<ISimulationClock>(sp => sp.GetRequiredService<ManualSimulationClock>());
services.AddScoped(_ => ThermalCycleSettings.Example());
services.AddScoped<ThermalChamberModel>();
services.AddScoped<ILoadBehavior>(sp =>
{
    var chamber = sp.GetRequiredService<ThermalChamberModel>();
    return new TemperatureDependentLoadBehavior(
        chamber, sp.GetRequiredService<ISimulationClock>(),
        ElectricCurrent.FromAmperes(0.5),
        Temperature.FromDegreesCelsius(30),
        Temperature.FromDegreesCelsius(70), 0.10);
});
services.AddScoped<MockPowerSupply>();

using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateScopes = true, ValidateOnBuild = true
});
using var run = provider.CreateScope();
var clock = run.ServiceProvider.GetRequiredService<ManualSimulationClock>();
var chamber = run.ServiceProvider.GetRequiredService<ThermalChamberModel>();
var supply = run.ServiceProvider.GetRequiredService<MockPowerSupply>();

await supply.ConfigureAsync(ElectricPotential.FromVolts(12), ElectricCurrent.FromAmperes(1));
await supply.SetOutputAsync(true);
var tempPoints = new List<(Duration X, Temperature Y)>();
var currentPoints = new List<(Duration X, ElectricCurrent Y)>();
Console.WriteLine("Virtual hour | Phase        | Chamber °C | Supply A");
try
{
    for (int minute = 0; minute <= 420; minute += 5)
    {
        TimeSpan t = clock.Elapsed;
        var sample = chamber.At(t);
        var measurement = await supply.MeasureAsync();
        tempPoints.Add((Duration.FromHours(t.TotalHours), sample.Temperature));
        currentPoints.Add((Duration.FromHours(t.TotalHours), measurement.Current));
        if (minute % 60 == 0 || minute == 420)
            Console.WriteLine($"{t.TotalHours,12:F2} | {sample.Phase,-12} | {sample.Temperature.DegreesCelsius,10:F1} | {measurement.Current.Amperes,8:F3}");
        if (minute < 420) clock.AdvanceBy(TimeSpan.FromMinutes(5));
    }
}
finally
{
    await supply.SetOutputAsync(false);
}

var temperaturePlot = MeasurementPlot.Create("Thermal chamber temperature",
    tempPoints, QuantityAxes.Hours("Simulation time"),
    QuantityAxes.Celsius());
var currentPlot = MeasurementPlot.Create("Supply current",
    currentPoints, QuantityAxes.Seconds("Simulation time"),
    QuantityAxes.Amperes());
Directory.CreateDirectory("thermal-demo-output");
await File.WriteAllTextAsync("thermal-demo-output/temperature.plotly.json", PlotlyFigure.ToJson(temperaturePlot));
await File.WriteAllTextAsync("thermal-demo-output/current.plotly.json", PlotlyFigure.ToJson(currentPlot));
Console.WriteLine("Wrote Plotly figure JSON to thermal-demo-output/. No physical equipment accessed.");
