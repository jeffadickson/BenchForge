# BenchDouble — Consolidated Test-Lab Runner Requirements

**Baseline:** TR-REQ-001 · 2026-10-14 · **Status:** requirements, not implementation verification.

This is the single indexed requirements baseline for the integrated electronic procedure editor, experiment runner, hardware orchestration, results, simulation, review, and signoff discussed for BenchDouble. It complements the existing subsystem specifications. Stable IDs support traceability and tests. **P0** = correctness/safety core; **P1** = usable beta; **P2** = subsequent enhancement.

**Product boundary:** BenchDouble runs and records measurement-oriented procedures. TestForge can build on its execution engine for formal flight-hardware qualification, verification matrices, approvals, and evidence management; do not create two competing execution engines.

## A. Structured procedure editor and authoring

- **TR-001 P0** One structured procedure definition with stable ID, title, controlled revision, author, parameters, instrument requirements, step tree, acceptance criteria, and approval status.
- **TR-002 P0** The editor and runner operate on the **same structured procedure model**, not parallel HTML/code representations.
- **TR-003 P0** Insert before/after, delete, duplicate, move, reorder, indent/outdent and renumber steps without changing stable step IDs.
- **TR-004 P0** Step types: instruction, instrument configuration, measurement with limits, calculation, wait, condition/branch, loop, operator prompt, inspection, safety hold, attachment, signature, C# action, and subprocedure.
- **TR-005 P1** Typed step inputs, defaults, enums, UnitsNet quantities, validation and help text.
- **TR-006 P0** Draft, approved procedure revision, execution snapshot, redline, and deviation are separate versioned records.
- **TR-007 P0** Executions pin the exact approved revision/hash; redlines cannot silently overwrite it.
- **TR-008 P1** Step templates, reusable subprocedures, library search, cross-references and dependency validation.
- **TR-009 P1** Transactional edits, undo/redo, deterministic serialization, position mapping and autosave.
- **TR-010 P1** Inline review comments, change diffs, history and reviewer workflow.
- **TR-011 P1** C# custom-action editing with compilation diagnostics and typed instrument APIs, but not as the underlying document format.
- **TR-012 P1** Editor technology remains replaceable; Monaco may become the step editor or code editor. Do not prematurely constrain the structured document model to Monaco.
- **TR-013 P1** Preserve the independently specified C#-centric structured editor engine direction: no required ProseMirror/Tiptap dependency; browser JavaScript only for necessary interop.
- **TR-014 P2** Accessible keyboard navigation, IME, rich text, tables, attachments and eventual multi-user editing with conflict handling.

## B. Runner lifecycle and concurrency

- **TR-015 P0** Run identity, pinned revision, operator, backend, parameters, instrument inventory, start/finish times, status and outcome.
- **TR-016 P0** Auditable states: Created, Validating, Reserving, Startup, Running, Cleanup, Completed, Failed, Cancelled, RecoveryRequired.
- **TR-017 P0** Explicit startup/execute/shutdown phases; cleanup executes after success, exception or cancellation.
- **TR-018 P0** Cleanup has a bounded policy and device-specific abort/disable confirmation; failure reports OutcomeUnknown/RecoveryRequired.
- **TR-019 P0** Validate procedure inputs, driver capabilities, permissions and resource availability before physical actuation.
- **TR-020 P0** Async Task/ValueTask, CancellationToken, deadlines, Task.WhenAll/WhenAny and IAsyncEnumerable for streaming.
- **TR-021 P0** Serialize per-instrument commands and full query/response transactions; independent devices may operate concurrently.
- **TR-022 P0** Cancelling an awaited task does not imply physical acquisition abort or safe-state confirmation.
- **TR-023 P1** Queue/schedule runs with fairness, progress, priority, operator holds and resource-aware admission.
- **TR-024 P1** Pause, resume, skip, retry, abort and approved checkpoint restart with clear audit and idempotency semantics.
- **TR-025 P1** Reserve all required instruments before coordinated execution; deadlock-free host-local atomic grants and failure-aware multi-host acquisition.
- **TR-026 P1** Support dependencies and partial-order barriers between concurrent operations; don't impose arbitrary global total order.
- **TR-027 P1** Live per-step state, progress, measurements, logs and errors, with reconciliation after browser/network disconnect.
- **TR-028 P2** Parameter sweeps, nested procedures, streaming acquisitions and reusable calculations.

## C. Distributed instrument hosts

- **TR-029 P0** Same procedure code runs with physical, simulated and strict-mock backends.
- **TR-030 P0** Instrument host is a separate, **platform-neutral** service: any supported computer with the necessary .NET runtime and instrument drivers.
- **TR-031 P0** Host is authoritative for resource identity, ownership, device operations and reservation policy; clients cannot bypass it.
- **TR-032 P0** Exclusive control default; leases identity-bound with expiry, renewal, fencing tokens and alias canonicalization.
- **TR-033 P0** Multiple sessions, driver versions or clients cannot independently control the same physical resource.
- **TR-034 P1** Device-specific policies: exclusive, safe shared read-only, exclusive controller with observers, host-sampled publish/subscribe.
- **TR-035 P1** Observers receive host-produced measurements/logs, not arbitrary independent SCPI reads that could change state.
- **TR-036 P0** Record host, manufacturer/model/firmware, driver/version/hash, instrument capability and calibration reference.
- **TR-037 P1** Use official IVI class interfaces where available, SCPI/VISA otherwise, with vendor-specific optional capabilities.
- **TR-038 P1** Versioned protobuf/gRPC contracts with capability negotiation; never serialize arbitrary CLR objects over the network.
- **TR-039 P0** Authentication, TLS for non-loopback, operation-level authorization, and no arbitrary untrusted code in privileged host.
- **TR-040 P2** Optional signed content-addressed driver packages in isolated worker processes, with version coexistence but single physical reservation authority.

## D. Mocks, simulation and verification

- **TR-041 P0** Mock definition separates initialize/default state from each operation's state transition and measurement behavior.
- **TR-042 P0** DMM voltage/resistance mode changes determine subsequent measurement quantity, behavior and validity.
- **TR-043 P0** Mock outputs support constants, sequences, sine, seeded noise, state-dependent functions, injected faults and timeouts.
- **TR-044 P1** One explicit run-scoped shared simulation clock for all instruments and virtual DUT components.
- **TR-045 P1** Real-time, accelerated, paused and manually advanced virtual-time modes; speed factor changes wall duration, not modeled physical dynamics.
- **TR-046 P0** Physical safety delays, lease expiry, authentication and real hardware timeouts never use accelerated virtual time.
- **TR-047 P1** Model initialization/warmup, settling, integration, trigger/acquisition, processing latency, jitter and optional display pacing separately.
- **TR-048 P1** Strict EasyMock-style expected sequences, unordered regions, cardinality, argument matchers, fault injection and cross-device partial-order expectations.
- **TR-049 P1** Reproducible seeded randomness, virtual timestamps, state snapshots, deterministic scheduling and transcript replay.
- **TR-050 P2** Virtual wiring/DUT transfer functions and validated noise/uncertainty models; never claim simulator calibration.

## E. Measurements and durable data

- **TR-051 P0** UnitsNet quantities (volts, ohms, amperes, hertz, temperature, duration); reject dimension mismatches.
- **TR-052 P0** Preserve DC/RMS/peak/Vpp, measurement mode, range, limits, resolution, quality, uncertainty and source metadata.
- **TR-053 P0** Record acquisition timestamp, host wall time, simulation elapsed time if relevant, run/step IDs and driver identity.
- **TR-054 P0** Raw readings and run events are append-only; redlines/corrections cannot rewrite original evidence.
- **TR-055 P1** Durable local persistence (SQLite initially), checkpoints, crash recovery and transactional consistency.
- **TR-056 P1** Persist procedure revision/hash, configuration, operator, equipment/calibration, measurement rows, outcomes, attachments and approvals.
- **TR-057 P1** CSV/machine-readable export, tables, filtering, charts, waveform display, run comparison and reports.
- **TR-058 P1** Operate without central database/network, then synchronize with stable IDs, conflict detection, duplicate suppression and explicit reconciliation.
- **TR-059 P1** Hosts retain bounded event spools; reconnect replays available events with visible gap markers.
- **TR-060 P1** Pass/fail/limits calculations are separate traceable results, never substituted for original measured values.

## F. Redlines, approvals and electronic signoff

- **TR-061 P0** Execution-scoped redlines include before/after, rationale, author, timestamp, review state and affected steps.
- **TR-062 P0** Reviewers approve/reject/request changes; released procedure definitions remain immutable.
- **TR-063 P0** Attributable electronic signatures at step, hold point, section and final run with explicit signing intent.
- **TR-064 P0** Signatures bind signer identity/role, timestamp and exact content hash; post-sign changes require new attestation.
- **TR-065 P1** Role-based permissions for author, operator, reviewer, approver and administrator, including optional separation of duties.
- **TR-066 P1** Corrections after submission remain possible only as logged attributable changes/superseding records.
- **TR-067 P1** Deviations/nonconformances, disposition, follow-up actions and operator notes/photos/files link to stable step IDs.
- **TR-068 P0** No 21 CFR Part 11 or other regulatory compliance claim without dedicated validation of identity, signatures, retention and audit controls.

## G. Unified editor/runner UX

- **TR-069 P0** One browser workspace composes, inserts, executes, redlines, reviews and signs off steps; notebook not required.
- **TR-070 P0** Inline measurement values, limits, pass/fail, logs, operator prompts and step progress; minimal presses.
- **TR-071 P1** Easy pre-submit correction, logged post-submit changes, clear confirmations for destructive or hazardous operations.
- **TR-072 P1** Host and client show the same authorized log events with event IDs, step/run correlation, filters and replay.
- **TR-073 P1** Live graphs, waveform plots, sortable measurement tables and time axes with correct units.
- **TR-074 P1** Accessible responsive layout, keyboard support, navigation/search and persistent unsaved editing state.
- **TR-075 P2** Optional Monaco-backed C# or structured step editing without coupling procedure storage to a particular editor.

## H. Observability, security and operational quality

- **TR-076 P0** Microsoft.Extensions.Logging structured events for commands, leases, runs, steps, results, approvals, faults and cleanup.
- **TR-077 P1** OpenTelemetry traces/metrics propagate host, instrument, run, step and request correlations.
- **TR-078 P0** Redact sensitive SCPI payloads by default; privileged debugging requires policy and retention controls.
- **TR-079 P0** Slow log subscribers cannot block instrument I/O; critical alarms preserved with explicit loss/gap reporting.
- **TR-080 P0** User-supplied C# runs in a restricted worker, not the privileged instrument host; host permissions remain authoritative.
- **TR-081 P1** Audit retention, encryption, backup, trace IDs, clock discipline and tamper-evidence policy are documented and tested.

## I. Acceptance and verification

- **TR-082 P0** Reproducible cross-platform build and hardware-free CI; actual VISA/IVI tests opt-in.
- **TR-083 P0** Test startup failure, cancellation before/after dispatch, timeouts, cleanup failure and uncertain hardware state.
- **TR-084 P0** Test lease conflicts, multi-instrument deadlock prevention, shared observers and mixed client/host versions.
- **TR-085 P1** Test redlines, approval, signature hashes, offline conflicts and immutable execution records.
- **TR-086 P1** Test deterministic mock timing, scaling, state transitions, noise seeds and partial-order expectations.
- **TR-087 P1** Test host restart, disconnect, stale fencing tokens, log replay and unauthorized commands.
- **TR-088 P1** End-to-end frequency sweep spanning two simulated hosts: reserve, configure, acquire, stream, plot, log, cancel and cleanup.
- **TR-089 P1** Six-step electronic procedure demonstration: insert/reorder, execute, redline, approve, sign off and archive.
- **TR-090 P2** Repeat same compiled procedure with approved physical drivers; compare semantic behavior, not identical SCPI transcripts.

## J. Implementation inventory (not inferred from requirements)

**Existing prototype source:** `src/BenchDouble/Experiments/ExperimentRunner.cs` lifecycle and in-memory results; synchronous and async SCPI session facades; basic strict replay; simulator models; in-process reservation manager; loopback-only simulated `Host.Server` and `Host.Client`; UnitsNet facades; local structured logging.

**Not yet demonstrated as complete:** integrated procedure editor/signoff; production scheduling; persistent results; secure distributed host leases; real VISA and full IVI driver interchangeability; streaming host logs; shared virtual clock; advanced mock partial-order verifier; offline sync; safe physical recovery. The repository has not established a complete green .NET CI baseline in the referenced development sessions.

## K. Normative companion documents

- [Integrated procedure editor](INTEGRATED-PROCEDURE-RUNNER.md)
- [Experiment runner lifecycle](EXPERIMENT-RUNNER.md)
- [Distributed instrument hosts](DISTRIBUTED-INSTRUMENT-HOST.md)
- [Versioned drivers and shared resources](VERSIONED-DRIVERS-AND-SHARING.md)
- [Mock lifecycle and simulation time](MOCK-TIME-AND-LIFECYCLE.md)
- [UnitsNet and measurement semantics](UNITS-AND-MEASUREMENTS.md)
- [Logging and observability](LOGGING.md)
- [Instrument driver generation](DRIVER-GENERATION.md)
- [Package architecture](PACKAGE-ARCHITECTURE.md)
- [Main software description](SOFTWARE-DESCRIPTION.md)

## L. First integrated delivery plan

**Milestone 1:** Validate existing build and run kernel; durable local result store, typed step execution, cancellation and bounded cleanup. **Milestone 2:** Integrated step editor and versioned procedure snapshots; six-step simulator demonstration. **Milestone 3:** Redlines, review, signoff and immutable audit. **Milestone 4:** Secure distributed host leases and correlated live log/measurement streaming. **Milestone 5:** Multi-host sweep, deterministic mocks, offline sync, opt-in hardware verification.

Every milestone must include automated acceptance tests, status evidence and an updated requirement-to-test traceability matrix. Do not mark requirements complete merely because a document or interface exists.
