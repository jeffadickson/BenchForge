# BenchForge alpha milestone

Scope freeze: prioritize instrument import, verified .NET 10 builds and one integrated hardware-free bench. Distributed host TLS/authentication, durable command ledger and physical safety qualification remain separate post-alpha work; existing issues stay open.

## Run the first smoke test

```sh
dotnet run --project examples/BenchForge.AlphaDemo/BenchForge.AlphaDemo.csproj --configuration Release
```

The initial demo acquires a deterministic synthetic waveform using the shared oscilloscope coordinator, checks that its RMS value is consistent with a unit-amplitude sine wave, and writes `alpha-demo-output/capture.json` containing unit-labeled samples and metadata. It uses no physical equipment.

**Important:** the first version does not yet connect an independently configured generator or DMM mock to the scope; its generator and DMM fields are explanatory metadata and a waveform-derived RMS calculation. The target alpha requires one shared simulated source, a generator driver controlling it, an oscilloscope acquiring it, and a DMM mock independently sampling the same signal.

## Acceptance gates

1. All first-party projects restore and build against .NET 10 on Windows, Linux and macOS.
2. All relevant xUnit, Definer, generated-code consistency and documentation checks pass with archived CI evidence.
3. Reviewed schema-v2 definitions generate typed async drivers and mode-aware mocks for the function generator, Keithley 2400 SMU and a specific oscilloscope; unsupported commands fail explicitly.
4. The shared-signal demonstration has reproducible measurements, deterministic time, bounded waveform capture and explicit units.
5. No physical hardware support or distributed safety claim is made without independent qualification.

See issues #63, #65 and #66. This milestone remains open until the tests and integration gates pass.
