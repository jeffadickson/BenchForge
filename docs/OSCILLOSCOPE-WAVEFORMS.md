# Oscilloscope waveform infrastructure

Status: source implementation and xUnit tests committed; CI and physical hardware validation pending.

## Scope

`ScpiBinaryBlock.Decode` decodes bounded IEEE 488.2 definite-length blocks in memory. It rejects indefinite blocks, malformed decimal lengths, truncated payloads, oversized payloads and unexpected trailing bytes. It accepts an optional LF or CRLF terminator.

`WaveformCapture` retains a caller-supplied acquisition ID, channel, validated preamble, raw signed 16-bit ADC bytes, and byte order. `TimeAt` returns UnitsNet `Time` and `VoltageAt` returns `ElectricPotential`.

## Example

```csharp
using BenchDouble.Oscilloscopes;
using UnitsNet;

byte[] frame = "#14"u8.ToArray().Concat(new byte[] { 100, 0, 156, 255 }).ToArray();
byte[] payload = ScpiBinaryBlock.Decode(frame);
var capture = new WaveformCapture(
    Guid.NewGuid(), 1,
    new WaveformPreamble(1e-6, -2e-6, 0, 0.01, 0, 0),
    payload, littleEndian: true);
Console.WriteLine($"Samples: {capture.SampleCount}");
Console.WriteLine($"First: {capture.TimeAt(0).Microseconds} us, {capture.VoltageAt(0).Volts} V");
```

Expected: 2 samples; first at -2 microseconds and 1 V. The waveform is synthetic and does not require equipment.

Run `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj` on .NET 10. No passing run is claimed here.

## Boundaries

The parser currently accepts a complete in-memory block only, not streaming chunks. Capture decoding currently assumes signed 16-bit integer samples, not 8-bit, unsigned, float, or vendor-specific preambles. It does not manage transport, trigger/acquisition state, physical calibration, or artifact-store persistence. Those are tracked in [issue #66](https://github.com/jeffadickson/BenchForge/issues/66), with generator integration in [#63](https://github.com/jeffadickson/BenchForge/issues/63). Real instrument command mappings require manufacturer review.
