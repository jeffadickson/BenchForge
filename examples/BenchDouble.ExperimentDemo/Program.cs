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
// --automock adds a time-triggered fault without modifying SimulatedSupply
// or SensorDrivenLoad. Rules are immutable and scoped to this experiment.
bool useAutoMockJson = args.Contains("--automock-json", StringComparer.OrdinalIgnoreCase);
bool useAutoMock = useAutoMockJson || args.Contains("--automock", StringComparer.OrdinalIgnoreCase);
if (useAutoMockJson)
{
    // Run from repository root; configuration contains no executable code.
    var json = File.ReadAllText("examples/BenchDouble.ExperimentDemo/automock-overcurrent.json");
    builder.AutoMockLoadFromJson(
        sp => new SensorDrivenLoad(sp.GetRequiredService<ITemperatureSignal>()), json);
}
else if (useAutoMock)
{
    builder.AutoMockLoad(
        sp => new SensorDrivenLoad(sp.GetRequiredService<ITemperatureSignal>()),
        rules => rules.FixedCurrent("late-overcurrent",
            ElectricCurrent.FromAmperes(2.5),
            new AutoMockLoadCondition(From: TimeSpan.FromHours(2))));
}
else
{
    builder.Behavior<ILoadBehavior, SensorDrivenLoad>();
}
builder.Simulated<IThermalChamber, SimulatedChamber>("Chamber1");
builder.Simulated<IPowerSupply, SimulatedSupply>("Supply1");
// The DMM advertises two typed capabilities. Future instrument definitions
// will generate these registrations, not infer them from the caller's assignment.
builder.Model(_ => new TypedMockMeasurementsBuilder()
    .Constant(ElectricPotential.FromVolts(12))
    .Register<ElectricResistance>(t =>
        ElectricResistance.FromOhms(4700 + t.TotalMinutes))
    .Build());
builder.Simulated<ITypedConfigurableMeasurementSource>("Meter1", sp =>
    new TypedMockMeter(sp.GetRequiredService<TypedMockMeasurements>(),
        sp.GetRequiredService<ISimulationClock>()));

var definition = builder.Build("Thermal qualification");
Console.WriteLine($"Experiment: {definition.Name}");
if (definition.AutoMockRuleSha256 is { } digest)
    Console.WriteLine($"AutoMock rule SHA-256: {digest}");
foreach (var instrument in definition.Instruments)
    Console.WriteLine($"Registered: {instrument.Role} @ {instrument.HostId} [{instrument.Backend}]");

await using var run = definition.StartSimulated();
var chamber = run.Instrument<IThermalChamber>("Chamber1");
var supply = run.Instrument<IPowerSupply>("Supply1");
var meter = run.Instrument<ITypedConfigurableMeasurementSource>("Meter1");
await meter.ConfigureAsync<ElectricResistance>();
Console.WriteLine($"DMM resistance: " +
    $"{(await meter.MeasureAsync<ElectricResistance>()).Ohms:F1} ohms");
run.Clock.AdvanceBy(TimeSpan.FromHours(1));
Console.WriteLine($"At 1h: chamber {chamber.Setpoint.DegreesCelsius:F1} C, " +
    $"sensor-driven current {supply.Current.Amperes:F3} A");
run.Clock.AdvanceBy(TimeSpan.FromHours(1));
Console.WriteLine($"At 2h: current {supply.Current.Amperes:F3} A" +
    (useAutoMock ? " (AutoMock fault injected)" : " (normal load)"));
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
