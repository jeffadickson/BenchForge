# Host fencing ledger: prototype and safety boundary

`HostFencingLedger` persists a host epoch and per-resource generation counters to a JSON file. Opening the ledger increments and writes the epoch; `NextGeneration(resourceId)` persists a generation before returning it. Corrupt JSON or invalid negative values fail closed.

```csharp
var ledger = new HostFencingLedger("host-state/fencing.json");
var registry = new HostedOscilloscopeRegistry(ledger: ledger);
var lease = registry.Reserve("SCOPE1");
Console.WriteLine($"Epoch {ledger.Epoch}, generation {registry.Generation("SCOPE1")}");
```

This is optional in the prototype and not currently registered as the default ASP.NET Core host service. The existing public HTTP request contracts carry only a GUID, not epoch and generation. Consequently these counters are **not yet enforced as fencing tokens** by command dispatch. They must not be described as production-safe restart fencing.

## Critical follow-on acceptance

1. Guarantee single-writer ownership of the ledger across processes and hosts. A JSON rename alone does not provide a cross-process lock, database transaction or distributed consensus.
2. Crash-safe recovery must preserve the last committed generation. Test disk-full, rename failure, process kill, power loss and filesystem durability; a file flush does not universally guarantee parent-directory durability.
3. Bind every reservation and command to authenticated identity, host epoch and lease generation. Reject stale epochs and generations before dispatch and during revalidation.
4. Use monotonic clock for live expiry, durable wall-clock evidence for recovery, and explicit bounded heartbeat budgets.
5. Reconcile in-flight command outcomes after restart using a durable command receipt ledger. Never claim exactly-once physical effects.
6. Independently verify safe-state controls for hazardous hardware; a persisted counter cannot stop an instrument output.
7. Confirm all tests and .NET 10 CI pass on Windows, Linux and macOS.

Tracked in issues #56–#58. Source and tests are committed but CI results have not been verified.
