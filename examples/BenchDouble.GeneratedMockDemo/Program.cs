using BenchDouble.Generated;
using UnitsNet;

// Both classes were generated from instrument-definitions/keysight/33522b.instrument.json.
// The driver uses the same strongly typed UnitsNet APIs for a real SCPI session
// or the in-memory mock. No duplicated SCPI command implementation is needed.
var mock = new Keysight33522BMock();
var instrument = mock.CreateDriver();

Console.WriteLine("Keysight 33522B — definition-driven simulated SCPI session");
Console.WriteLine($"Channel 1 initial frequency: " +
    $"{(await instrument.GetFrequencyAsync(1)).Hertz:F0} Hz");

mock.SetMockFrequency(1, Frequency.FromKilohertz(1.5));
Console.WriteLine($"Injected mock frequency: " +
    $"{(await instrument.GetFrequencyAsync(1)).Hertz:F0} Hz");

await instrument.SetFrequencyAsync(2, Frequency.FromKilohertz(2.5));
await instrument.SetAmplitudeAsync(2, ElectricPotential.FromVolts(2));
Console.WriteLine($"Channel 2 frequency: " +
    $"{(await instrument.GetFrequencyAsync(2)).Hertz:F0} Hz");
Console.WriteLine($"Channel 2 amplitude: " +
    $"{(await instrument.GetAmplitudeAsync(2)).Volts:F2} V");
Console.WriteLine("Accepted commands:");
foreach (var command in mock.Transcript)
    Console.WriteLine("  " + command);

Console.WriteLine("Mock only. No VISA/USB/GPIB access or hardware validation.");
