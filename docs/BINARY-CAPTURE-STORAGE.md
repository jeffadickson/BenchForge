# Binary scope traces and screenshots

Requirements: BD-ART-001..005. [Implementation/benchmark issue #62](https://github.com/jeffadickson/BenchDouble/issues/62).
This is an architecture decision, not an implemented artifact backend.

## Recommended design

Use a managed artifact service alongside the time-series recorder. Experiments
save streams and receive stable artifact IDs. They never construct storage paths.
The database indexes acquisitions, ownership and artifact metadata; immutable
bytes live behind a storage interface with a local durable implementation first
and optional object storage replication. Small embedded deployments may use an
SQLite BLOB adapter behind the same contract. Do not choose a universal size
threshold until representative capture sizes and read patterns are benchmarked.

| Data | Representation |
|---|---|
| Numeric readings, trends | Existing time-series interface |
| Raw oscilloscope capture | Original vendor bytes plus a versioned decoding manifest; optionally a normalized array representation |
| Screenshot | Original PNG/JPEG bytes, media type, acquisition link |
| Search/index | Transactional metadata keyed by run, instrument, acquisition and artifact ID |
| Central bulk retention | Optional immutable object store; metadata contains opaque object identity/version, never an expiring download URL |

Modern BLOBs are not inherently unsuitable. PostgreSQL `bytea` uses TOAST for
large values; its separate large-object interface supports streamed/partial access.
The engineering tradeoff is capture volume, partial reads, backup/restore cost,
replication load and transactional simplicity. Measure those costs on our workload.
Object storage is the preferred scalable central backend, not a requirement to use
a cloud service. A local-only instrument server must work without it.

## Manifest and provenance

Each artifact has an opaque ID, schema version, run/acquisition/instrument IDs,
captured UTC (distinct from upload time), optional simulation time, instrument
identification snapshot, media type, byte count and SHA-256 of the exact stored
bytes. Keep original filename as a display label only. Backend locator and status
are service-managed; moving storage must not invalidate references.

Waveforms additionally record channel, sample count, encoding/dtype, byte order,
sample interval or explicit timestamp array, trigger/time origin, units, vertical
scale/offset, acquisition settings and calibration/probe information when available.
Unknown values are explicitly absent. Never infer units or discard the original
data silently. A screenshot is a related artifact, not a replacement for raw samples.
Derived previews/downsampled data reference their original artifact and transform
version. Access by run and acquisition should retrieve the trace and its screenshot.

## Commit and recovery protocol

1. Allocate an upload/artifact ID and pending metadata record. Stream bytes to a
   bounded local staging area while computing length and digest. Enforce quotas
   and cancellation without buffering the entire trace in memory.
2. Flush the completed local bytes, finalize an immutable object and verify its
   digest. The implementation must define and test its filesystem/OS durability
   guarantees, including directory metadata; rename alone is not a power-loss test.
3. Commit ready metadata plus replication-outbox entry in one database transaction.
   Acknowledge local durability only after both bytes and metadata are committed.
   Remote completion is a separate status. Pending objects never appear as ready.
4. Replicate idempotently with immutable keys/conditional creation, checksum
   verification and a durable checkpoint. Provider ETags are not assumed to be
   SHA-256. Do not delete the local copy before verified remote durability and
   policy approval. Retry immutable transfers safely; this is separate from retrying
   instrument commands, which may have physical side effects.
5. Reconcile crash leftovers: incomplete staging uploads, finalized but unindexed
   objects, pending rows with missing bytes and interrupted transfers. Use a grace
   period and leases before garbage collection; never delete live uploads. Corrupt
   or missing ready objects produce an explicit recording/retrieval fault.

There is no atomic transaction spanning an ordinary database and object store.
The pending/ready state machine and reconciliation are essential to avoid the
broken-filename-reference problem. Content hashes support integrity and optional
deduplication; authorization still follows the artifact/run identity. Deduplication
must not allow one run's retention policy to delete bytes referenced by another.

## Acceptance and operational tests

- Save raw trace and screenshot, restart, retrieve by acquisition ID, verify bytes
  and metadata. Move storage without changing application references.
- Inject interruption before/after each commit boundary, disk full, corrupt bytes,
  missing objects, upload timeout and duplicate requests; recover or report a clear
  fault with no false durable acknowledgment.
- Stream captures larger than available process memory with a bounded working set;
  support bounded/range reads where the format permits them.
- Back up/export a self-contained manifest plus bytes; restore to an empty host
  and validate every digest and relationship. A metadata-only backup is incomplete.
- Apply retention and access checks consistently to original data and previews.
  Link the recording-failure policy to runner pause/stop behavior; never silently
  discard accepted evidence to make room.
- Benchmark embedded BLOBs, managed local files and the chosen central object
  backend using actual screenshot/trace sizes, sequential and range reads,
  concurrent acquisition, cold reads, sync durability and backup/restore time.
  Publish hardware, filesystem, configuration and p50/p95 latency/throughput.

## Sources reviewed

- [PostgreSQL large objects and TOAST](https://www.postgresql.org/docs/current/lo-intro.html)
- [S3 conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html)
- [S3 integrity checks](https://docs.aws.amazon.com/AmazonS3/latest/userguide/checking-object-integrity.html)

The recovery protocol above is our design recommendation. An S3-compatible API
does not by itself guarantee identical consistency or durability behavior; test the
selected backend. Git LFS is not the experiment artifact lifecycle service.
