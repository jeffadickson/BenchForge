# Async oscilloscope demonstration

This is a simulation-only example for BenchForge's waveform infrastructure.

```csharp
using BenchDouble.Oscilloscopes;
using UnitsNet;

var mock = new SimulatedOscilloscopeSession(channels: 2, samples: 128);
var scope = new AsyncOscilloscope(mock);
await scope.ConfigureTimebaseAsync(Duration.FromMicroseconds(2));
await scope.SingleAsync();
var trace = await scope.ReadWaveformAsync(1, Guid.NewGuid());
Console.WriteLine($"Samples: {trace.SampleCount}");
Console.WriteLine($"Sample 16: {trace.TimeAt(16).Microseconds} us, {trace.VoltageAt(16).Volts} V");
```

Expected simulated output: 128 samples; sample 16 at 32 microseconds and 1 V. The channel-2 waveform has a different phase. The mock rejects reading before `SingleAsync` and unsupported channels.

Run the source tests with `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj` after installing the .NET 10 SDK. These tests have been committed but passing CI has not been verified.

## Limitations

This is not a hardware-validated Keysight driver. Its six-field preamble is a simulation-specific representation, not a manufacturer's `WAV:PRE?` format. The streaming reader reads one bounded block but leaves terminator handling to the transport. Command/query serialization across clients, cancellation recovery, physical triggering, model-specific SCPI dialects, binary artifact persistence and schema-v2 generation remain outstanding. See issues #63 and #66.
