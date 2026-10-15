# Coordinated oscilloscope acquisition

The `OscilloscopeSessionCoordinator` owns one `IAsyncBinaryScpiSession` and provides a semaphore-protected transaction across **all** commands required for one acquisition. All in-process clients must share the same coordinator and must not bypass it to call the raw transport. The `CoordinatedOscilloscope` is a simulation-oriented facade that uses this mechanism.

```csharp
using BenchDouble.Oscilloscopes;

var session = new SimulatedOscilloscopeSession(channels: 2, samples: 128);
var coordinator = new OscilloscopeSessionCoordinator(session);
var scope = new CoordinatedOscilloscope(coordinator);
var acquisitions = await Task.WhenAll(
    scope.AcquireAsync(1, Guid.NewGuid()).AsTask(),
    scope.AcquireAsync(2, Guid.NewGuid()).AsTask());
Console.WriteLine(acquisitions[0].VoltageAt(0).Volts); // 0 V
Console.WriteLine(acquisitions[1].VoltageAt(0).Volts); // 1 V
```

The two acquisitions do not interleave their channel-selection, preamble or waveform-data commands. `tests/BenchDouble.Tests/OscilloscopeSessionCoordinatorTests.cs` covers concurrent acquisitions, cancelled waiters and failure latching. Run with `dotnet test tests/BenchDouble.Tests/BenchDouble.Tests.csproj` on .NET 10.

## Recovery policy

A transaction error after entry marks the coordinator `RequiresRecovery`; subsequent transactions fail fast. `ConfirmExternalRecoveryAsync` only clears that latch. It does **not** reset or reopen the physical device, drain unread bytes, clear SCPI status, or prove that outputs are safe. A trusted host must perform those actions and verify readiness before clearing the latch. Never automatically retry an actuation after an uncertain outcome.

## Remaining limitations

This implementation is not a distributed lease, not cross-process locking, and not an authoritative host-side resource manager. The raw transport must not be exposed to independent clients. The binary reader still leaves trailing terminators to the transport and does not guarantee stream resynchronization. Full recovery needs an explicit transport reopen and health-check interface, fencing and persisted lease generations on the instrument host. Manufacturer-specific trigger-completion polling and binary formats are also pending. Source and tests are committed; passing CI evidence is not yet established.
