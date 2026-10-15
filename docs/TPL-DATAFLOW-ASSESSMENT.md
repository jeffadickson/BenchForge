# TPL Dataflow Assessment for BenchDouble

**Decision (2026-10-15):** Use `System.Threading.Tasks.Dataflow` **selectively** as an internal implementation tool for host-owned, serialized instrument command actors and selected bounded processing pipelines. Do not make it the public instrument API, the procedure definition format, the reservation authority, the durable workflow engine, or the safety system.

## Why it fits
- `ActionBlock<T>` with `MaxDegreeOfParallelism = 1` provides a sequential async mailbox per instrument.
- `BoundedCapacity` + `SendAsync` provide admission backpressure instead of unbounded pending commands.
- `TransformBlock<TIn,TOut>` can run CPU-bound postprocessing of immutable measurement frames; use bounded parallelism and document ordering.
- `BufferBlock<T>` can queue bounded data for one consumer, but it does not broadcast each event to all subscribers.
- `BroadcastBlock<T>` holds latest values; it is **not** a reliable per-subscriber audit/log stream. Use independent bounded channels + persisted cursor/replay for host/client logging and shared sensors.
- `BatchBlock<T>` can group bounded samples; `JoinBlock` can pair streams but is not a substitute for the procedure's explicit dependency DAG.

## Concrete layering

```text
Integrated procedure editor and experiment scheduler (versioned steps / durable events)
  -> authorized, fenced instrument lease (owning host)
  -> InstrumentActor<T> (ActionBlock with capacity and concurrency=1)
      -> operation dispatcher (typed IDs + UnitsNet + capability checks)
      -> real IVI/SCPI/legacy GPIB OR stateful mock backend
      -> immutable measurement/event records
          -> durable results/audit store
          -> per-subscriber bounded live streams (Channels/gRPC)
```

The actor's mailbox is scoped to **canonical physical resource ID**, not to an individual driver object or remote client session. Host-level leases gate **enqueue and execution**, with fencing/identity checked again at dispatch. Only one actor owns each physical session. Clients cannot create their own actors to bypass the host. Distinct instrument actors can run concurrently.

## Sketch (illustrative; not current BenchDouble API)

```csharp
using System.Threading.Tasks.Dataflow;

public sealed class InstrumentActor
{
    private readonly ActionBlock<CommandRequest> _mailbox;

    public InstrumentActor(Func<CommandRequest, Task> execute)
    {
        _mailbox = new ActionBlock<CommandRequest>(
            execute,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                BoundedCapacity = 32,
                EnsureOrdered = true
            });
    }

    public Task<bool> EnqueueAsync(CommandRequest request, CancellationToken ct) =>
        _mailbox.SendAsync(request, ct);
}
```

**Warning:** `SendAsync` returning true means **accepted into the block**, not completed by hardware. Production `CommandRequest` needs an operation ID, lease/fence, deadline and `TaskCompletionSource<OperationResult>` (or equivalent) so the caller can await the *actual* outcome. `false` means declined (e.g. completed/faulted block). A queue can fault and stop processing; all pending callers must receive terminal outcomes, and the physical resource must transition to recovery as required. Cancellation while waiting for admission differs from cancellation after dispatch; only the latter may leave hardware state uncertain.

## What Dataflow must NOT own

1. **Reservation and authorization:** host-owned durable lease manager and fencing, separate from mailbox serialization.
2. **Multi-step instrument atomicity:** a SCPI write/query pair or configure-acquire-cleanup sequence may require an atomic actor request; simply posting multiple commands allows interleaving.
3. **Procedure revision, redline, signoff and durable checkpoints:** these belong to the step engine/event store.
4. **Safety interlocks and emergency stop:** high-priority safety actions must not be trapped behind a long-running bounded normal command queue. Provide a separate authorized safety path with validated abort semantics and a recovery state; never concurrently access an unsafe VISA session.
5. **Network delivery:** Dataflow is in-process. Remote gRPC requests, authentication, deduplication, reconnect and versioning are separate.
6. **Shared sensor fan-out and audit logs:** ordinary `BroadcastBlock` is not a durable multi-client event bus.
7. **Deterministic virtual time:** inject a shared simulation clock; don't assume Task scheduling gives repeatable mock event order.
8. **Automatic retries:** distinguish idempotent queries from state-changing commands; do not retry non-idempotent operations without explicit device-specific policy.

## Alternative comparison

| Need | Prefer | Why |
|---|---|---|
| Single-instrument serialized command mailbox | `ActionBlock<T>` | Async delegate, bounded capacity, simple actor semantics |
| Single producer/consumer streaming | `System.Threading.Channels` | Lower abstraction cost and good `IAsyncEnumerable` integration |
| Complex CPU processing graph | TPL Dataflow transforms | Bounded parallel stages, routing, batching |
| Experiment dependency DAG | Explicit step scheduler | Dependencies, holds, redlines and persistent state are domain concepts |
| Real-time telemetry to multiple clients | Host fan-out channels + durable store | Subscriber isolation, replay and auditability |
| One-off async calls | Plain `async/await` | No reason to create a graph |
| Distributed durable orchestration | Procedure service + event store | TPL Dataflow is not distributed or persistent |

## Tests before adopting

- 100 concurrent requests to one instrument: no overlap, exact accepted request count, correct response correlation.
- Two independent instruments run concurrently without unnecessary global lock.
- Bounded capacity: blocked senders cancel without command dispatch; no dropped accepted commands.
- Cancel after dispatch, timeout, host loss, driver crash: report unknown outcome and safe recovery; pending completions terminate.
- Queue fault: no indefinite hung tasks, logs and leases remain consistent.
- Multi-command transaction cannot interleave with another client.
- Lease expiry/fencing is rechecked when dequeuing, not only on enqueue.
- Command priority and emergency-stop behavior is independently verified.
- Simulated 1x/100x/manual clocks yield identical logical results.
- Benchmark ActionBlock vs Channel<T> vs current SemaphoreSlim under realistic I/O and burst load; choose by measured simplicity/latency/memory.

## Adoption steps
1. Prototype `InstrumentActor` in an internal host component with no public TPL Dataflow types.
2. Test on mock Keithley 236 and DMM: command/response serialization, cancellation and bounded admission.
3. Benchmark against `Channel<T>` and existing `SerializedAsyncScpiSession`; retain whichever is simpler and robust.
4. Add Dataflow package only to the component using it, not the core instrument contracts. For current net8 target, use a compatible stable package version and pin it in reproducible dependencies.
5. Add optional processing pipelines after actor correctness; do not make every operation a block.

## Sources
- https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/dataflow-task-parallel-library
- https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.dataflow
- https://www.nuget.org/packages/System.Threading.Tasks.Dataflow/

**Status:** architectural assessment only. No TPL Dataflow actor has been added or tested by this document.
