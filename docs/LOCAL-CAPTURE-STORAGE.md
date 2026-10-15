# Save a scope trace and screenshot locally

The first implemented artifact backend is `BenchDouble.Artifacts.Sqlite`.
It stores **the original bytes and searchable metadata together in a local SQLite
file**. It does not store a filename pointing to a separate user-managed image.
No database server, cloud account or network is required.

The application receives a stable artifact ID and a SHA-256 digest. A run ID plus
acquisition ID connects the waveform and screenshot, even though each has its own
artifact ID. The existing time-series recorder continues to handle scalar readings;
applications must explicitly carry these acquisition IDs across their records.
Automatic instrument-to-recorder integration is not implemented yet.

## Run the complete example

```sh
make setup
make demo-capture
```

Or run `dotnet run --project examples/BenchDouble.CaptureDemo`. The example prints
where it wrote `capture-demo-output/captures.sqlite`, saves four synthetic voltage
samples and a one-pixel PNG fixture, finds both by acquisition and exports a verified
`capture.png`. These are synthetic fixtures, not a live oscilloscope acquisition.
Re-running uses the same IDs and metadata and verifies the existing records rather
than adding duplicates. Real captures must use actual acquisition UTC and distinct
IDs, retained across retries of the *same* save.

## Application example

```csharp
IArtifactStore captures = SqliteArtifactStore.Open("runs/captures.sqlite");
var id = Guid.NewGuid(); // Persist this ID with the acquisition before retrying saves.
var manifest = new CaptureDescriptor(
    RunId: "RUN-42", AcquisitionId: "ACQ-7", InstrumentId: "SCOPE-1",
    CapturedUtc: capturedUtc, // Actual capture time, offset zero.
    MediaType: "application/octet-stream", Kind: "waveform",
    Identification: identificationAtCapture,
    Waveform: new WaveformDescription(
        Channel: "CH1", Encoding: "float64-le/v1", SampleCount: sampleCount,
        Unit: "V", SampleIntervalSeconds: intervalSeconds,
        TimeOriginSeconds: triggerOffsetSeconds, VerticalScale: 1,
        VerticalOffset: 0));
ArtifactRecord saved = await captures.SaveAsync(id, manifest, rawBytes,
    exactByteLength, cancellationToken);
```

The caller supplies the stream, exact length and truthful format description. The
store preserves opaque bytes; it does **not** decode vendor formats, validate PNGs,
verify that sample count matches an arbitrary encoding, or infer missing calibration.
Optional waveform fields stay null when unknown. Settings/probe/calibration details
can be retained in `SettingsJson`, a bounded JSON object. The database schema is v1;
unsupported future versions are rejected rather than silently migrated.

For a screenshot, use a separate ID with the same run/acquisition IDs, `Kind:
"screenshot"`, `MediaType: "image/png"` and no waveform manifest. The caller owns
input/output streams and must dispose them. Acquisition identity is a search key,
not an authorization mechanism.

## Where the data lives

| Item | Location and meaning |
|---|---|
| Bytes | `artifacts.payload`, streamed as a SQLite BLOB |
| Provenance | The same row: IDs, acquisition time, instrument identification and decoding manifest |
| Integrity | Byte count and SHA-256 of the original payload |
| Replication intent | `artifact_outbox`, atomically inserted with state `pending` |
| Exported PNG | Optional verified convenience copy; the database remains the stored original |
| Remote copy | **None yet.** A pending outbox entry does not mean an upload occurred |

Use `FindAsync(runId, acquisitionId)` to find related captures. It returns metadata
only, with a maximum of 100 results by default and a configurable limit up to 1000.
Results reaching the limit may be truncated; pagination is not yet implemented.
Use `CopyToAsync(artifactId, destination)` to retrieve and verify bytes.

**Publish an exported file only after `CopyToAsync` succeeds.** Verification happens
while streaming, so a corrupt capture or cancelled copy can already have written
partial output. Write to a temporary file, close it after success, then rename it;
delete the temporary file on failure. The runnable example demonstrates this.

## What success and failure mean

| Result | Caller action |
|---|---|
| `SaveAsync` succeeds | Bytes, metadata and outbox are locally committed; no remote durability is implied |
| Same ID, same descriptor and bytes | Existing record is returned after both incoming and persisted bytes verify |
| Same ID, different metadata/bytes | `InvalidDataException`; existing capture is not overwritten |
| Short/long input or stream failure | Transaction rolls back; no capture or outbox entry becomes visible |
| Cancellation before commit | Operation aborts and rolls back; no guarantee about the physical instrument is implied |
| Lost response around commit | Retry the same ID, exact descriptor and bytes; never blindly recapture the instrument |
| Quota exceeded | Explicit failure; accepted captures are retained, with no automatic deletion |
| SQLite/OS/disk error | Exception propagates; the application must apply its recording-failure pause/stop policy |
| Missing/corrupt retrieval | Explicit failure; discard partial exported output |

SQLite WAL and synchronous FULL provide the commit/recovery mechanism. All new
payload bytes, metadata and outbox changes share one transaction, so this local
backend has no separately finalized file to reconcile. A process-exit test confirms
rollback of an interrupted transaction while preserving an earlier committed capture.
This is not evidence of power-loss behavior on every filesystem/device.

## Limits and unfinished capabilities

- Payload limit defaults to 64 MiB, configurable up to 512 MiB. Total logical payload
  quota defaults to 1 GiB. These are configuration guards, **not measured optimal
  thresholds**. Database indexes, WAL and temporary disk overhead need extra space.
- A 64 KiB managed transfer buffer bounds copying overhead. The native SQLite cache,
  caller buffers and manifest also consume memory. The current test streams 2 MiB;
  larger-than-process-memory acceptance remains unverified.
- SQLite writes synchronously and permits one writer. Transfers hold a write
  transaction, with a five-second lock wait. Keep storage away from control/safety
  threads. All writers must use the same configured quota policy.
- No remote replicator, automatic retention, range API, authorization service,
  online backup/export service, decoder, downsampling or storage benchmarks yet.
- Do not copy a live `.sqlite` file alone: committed data may reside in its WAL.
  Quiescent relocation is tested only after all operations/connections finish.
  Use a supported SQLite backup mechanism for a live system; a coordinated backup
  API and restore workflow remain required work.
- Hashes detect accidental corruption; they are not signatures or tamper evidence.

## Developer verification

```sh
make artifact-check
```

This runs xUnit tests plus a child-process termination/recovery probe. Bootstrap
already discovers the new library, tests, demo and probe projects. The backend reuses
Microsoft.Data.Sqlite 8.0.11 from the existing SQLite recorder. API documentation
covers every new public member; `make docs-check` must remain free of new gaps.

See [architecture](BINARY-CAPTURE-STORAGE.md) for optional object-store design and
[local execution evidence](ARTIFACT-STORAGE-VALIDATION-2026-10-15.md) for what was
actually run in this development environment. Work remains tracked in #62 and
BD-ART-001..005; none is marked fully Verified.
