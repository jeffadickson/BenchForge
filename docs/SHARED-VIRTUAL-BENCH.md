# Shared virtual bench — stateful simulation

BenchForge now has a source-neutral, hardware-free signal model in `VirtualSignalBus` and a shared instrument view in `VirtualBench`. The model provides an explicitly configured sine source, resistive load, current compliance, output enable/disable, voltage and current at virtual time, and deterministic DC/RMS meter readings. `VirtualBench.Capture` converts the same source state to a signed 16-bit `WaveformCapture` with UnitsNet time/voltage metadata.

```csharp
using BenchDouble.Simulation;
using UnitsNet;

var signal = new VirtualSignalBus();
signal.Configure(Frequency.FromHertz(1_000), ElectricPotential.FromVolts(2),
    ElectricPotential.FromVolts(0), ElectricResistance.FromOhms(1_000),
    ElectricCurrent.FromMilliamperes(10));
signal.SetOutput(true);

var bench = new VirtualBench(signal);
var (dc, rms) = bench.MeasureVoltage();
var trace = bench.Capture(1, 1_000, Time.FromMicroseconds(1));
Console.WriteLine($"Meter: {rms.Volts:F4} Vrms; scope samples: {trace.SampleCount}");
```

Expected ideal result: approximately 1.4142 Vrms and 1,000 oscilloscope samples. Output-off produces zero measurements. Compliance clipping follows `abs(V) <= I_limit * R_load`. This is a simple resistive illustrative model; it does not represent calibrated equipment, real settling, slew rate, input impedance or a physical interlock.

Run `make test-virtual-bench` for cross-instrument consistency and state-transition tests. Source and tests have been committed; passing CI evidence has not yet been established.

The next generator step is to bind reviewed instrument definitions to shared `VirtualSignalBus` capabilities rather than giving each generated mock unrelated values. Unknown instrument behavior must remain explicit rather than silently modeled as a generic sine source.
