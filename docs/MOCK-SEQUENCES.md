# Stateful mock output sequences

TR-043 now has an explicit sequence API through `StatefulMockEngine` and
`ImportedInstrumentMock`; both use the existing exclusive transaction gate.
This is simulation only and never opens or controls physical instruments.

```csharp
mock.SetSequence("voltage", new[] { 0.1, 0.2, 0.3 });
await mock.ConfigureModeAsync("voltage");
var first = await mock.MeasureAsync("voltage"); // 0.1 in the declared unit
var remaining = mock.SequenceStatus("voltage")!.Remaining; // 2
```

The capability and mode names must exist in the imported definition. Values are
copied, must be finite and number from one to 4096. Default exhaustion is an
explicit error; pass `repeatLast: true` to hold the final reading. Installing a
new sequence resets its cursor and replaces that capability's sync/async model.
An override remains available as fallback after `ClearSequence`.

Only successful mode-compatible scalar readings consume values. A queued fault,
observed cancellation, invalid mode or invalid clock leaves the cursor unchanged.
Parallel readers consume distinct positions through the canonical gate; thread
scheduling determines which caller receives each value. Captured acquisition
reads continue using immutable acquisition data and do not consume sequences.

`SequenceStatus` reads detached cursor metadata without sampling the clock.
`Snapshot().SequencePositions` captures independent read-only cursor positions
with the mode and revision. Successful cursor advancement changes the revision;
holding the exhausted final value does not. Reset removes all sequences; model
replacement or removal also clears the affected sequence.

Signal components also validate finite output. Seeded noise draws are serialized,
and interpolation supports opposite finite double extremes without overflowing
their difference. Composite signal membership is copied at construction.
These are deterministic software models, not calibrated physics or uncertainty
models. Full transcript replay, expectation cardinalities and isolation of
arbitrary compiled user code remain separate unfinished requirements.

Regression execution for this feature is scheduled at checkpoint 200. Source
links and new tests do not promote requirement status to Verified.

## Detached diagnostics and bulk controls

`StatefulMockEngine.Snapshot()` and `ImportedInstrumentMock.Snapshot()` now include
read-only `PendingFaultCounts` and `BehaviorKinds` maps. They expose neither fault
exception payloads nor callbacks/sequence values. `FaultCount(capability)` reads
queued counts without consulting virtual time. Snapshot capture still samples the
shared simulation clock and is serialized with mode and reading transactions.

Both APIs provide `ClearAllFaults`, `ClearAllSequences`, `ClearAllBehaviors`, and
`ResetAsync`. Bulk removal changes the state revision once only when it removes
something; each operation preserves state outside its documented scope. Async
reset shares the synchronous reset transaction and observes cancellation before
mutation. These are simulation controls; they neither release instrument ownership
nor certify physical safety. Cancellation observed during clock sampling now
precedes fault consumption and sequence advancement.
