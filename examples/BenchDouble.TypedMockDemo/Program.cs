using BenchDouble.Simulation;
using UnitsNet;

// A DMM advertises the quantity types it actually supports.
// Eventually these registrations will be generated from instrument definitions.
var clock = new ManualSimulationClock();
var measurements = new TypedMockMeasurementsBuilder()
    .Constant(ElectricPotential.FromMillivolts(250))
    .Register<ElectricResistance>(elapsed =>
        ElectricResistance.FromOhms(4700 + elapsed.TotalSeconds * 2))
    .Build();
var meter = new TypedMockMeter(measurements, clock);

await meter.ConfigureAsync<ElectricPotential>();
ElectricPotential volts = await meter.MeasureAsync<ElectricPotential>();
Console.WriteLine($"Voltage mode: {volts.Volts:F3} V");

await meter.ConfigureAsync<ElectricResistance>();
ElectricResistance ohms = await meter.MeasureAsync<ElectricResistance>();
Console.WriteLine($"Resistance mode: {ohms.Ohms:F1} ohms");

clock.AdvanceBy(TimeSpan.FromSeconds(5));
Console.WriteLine($"Resistance after 5 simulated seconds: " +
    $"{(await meter.MeasureAsync<ElectricResistance>()).Ohms:F1} ohms");

try
{
    // Generic return type does not silently switch a meter to voltage mode.
    await meter.MeasureAsync<ElectricPotential>();
}
catch (InvalidOperationException error)
{
    Console.WriteLine($"Expected mode protection: {error.Message}");
}

Console.WriteLine("No hardware or VISA connection was used.");
