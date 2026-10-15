using BenchDouble.Plotting;
using BenchDouble.Simulation;
using BenchDouble.TimeSeries;
using BenchDouble.TimeSeries.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;

// Seven hours of virtual chamber cycling can be evaluated immediately.
// The console version advances a manual clock without sleeping.
var services = new ServiceCollection();
services.AddScoped<ManualSimulationClock>();
services.AddScoped<ISimulationClock>(sp => sp.GetRequiredService<ManualSimulationClock>());
services.AddScoped(_ => ThermalCycleSettings.Example());
services.AddScoped<ThermalChamberModel>();
services.AddScoped<ITemperatureProgram>(sp => sp.GetRequiredService<ThermalChamberModel>());
services.AddScoped<FirstOrderThermalSensor>(sp =>
    new FirstOrderThermalSensor(sp.GetRequiredService<ITemperatureProgram>(),
        TimeSpan.FromMinutes(20), Temperature.FromDegreesCelsius(30)));
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
var thermalSensor = run.ServiceProvider.GetRequiredService<FirstOrderThermalSensor>();


// A virtual wiring graph connects chamber -> sensor -> temperature-dependent
// DUT current, with the 12 V supply as a second typed input.
// This graph is built once for the experiment run and evaluated at each clock tick.
var wiring = new VirtualBenchGraphBuilder();
var chamberPort = wiring.Port<Temperature>("chamber.setpoint");
var sensorPort = wiring.Port<Temperature>("dut.sensor");
var voltagePort = wiring.Port<ElectricPotential>("supply.voltage");
var loadCurrentPort = wiring.Port<ElectricCurrent>("dut.current");
wiring.Source(chamberPort, t => chamber.ReadAt(t));
wiring.Transform(chamberPort, sensorPort, (_, t) => thermalSensor.ReadAt(t));
wiring.Source(voltagePort, _ => ElectricPotential.FromVolts(12));
wiring.Transform(sensorPort, voltagePort, loadCurrentPort, (temp, volts, _) =>
{
    double hotFraction = Math.Clamp((temp.DegreesCelsius - 30) / 40, 0, 1);
    return ElectricCurrent.FromAmperes(volts.Volts / 24 * (1 + 0.10 * hotFraction));
});
var signalGraph = wiring.Build();
var dutCurrentPoints = new List<(Duration X, ElectricCurrent Y)>();

await supply.ConfigureAsync(ElectricPotential.FromVolts(12), ElectricCurrent.FromAmperes(1));
await supply.SetOutputAsync(true);
var tempPoints = new List<(Duration X, Temperature Y)>();
var currentPoints = new List<(Duration X, ElectricCurrent Y)>();
var sensorPoints = new List<(Duration X, Temperature Y)>();
// Local-first durable measurement recorder: no server or network needed.
Directory.CreateDirectory("thermal-demo-output");
string databasePath = Path.Combine("thermal-demo-output", "measurements.sqlite");
await using var recorder = await SqliteTimeSeriesStore.OpenAsync(databasePath);
const string runId = "THERMAL-DEMO-001";
var acquiredEpoch = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
Console.WriteLine("Virtual hour | Phase        | Setpoint °C | Sensor °C | Supply A | DUT A");
try
{
    for (int minute = 0; minute <= 420; minute += 5)
    {
        TimeSpan t = clock.Elapsed;
        var sample = chamber.At(t);
        var measurement = await supply.MeasureAsync();
        var snapshot = signalGraph.SampleAt(t);
        var sensed = snapshot.Read(sensorPort);
        var dutCurrent = snapshot.Read(loadCurrentPort);
        tempPoints.Add((Duration.FromHours(t.TotalHours), sample.Temperature));
        sensorPoints.Add((Duration.FromHours(t.TotalHours), sensed));
        dutCurrentPoints.Add((Duration.FromHours(t.TotalHours), dutCurrent));
        currentPoints.Add((Duration.FromHours(t.TotalHours), measurement.Current));
        long sequence = minute / 5;
        // Preserve both real UTC acquisition field and simulated elapsed time.
        // Fixed UTC epoch makes this offline demo deterministic across reruns.
        var acquired = acquiredEpoch.AddMinutes(minute);
        await recorder.AppendAsync(new[]
        {
            QuantitySeries.FromQuantity(runId, "CHAMBER-1", "chamber.setpoint",
                acquired, sample.Temperature, sequence, t),
            QuantitySeries.FromQuantity(runId, "THERMAL-SENSOR-1", "dut.sensor",
                acquired, sensed, sequence, t),
            QuantitySeries.FromQuantity(runId, "SUPPLY-1", "supply.current",
                acquired, measurement.Current, sequence, t),
            QuantitySeries.FromQuantity(runId, "DUT-1", "dut.current",
                acquired, dutCurrent, sequence, t)
        });
        if (minute % 60 == 0 || minute == 420)
            Console.WriteLine($"{t.TotalHours,12:F2} | {sample.Phase,-12} | {sample.Temperature.DegreesCelsius,11:F1} | {sensed.DegreesCelsius,9:F1} | {measurement.Current.Amperes,8:F3} | {dutCurrent.Amperes,5:F3}");
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
var sensorPlot = MeasurementPlot.Create("Filtered thermal sensor",
    sensorPoints, QuantityAxes.Hours("Simulation time"),
    QuantityAxes.Celsius());
var dutCurrentPlot = MeasurementPlot.Create("Sensor-driven DUT current",
    dutCurrentPoints, QuantityAxes.Hours("Simulation time"),
    QuantityAxes.Amperes());
var currentPlot = MeasurementPlot.Create("Supply current",
    currentPoints, QuantityAxes.Hours("Simulation time"),
    QuantityAxes.Amperes());
Directory.CreateDirectory("thermal-demo-output");
await File.WriteAllTextAsync("thermal-demo-output/temperature.plotly.json", PlotlyFigure.ToJson(temperaturePlot));
await File.WriteAllTextAsync("thermal-demo-output/current.plotly.json", PlotlyFigure.ToJson(currentPlot));
await File.WriteAllTextAsync("thermal-demo-output/sensor.plotly.json", PlotlyFigure.ToJson(sensorPlot));
await File.WriteAllTextAsync("thermal-demo-output/dut-current.plotly.json", PlotlyFigure.ToJson(dutCurrentPlot));
int stored = 0;
await foreach (var point in recorder.QueryAsync(new TimeSeriesQuery(
    runId, "dut.sensor", acquiredEpoch, acquiredEpoch.AddHours(7), 1000)))
    stored++;
Console.WriteLine($"Persisted {stored} thermal-sensor readings to {databasePath}.");
Console.WriteLine("Wrote Plotly figure JSON to thermal-demo-output/. No physical equipment accessed.");
