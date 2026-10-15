using BenchDouble.Experiments;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;

// The experiment owns instrument role registration and mock composition.
// The instrument classes receive normal C# constructor dependencies.
var builder = new ExperimentDefinitionBuilder();
builder.Model(_ => new ThermalChamberModel(ThermalCycleSettings.Example()));
builder.Model<ITemperatureProgram>(sp => sp.GetRequiredService<ThermalChamberModel>());
builder.Model(sp => new FirstOrderThermalSensor(
    sp.GetRequiredService<ITemperatureProgram>(),
    TimeSpan.FromMinutes(20), Temperature.FromDegreesCelsius(30)));
builder.Model<ITemperatureSignal>(sp => sp.GetRequiredService<FirstOrderThermalSensor>());
builder.Behavior<ILoadBehavior, SensorDrivenLoad>();
builder.Simulated<IThermalChamber, SimulatedChamber>("Chamber1");
builder.Simulated<IPowerSupply, SimulatedSupply>("Supply1");

var definition = builder.Build("Thermal qualification");
Console.WriteLine($"Experiment: {definition.Name}");
foreach (var instrument in definition.Instruments)
    Console.WriteLine($"Registered: {instrument.Role} @ {instrument.HostId} [{instrument.Backend}]");

await using var run = definition.StartSimulated();
var chamber = run.Instrument<IThermalChamber>("Chamber1");
var supply = run.Instrument<IPowerSupply>("Supply1");
run.Clock.AdvanceBy(TimeSpan.FromHours(1));
Console.WriteLine($"At 1h: chamber {chamber.Setpoint.DegreesCelsius:F1} C, " +
    $"sensor-driven current {supply.Current.Amperes:F3} A");
Console.WriteLine("No physical instrument discovery, reservation or network connection was performed.");

public interface IThermalChamber
{
    Temperature Setpoint { get; }
}
public interface IPowerSupply
{
    ElectricCurrent Current { get; }
}
public sealed class SimulatedChamber(ThermalChamberModel chamber, ISimulationClock clock)
    : IThermalChamber
{
    public Temperature Setpoint => chamber.ReadAt(clock.Elapsed);
}
public sealed class SimulatedSupply(ILoadBehavior load, ISimulationClock clock)
    : IPowerSupply
{
    public ElectricCurrent Current =>
        load.CurrentAt(ElectricPotential.FromVolts(12), clock.Elapsed);
}
public sealed class SensorDrivenLoad(ITemperatureSignal sensor) : ILoadBehavior
{
    public ElectricCurrent CurrentAt(ElectricPotential voltage, TimeSpan elapsed)
    {
        double c = sensor.ReadAt(elapsed).DegreesCelsius;
        double factor = 1 + 0.10 * Math.Clamp((c - 30) / 40, 0, 1);
        return ElectricCurrent.FromAmperes(voltage.Volts / 24 * factor);
    }
}
