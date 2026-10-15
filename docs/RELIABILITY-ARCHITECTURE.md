# Lifecycle and distributed reliability baseline

Status: accepted requirements/design; most implementation remains planned.
Canonical requirements: BD-LIFE-001..006, BD-HB-001..006, BD-CMD-001..008,
BD-RES-001..003, BD-ID-001..004, BD-OWN-001..002, BD-REQ-006.
These refine existing TR-015..040 and observability/recovery requirements; they
do not replace or renumber the original requirements.

## One shared core

Use one instrument lifecycle coordinator, one authoritative lease service per
resource, one command journal/dispatcher and one health supervisor. Adapt current
direct, generated, async, VISA/IVI and remote entry points into those contracts.
Preserve existing facades as compatibility adapters; do not silently change their
constructor behavior to perform network I/O. New managed opens are explicit async
factories. Implementation must identify legacy paths that bypass managed opens.

The registry registers Opening before I/O, resolves aliases, owns partial-open
cleanup and retains recovery state. It must not hold a global lock during slow
instrument I/O. Per-resource serialization and ownership are distinct: a transport
lock protects a query's write/read pair, while a reservation protects a complete
configuration/acquisition sequence. Native device locking is an additional layer.

Health supervision uses monotonic real time and background async services with
cancellation, independent of simulated time and command queues. A responsive host
does not prove a responsive device. Optional observer/database loss does not
automatically terminate a locally autonomous run; policy declares which peers
are required. Each device has one ownership authority, even with many servers.

## Command semantics

Accepted means validated and committed to the local durable journal. Started
means dispatch began. Completed carries an explicit evidence level: transport,
instrument completion or physical confirmation. Failed/Rejected/Cancelled are
distinguished from OutcomeUnknown. Cancellation after dispatch is not proof of
non-execution. Status lookup is authorized and survives restart.

Store command ID, payload hash, run/resource IDs, owner epoch/generation,
deadline, transitions and outcome. Matching duplicates return existing status;
conflicting reuse is rejected. Check authorization/fencing again at dispatch.
Bound both queue depth and deadlines, and reject cleanly on overload or journal
failure. The journal must not depend on a central database being online.

The crash between physical actuation and recording completion cannot be made
transactional by software alone. Reconcile with device evidence, otherwise report
OutcomeUnknown. Automatic retries require reviewed operation-specific repeat
safety and a finite budget. Never blindly repeat pulses, triggers or consuming
reads. A transport exception is not sufficient evidence that nothing happened.

## Recovery and shutdown

Lost contact first marks the peer Suspect. Expiry fences the old owner and invokes
local safety policy. Do not grant a new lease until recovery establishes readiness.
Persist recovery/quarantine across restart, and record authorized operator recovery.
Cleanup has independent finite budgets, dependency-aware ordering and error
aggregation so one failing instrument does not suppress all other cleanup.
Non-cooperative drivers can outlive cancellation; isolate them when needed and
retain quarantine. Process/power loss needs independent hardware protection where
required by the application; finally blocks cannot provide that guarantee.

## Implementation order and exit criteria

1. #55: lifecycle registry, typed states, partial-start rollback and quarantine.
2. #56 with #46: authenticated health, canonical ownership, expiry and fencing.
3. #57: durable command ledger, receipts, status reconciliation and retry classes.
4. #54: integrate all managed opens, identity constraints and durable history.
5. #58: deterministic fault matrix, process-kill tests and hardware evidence.

Add tests alongside each stage; #58 is an integrating gate, not permission to
defer all testing. Procedure/editor/graphical expansion stays deferred. Cross-platform
build evidence (#50) and actual device safe-state confirmation are release gates.
