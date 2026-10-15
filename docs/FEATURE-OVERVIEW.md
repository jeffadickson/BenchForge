# BenchForge: instrument automation with explicit evidence

BenchForge aims to make experiments ordinary, testable C# programs while keeping
instrument ownership, units, simulation, failure recovery and evidence explicit.
This is an evolving prototype, not a completed LabVIEW replacement or a verified
physical safety system. The requirements scoreboard distinguishes those claims.

| Capability | Present implementation | Remaining work |
|---|---|---|
| Typed instrument APIs | C# interfaces and UnitsNet quantities | Broader driver coverage and hardware qualification |
| Hardware-free development | Stateful mocks, strict transcripts, virtual time and injected behavior | Full command/mode coverage and integrated acceptance |
| Instrument Definer | Static PyMeasure extraction, CSV import, review drafts, mock preview | Full schema validation, inherited extraction and reviewed v2 generation |
| Local-first routing | Optional JSON directory, localhost default, logged decisions, role-bound HTTP client | Production authenticated remote hosting and managed-open integration |
| Multiple experiments | Per-resource prototype reservations; different resources can be reserved independently | Authenticated owner/run, expiry and fencing |
| Multiple observers | Cached read-only host snapshots and dashboard polling | Registered streaming subscriptions and replay |
| Identification | Demo-host open-time query, structured logs and cached dashboard identity | Every backend/reconnect and durable identity history |
| Measurement storage | SQLite and unit-aware sample APIs | Offline-to-central replication and complete provenance |
| Cleanup | Runner's bounded independent cleanup and recovery latch | Shared lifecycle registry, durable quarantine and device-specific confirmation |
| Reliable remote commands | Requirements and architecture baseline | Durable receipts, uncertainty reconciliation and explicit retry policy |
| Requirements and testing | StrictDoc, GitHub issues, xUnit/Reqnroll/Playwright source and generated scoreboard | Passing cross-platform acceptance and hardware evidence |

The useful combination is typed quantities, deterministic simulation, one reviewed
definition for driver/mock generation, and explicit failure/evidence semantics.
It is an engineering objective rather than proof of superiority over other tools.

Start with [setup and configuration examples](LOCAL-FIRST-CONFIGURATION.md),
[Instrument Definer](INSTRUMENT-DEFINER.md), and
[reliability architecture](RELIABILITY-ARCHITECTURE.md). Build the searchable
scoreboard with `make scoreboard`; export StrictDoc with `make requirements`.
Routine pushes skip hosted CI; local tests and milestone verification are separate.

## Local trace and screenshot storage

`BenchForge.Artifacts.Sqlite` now stores immutable capture bytes plus searchable
metadata and a pending outbox in one local transaction. It supports bounded
streaming, SHA-256 verification, idempotent IDs and acquisition lookup.
[Run the capture example](LOCAL-CAPTURE-STORAGE.md). Remote upload, retention,
online backup and comparative storage benchmarks remain pending; the pending
outbox is not a remote-durability claim.


## Hands-on examples

See [Examples](EXAMPLES.md) for runnable commands, expected observations, limitations and validation steps. All examples distinguish simulated operation from physical instrument control.
