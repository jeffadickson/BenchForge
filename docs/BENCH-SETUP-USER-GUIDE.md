# Guided bench setup

The local host now provides a guided setup page at **http://127.0.0.1:5278/setup/**. Start it with `make demo-dashboard`, then choose **Set up bench** from the dashboard.

Serial numbers are optional. No serial is required to discover, name, associate, assign or save equipment. Expected manufacturer/model/serial checks are separate opt-in fields, not mandatory identity inputs.

## Working workflow

1. Choose **Simulation inventory** and discover the local host. It presents two simulated GPIB adapters, address 22 on each bus, and an Ethernet alternative for DMM1. These represent the existing host simulators, not hardware attached to your computer.
2. Add the endpoints you want. Name the adapters and confirm which adapter is which. The simulation deliberately omits stable adapter identities and serial metadata so the no-serial workflow is exercised.
3. Name the instruments. When adding both DMM1 routes, explicitly confirm that they represent one physical instrument and select the connection to use. Removing an association or correcting it is possible in the draft before saving. Advanced JSON editing permits arbitrary reassignment; unsupported physical associations remain untested.
4. Assign roles such as `voltmeter` and `generator`.
5. Validate connections. Supported simulator connections are checked under temporary leases from the existing registry. Busy instruments are reported without identity queries. Optional expected identity fields are checked only when provided.
6. Review the previous and proposed JSON side by side, confirm the mappings, then save. Alternatively, explicitly save an **untested offline draft**. Drafts cannot reserve instruments.

The ordinary experiment client location file (`instruments.json`) and this host setup document are different schemas. Use the setup role-reservation API below to resolve roles in this host document. Existing clients can query/release the resulting canonical resource and lease using their existing methods.

## Installed VISA discovery

Select **Installed VISA · GPIB**, **USB**, or **one Ethernet host**. Ethernet requires one explicit DNS name or IPv4 address; this implementation does not sweep a subnet. The host dynamically loads an installed vendor VISA library (`visa64.dll`/`visa32.dll`, `libvisa.so`, or the macOS VISA framework) and uses resource-manager enumeration. It does not call instrument open, read or write functions.

GPIB results distinguish the controller address from the instrument address. Provider-reported interface resources are shown even when no instrument endpoint is found. No stable adapter serial is inferred from a controller index. USB and TCPIP resource descriptors are retained as reported. USB serials present in a resource descriptor are displayed as unverified evidence; they never trigger automatic association. GPIB secondary-address resources are diagnosed as unsupported rather than collapsed onto a primary-address device.

Missing runtimes, unsupported exports, vendor error codes, no matches and unsupported resource shapes produce explicit diagnostics. VISA may only report resources already configured in its vendor utility. Vendor enumeration behavior is outside BenchForge's control; configure and scan physical buses while idle. Cancellation is checked before and between native calls; a blocking vendor enumeration call cannot be interrupted. Enumeration is bounded to 4096 reported resources.

**Current physical boundary:** native enumeration is implemented, but was not verified with a vendor runtime or attached hardware in this environment. Discovered physical endpoints have unknown availability/identity and no registered canonical host driver. They can be named and saved as offline drafts; physical validation, activation and queries remain blocked. This does not claim a complete production VISA transport or cross-process physical ownership service.

The native API signatures follow the IVI Foundation [VPP-4.3 VISA Library Specification](https://www.ivifoundation.org/downloads/VISA/vpp43_2024-01-04.pdf), search services in section 4.4. The code uses `viOpenDefaultRM`, `viFindRsrc`, `viFindNext` and `viClose`; it closes both search and resource-manager handles.

## Import, repair and persistence

Use the JSON editor or a local JSON file to import a draft; import does not save it. Duplicate/unknown fields and structural errors are rejected by the server. Manual/offline entry supports equipment that is not discoverable. Controllers and connections can be corrected by editing a draft; review/save shows old and new mappings.

For adapters without stable identity, rediscovery requires renewed operator confirmation. Injected providers can supply stable adapter IDs, in which case the resolver follows that identity across controller-index changes and rejects missing or ambiguous matches. It never chooses the first of several matches.

The default file is `src/BenchDouble.Host.Server/build/bench-setup.json` when started from the project directory. Override with the host configuration key `BenchSetup:Path` (environment variable `BenchSetup__Path`). The host is the sole writer. Saving uses a temporary file in the same directory and an atomic rename; revision hashes reject stale edits. This is not a multi-process file coordination service or a power-loss durability guarantee. Invalid input, cancellation of UI edits and failed validation preserve the saved document. Corrupt saved files produce an error rather than resetting to defaults.

Loaded configuration objects are detached copies. An active setup lease retains its configuration revision and selected connection snapshot even if another draft is saved. There is no automatic interface failover or retry of unanswered instrument commands. All simulator aliases use the same existing canonical host reservation authority.

## API

All endpoints are local-only and under `/api/v1/setup`. Cross-origin browser requests are rejected. The demonstration host is not a production remote authenticated service.

| Method and path | Input | Result |
|---|---|---|
| `POST /discover` | `{}` | Cached simulator inventory, controller bindings, diagnostics |
| `POST /discover-visa?scope=GPIB` | `{}` | Installed VISA GPIB catalog |
| `POST /discover-visa?scope=Ethernet&host=scope.lab` | `{}` | One-host VISA catalog |
| `GET /configuration` | None | Saved configuration envelope, or JSON `null` |
| `POST /validate` | Configuration document | Actionable problems; empty array means supported routes passed at that instant |
| `POST /configuration` | `{ configuration, expectedRevision, draft }` | Atomic save with new revision; stale edits return conflict |
| `POST /roles/voltmeter/reserve` | `{}` | Canonical resource, existing lease ID, configuration revision and selected connection |

Release a granted lease using the existing `DELETE /api/v1/reservations/{leaseId}` endpoint. Query using the existing `/api/v1/instruments/{resourceId}/query` endpoint and that lease. Setup grants are simulation-only with the currently supplied registry; they do not establish a physical safe state.

## Verification

- Host unit/integration tests cover no-serial setup, two independent buses, controller confirmation/rebinding, alternate-route ownership, busy validation, identity mismatch, strict JSON, stale saves, snapshots and offline drafts.
- `scripts/check_bench_setup_dom.cjs` runs a disposable host and exercises the shipped UI JavaScript with `happy-dom` against real HTTP endpoints. Install `happy-dom@21.0.3` into your development environment and set `HAPPY_DOM_MODULE` if needed. This is DOM/API verification, not browser rendering.
- `scripts/check_bench_setup_browser.cjs` runs a disposable host and exercises the page with Playwright, including desktop/mobile screenshots. Requires the host built in Release and `playwright@1.56.0` with Chromium installed. Set `BENCH_SETUP_DOTNET` to select the dotnet executable and optionally `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to select Chromium.
- Both scripts use a temporary configuration file. Run against an otherwise unused port 5278. The scripts do not test attached physical equipment.

See [discovery-assisted requirements](DISCOVERY-ASSISTED-SETUP.md) and issue #69 for the remaining physical-provider, native-runtime and rendered-browser acceptance work.

## Inspect an existing IVI configuration

On a Windows host, open **Review an existing IVI configuration**, select an IVI configuration XML file, and choose **Inspect IVI configuration**. The preview shows logical names, driver sessions, module declarations and hardware addresses. Expand the details to inspect all driver sessions, standard options and virtual-name mappings. Serial numbers are not required. This step leaves the bench draft, saved configuration and original XML unchanged.

The endpoint is `POST /api/v1/setup/ivi-catalog` with UTF-8 XML as its body, limited to 2 MiB. It shares the existing local-host/origin restrictions. It accepts uploaded content, never a host filesystem path. XML with DTDs/external entities is rejected. The returned revision hashes the submitted text encoded as UTF-8, after the endpoint removes a leading BOM.

The host references the official ConfigServer package through the existing optional IVI integration module; the portable BenchDouble core remains independent. ConfigServer 2.1.0 accesses the Windows registry even for configuration objects. Linux/macOS hosts therefore return an explicit HTTP 503 provider diagnostic; simulation and manual setup continue to work. No automatic vendor installation or driver assembly loading occurs.

This is a metadata preview, not schema validation, a lossless export, an applied driver binding, or hardware activation. The official `LoadWithoutValidation` API avoids requiring a separately installed schema; malformed structure or unresolved references can be rejected by the official loader. Custom data, virtual ranges and other extensions remain in the original XML. Standard settings are displayed but never applied. Keep the original XML; do not treat the catalog JSON as a replacement.

## Apply a reviewed IVI binding

After inspecting XML on a Windows host:

1. Enter a new friendly instrument ID, or an existing instrument ID if this route reaches that same physical instrument. Enter an optional experiment role.
2. Review the module, driver session and resource address. Confirm the checkbox, including the physical association when reusing an instrument ID.
3. Choose **Use binding** beside the desired logical name. This edits only the draft. A different role can reuse the same session/route without creating another instrument.
4. Review changes and save as an **untested offline draft**. Validation explicitly blocks driver activation before any device probes.

Bindings upgrade the draft to schema 2; schema 1 files remain readable. Each route carries the inspected source revision, original logical name/session, declared module, resource descriptor, standard settings and virtual-name mappings. These are retained evidence, not executable options. Additional vendor extensions remain in the original XML. GPIB primary addresses are scoped to imported controllers; other resource descriptors remain complete opaque VISA addresses. Controller identities still need discovery and confirmation.

The host retains the most recent successful catalog in memory. A changed catalog or host restart requires reinspection; saved bindings persist independently. Conflicting roles, assigning one endpoint to two instruments, and silently replacing another session's route binding are rejected. To replace a binding, remove its route in the draft and import the replacement, then review/save. Existing run leases retain their snapshots.

`POST /api/v1/setup/ivi-bindings/apply` accepts `configuration`, `catalogRevision`, `logicalName`, `instrumentId`, optional `role`, and `confirmed`. It returns the updated draft. Saving uses the existing revision-checked configuration endpoint. Source metadata is not a driver installation check; controlled registered-driver activation is described below.

## Setup logging

Setup uses `Microsoft.Extensions.Logging`; events appear in the host console and configured logging providers. Categories are `BenchDouble.Host.Server.Setup.BenchSetupService` and `BenchDouble.Host.Server.Setup.IviSetupCatalogService`. Default host logging includes information and warning events.

| Event IDs | Meaning |
|---|---|
| 7400–7402 | IVI inspection started, completed or failed |
| 7410–7411 | Reviewed binding applied to draft or rejected |
| 7420 | Discovery completed, with controller/endpoint counts |
| 7421 | Validation outcome with problem count and stable diagnostic codes |
| 7422–7423 | Save completed with revision/draft/binding counts, or failed |
| 7430 | Rejected setup HTTP request with failure category and request trace ID |

Application events omit uploaded XML, driver option values, raw descriptors and exception message payloads. These settings still appear in the reviewed configuration itself. The logs are operational diagnostics, not an authenticated, durable change-approval audit. No new log database or retention policy is implied.

## Explicit activation of a registered DMM

The host now has a typed DMM activation path. It ships with **no vendor factories**. An integrator must register an `IviDmmRegistration` in host dependency injection before `builder.Build()`, with one canonical resource ID, exact approved binding snapshots, a typed opening factory and a provider-specific recovery operation. The executable integration pattern is exercised in `tests/BenchDouble.Host.Tests/HostedIviDmmTests.cs`; its official-interface doubles are not vendor hardware.

The factory returns an `OwnedIviDmm`: an `OfficialDmmBridge` over the actual official `IIviDmm`, plus an explicit vendor-session close operation. Opening receives `Reset=false` and `IdQuery=true`. The provider must honor those policies, validate model compatibility, interpret only qualified options, and clean up partial allocations if opening throws. The host cannot infer constructor side effects from an IVI interface. XML cannot register factories, load arbitrary assemblies or turn a declaration into executable code.

In **Activate a registered IVI DMM**, enter a saved role, confirm opening, and choose **Activate saved role**. Activation requires the exact saved revision and approved binding. It qualifies only that pinned route; the saved bench may remain an offline draft, and ordinary draft reservation stays blocked. It neither validates other routes nor globally promotes the saved file. GPIB requires a current saved controller confirmation (or matching stable identifier); opaque GPIB descriptors cannot bypass it. Optional expected-identity fields require a registered `ReadIdentity` callback; missing support is rejected before opening. Manufacturer/model comparisons ignore case and surrounding whitespace; serial comparison ignores surrounding whitespace but is case sensitive. Only supplied fields are compared. A missing field, mismatch or reader failure after opening quarantines the session for explicit recovery. With no expected fields, no extra identity query is dispatched; serial remains optional.

Once active, configure the measurement function before measuring. The UI lists provider-declared functions and accepts optional positive range and resolution in base SI units before measurement and close/release. Measurement before successful configuration is refused without I/O. Registration permits DC voltage only when `SupportedFunctions` is omitted. To enable other functions, the integrator must declare them explicitly; declarations are copied and unsupported requests are rejected before I/O. This is a qualification declaration, not a live capability probe. Results use the configured function's base SI unit. NaN/infinity requires provider-specific interpretation and is treated as an operation failure in this initial JSON API.

Failures during opening, operations or close retain the canonical lease and show `RecoveryRequired` in host monitoring. Subsequent operations and ordinary reservation-release cannot bypass it. Use **Recover retained lease** only after reviewing the failure and confirming the registered provider's recovery procedure. That procedure must clean up and verify physical state before returning. Failure retains ownership; success releases it. The host does not invent a generic reset or replay operation.

| API (under `/api/v1/setup`) | Request |
|---|---|
| `POST /ivi/roles/{role}/activate` | `expectedRevision`, `confirmed` |
| `GET /ivi/{resourceId}/capabilities` | No body; returns declared function values without opening a driver |
| `POST /ivi/{resourceId}/status` | `leaseId`; returns cached resource/revision/state/last configured function, without device I/O |
| `POST /ivi/{resourceId}/configure` | `leaseId`, `function`, optional `range`, `resolution` |
| `POST /ivi/{resourceId}/measure` | `leaseId` |
| `POST /ivi/{resourceId}/recover` | `leaseId`, `confirmed` |

Activation returns `{lease, active, errorCode}`. An initialization failure returns `active=false` **with a retained lease**; keep that lease for recovery. Normal closure uses the existing `DELETE /api/v1/reservations/{leaseId}` endpoint. Driver cleanup occurs before ownership is removed. Event IDs 7450–7456 cover busy refusal, opening, readiness, operation completion, closure, failure/quarantine and recovery, with canonical resource IDs and without vendor exception payloads.

Synchronous vendor calls cannot be interrupted by request cancellation. Cancellation also interrupts waiting for another call on the same lease; once dispatched, the host retains the per-lease operation gate until the call actually returns. Other instruments can proceed. Synchronous provider execution cannot be forcibly interrupted. The host now bounds its shutdown decision wait and worker count as described below; that does not guarantee physical cleanup finishes before the deadline or complete BD-LIFE-004. Lease capabilities remain process-local; registered physical DMM ownership now persists in the host journal described below. Host restart is **not physical recovery**. Full BD-LIFE-006 acceptance, authenticated remote control and vendor-hardware qualification remain outstanding. Do not use this demonstration host as an unattended physical safety authority. The tab retains only resource ID, lease token and pinned revision in `sessionStorage`. Reload checks cached host status before enabling operations and never reopens the driver. Inspection remains available during blocked I/O. A transient status failure keeps the token and blocks commands until a successful refresh; a host-rejected token is discarded without claiming physical recovery. Tab closure or unavailable browser storage can still lose the token; this is not durable ownership recovery. The same-origin token is a control capability and is excluded from application logs. The UI suppresses overlapping instrument commands and consumes each open/recovery confirmation before dispatch. An interrupted activation response can still strand a host lease; there is no automatic open retry.


### Provider registration and diagnostic details

Register all routes for one physical instrument under one canonical ID. Registration validates bounded metadata and checks the entire batch before adding any resource; a late conflict cannot leave a partially registered batch. Descriptor ownership is checked in the shared registry, including across separately constructed provider services. Descriptor comparison ignores case; different aliases still require explicit physical association. Approved binding snapshots are copied so later caller edits do not change the allowlist. Settings remain the integrator's responsibility; approval does not certify vendor-specific behavior.

The optional `ReadIdentity(OwnedIviDmm)` callback returns `SetupIdentity`; it must obtain identity through qualified provider APIs. It is called after opening and before the session becomes ready, only when expected fields are present. It must not reset the instrument or turn on outputs. Recovery must handle both a returned driver and a null driver after partial factory failure.

| Event | Fields and purpose |
|---|---|
| 7451 | Resource, session correlation and pinned configuration revision at open |
| 7453 | Resource, correlation, operation and elapsed milliseconds at completion |
| 7455 | Resource, correlation, failing phase/operation and exception type; ownership retained |
| 7457 | Resource, correlation and operation at dispatch start |
| 7458 | Stable preflight rejection reason, with no user-provided role or options |
| 7459 | Expected identity accepted, with no identity values |
| 7460–7461 | Close/recovery started; matching 7454/7456 completion includes elapsed milliseconds |

Session correlation is deliberately distinct from the lease capability. Compare 7457 with 7453 to locate a call that has started but not completed; elapsed time is diagnostic and does not impose a deadline. Status is a cached snapshot, not a hardware health probe. These DMM states do not yet fulfill the general cross-driver lifecycle state-machine requirement.


### Measurement provenance and retained-lease resumption

The measurement response retains `value` and adds `resourceId`, `revision`, `correlationId`, `function`, `unit`, `sequence`, `completedUtc`, `requestedRange` and `requestedResolution`. Metadata is captured under the same gate as the reading, so a queued reconfiguration cannot change its meaning. Units are V for voltage, A for current and ohm for resistance. Sequence starts at one for each activation; correlation distinguishes activations with the same saved configuration. `completedUtc` is host receipt time, not the instrument's sampling timestamp. Requested settings are not verified hardware settings or accuracy claims. The C# `Measure` method still returns a double; `MeasureWithMetadata` provides the complete record.

IVI operation JSON rejects missing required properties, duplicate fields (including case variants) and unknown fields. Configuration must explicitly include `function` and `leaseId`; activation requires `expectedRevision` and `confirmed`. Recovery still requires explicit confirmation. Canceling recovery while queued prevents dispatch; cancellation after provider recovery starts cannot interrupt its cleanup or cause premature release.

Under **Retain or resume recovery details**, use **Show retained lease details** to copy the private resource/lease/revision envelope before closing a tab. On the same host in another tab, paste it, confirm inspection and choose **Resume supplied lease**. This validates the existing lease and reads cached metadata; it does not open, configure or recover the driver. An active tab lease cannot be replaced. Invalid or host-rejected details grant no access. Copied details are a control capability, not diagnostic text: keep them private. Closing or recovering clears the displayed details. This does not recover a token that was never received or retained.

Cached status also provides `lastFailurePhase`, `correlationId` and `diagnosticWriteFailures`. Canonical registry and typed IVI lifecycle log sinks are isolated from driver results: a sink exception cannot turn successful close into quarantine, strand a grant before returning its token or replay an operation. Failed writes are counted for diagnosis rather than retried through the same failing sink. Counts are process-local, include shared registry failures, and are not a durable audit. Other subsystems and provider-internal logging retain their own policies.


## Durable ownership and explicit restart reconciliation

The application creates a single-writer ownership journal at `BenchSetup:OwnershipDirectory`, defaulting to the setup filename plus `.ownership`. Keep this host-owned directory stable across restarts and deployments. Do not delete it to unblock an instrument or start another host against the same physical bench with a different directory. Its lock coordinates cooperating processes using the same directory; it is not a distributed physical-instrument lock.

Registered physical DMM grants persist an unresolved record before a lease is returned or the factory runs. Records contain canonical resource, a digest of registered routes, optional saved revision, timestamp and a review ID; they contain no lease token, raw descriptor or imported options. Cleanup removes the record only after the provider succeeds and the journal acknowledges removal. A persistence failure latches the file journal unavailable until restart; there is no in-memory fallback. `JournalUpdateRequired` means provider close succeeded but durable acknowledgment has not completed, not that the instrument should be reopened.

After an interrupted process, pending records become `RecoveryRequired`, including records whose provider is missing. Old lease tokens cannot control them. A missing or changed startup registration blocks new physical grants because the host cannot prove route separation; restore the original canonical IDs and route sets first. Matching registered resources remain independently gated during a provider's reconciliation. Simulators are not journaled; omitting the journal in direct registry construction is intended only for ephemeral tests or simulation.

Use **Inspect pending recovery** in the setup page, review the record and readiness code, then explicitly confirm reconciliation. Missing-provider and changed-route records cannot be reconciled through the page. The integrator must supply a separate `RecoverAfterRestart` callback qualified to establish reusable physical state without the prior vendor session. Ordinary `Recover` is never substituted. The stock host supplies neither vendor factories nor a restart procedure. Reconciliation does not activate a driver. A failed inspection invalidates the review, and every reconciliation attempt consumes its confirmation.

- `GET /api/v1/setup/ivi/recovery` reads pending records without I/O.
- `POST /api/v1/setup/ivi/recovery/{resourceId}/reconcile` accepts exactly `{ "recordId": "<review UUID>", "confirmed": true }`.
- Readiness codes are `ready`, `provider_missing`, `restart_provider_missing` and `routes_changed`.

The current record and route digest must still match at dispatch. Queued cancellation prevents the callback; cancellation after synchronous provider dispatch cannot release quarantine early. A provider failure retains the record. If provider success is followed by failed acknowledgment, an explicit retry in the same process does not replay the callback. After another process restart that completion is uncertain and requires a fresh qualified reconciliation.

Events 7470/7471 record ownership persistence/removal; 7472 records startup quarantine; 7473–7476 cover restart reconciliation dispatch, acknowledgment and failures; 7478 records refused acquisition after persistence failure. Record IDs are diagnostic review IDs, never control leases. Logs exclude journal paths and exception messages. Diagnostic sink failures remain isolated from ownership decisions.

The snapshot is flushed before atomic replacement. Corruption, unknown schema, duplicate fields/resources, oversized data, a second writer or a missing snapshot in an existing directory prevents startup. Keep the directory on a qualified local filesystem. The forced-process-exit probe verifies retained quarantine and explicit reconciliation only; it does not certify power-loss durability, directory fsync, network filesystems, Windows behavior, real vendor drivers or physical safe state. The probe uses a managed reservation and an inert qualified-callback stand-in, not an in-flight hardware command. See the third iteration report for commands and evidence.


## Orderly shutdown and control-owner heartbeat

The application now stops canonical admission when the host receives its stopping signal. New grants and queued commands are refused; already dispatched operations retain their lease gate. A host-owned coordinator then waits for cleanup using finite limits. Concurrent shutdown callers share one attempt and one immutable decision report. The browser has no stop-host action.

| Host setting | Default | Valid range or relationship |
|---|---:|---|
| `BenchShutdown:TimeoutMilliseconds` | 5000 | 10–60000 ms |
| `BenchShutdown:MaxConcurrency` | 4 | 1–16 workers |
| `BenchHeartbeat:IntervalMilliseconds` | 5000 | 10–60000 ms |
| `BenchHeartbeat:SuspectMilliseconds` | 15000 | At least twice the interval |
| `BenchHeartbeat:ExpireMilliseconds` | 30000 | At least twice suspect time; at most 3600000 ms |

Environment variables use double underscores, for example `BenchHeartbeat__ExpireMilliseconds`. All configured values must be unsigned decimal integers and satisfy the relationships. Invalid settings prevent startup. Heartbeat timing uses the real monotonic host clock, independently of instrument simulation and wall-clock corrections. Direct registry construction can omit heartbeat enforcement for legacy tests/simulation; the application installs it by default.

Live DMM shutdown requires the integrator's explicitly registered `IviDmmRegistration.PrepareForShutdown` callback. It must perform the device-specific abort/disable and safe-state confirmation; only successful return allows the normal close callback. Faulted sessions require explicit recovery rather than inferred automatic recovery. The stock host still bundles no physical provider or qualified safe-state procedure.

The coordinator reports `Closed`, `ProviderMissing`, `RecoveryRequired`, `DeadlineExceeded` or `StartupQuarantine` for each captured owner. A deadline never implies safe hardware or clears unresolved evidence. Workers queued beyond the deadline never start device work. Already dispatched synchronous work can finish later; `pendingCleanup` reflects that separately, without rewriting the original report. If late cleanup completes while persistence remains available, it may clear its journal record; otherwise the record remains for restart reconciliation. A bounded worker pool means not every resource can start cleanup when all workers are stuck. The coordinator assumes responsive host synchronization, filesystem and logging infrastructure; it is not a hard real-time or power-loss safety mechanism.

The setup page's **Host shutdown status** panel and `GET /api/v1/setup/host-lifecycle` expose `acceptingWork`, `pendingCleanup` and the cached `shutdown` report. Failed inspection removes the old display, and late responses cannot overwrite newer inspection. Host health returns HTTP 503 while stopping; mutation requests are refused. Existing simulation-only oscilloscope infrastructure is separate and is not qualified by the DMM cleanup tests.

Each canonical reservation in the application receives an owner generation within a random host process epoch. The client establishes a fixed peer epoch and sends an increasing heartbeat sequence. These messages use the existing lease capability and same-origin loopback API; they are not a replacement for remote authentication, TLS or deployment hardening.

- `POST /api/v1/setup/ivi/{resourceId}/owner-health` accepts `{ "leaseId": "..." }` and inspects without renewal or device I/O.
- `POST /api/v1/setup/ivi/{resourceId}/heartbeat` requires `leaseId`, `hostEpoch`, `ownerGeneration`, `peerEpoch` and `sequence`. Unknown, duplicate or missing JSON fields are rejected.
- Acknowledgments identify the same host epoch, generation, peer and accepted sequence. Duplicate/out-of-order sequences do not extend the deadline; changed epochs or generations are rejected.

The supported loss policy is **FenceAndRetain**. Silence first makes ownership `Suspect`. At expiry it permanently fences that lease; late renewal cannot revive it. The registry checks before queuing and immediately before dispatch. It retains in-flight operations and the durable ownership record, refuses generic release, and does not automatically recover, replay commands or reassign the instrument. Explicit confirmed provider recovery can reconcile an expired DMM session before a new activation obtains a new generation. Orderly shutdown may separately invoke a registered qualified shutdown callback. No heartbeat means no evidence of communication, not proof that outputs are disabled.

Passive monitor/status requests and other experiments' heartbeats never renew this owner. Device I/O cannot hold the heartbeat behind its vendor-call gate. Status separates `Heartbeat` from device `State`, `LastDeviceSuccessUtc`, `LastReadingUtc` and monotonic `ReadingAgeMilliseconds`. Healthy communication can coexist with a failed device or old data. UTC fields are host observation times, not hardware timestamps. Reading age is available for operator judgment; per-experiment freshness thresholds and a full stale-data dashboard remain future acceptance work.

The setup tab starts independent renewal after inspecting a heartbeat-enabled retained session. It uses the inspected peer/sequence on reload or explicit lease resumption, rejects mismatched acknowledgments and stops on terminal ownership rejection. It preserves the private token for explicit recovery, never reopens a driver, and stops its timer after closure or page departure. Browser suspension, background throttling, computer sleep or network loss can still expire the lease. A resumed tab must inspect current host state; healthy status never revives an already expired owner. Two tabs sharing one capability are not independent owners and can race sequences; use one controlling tab per lease.

For .NET clients, `BenchDouble.Host.Client.OwnerLeaseHeartbeat` accepts a current `OwnerHeartbeatSnapshot`, canonical resource and lease token. `Start()` begins immediate then periodic renewal; `RenewAsync()` permits a serialized explicit renewal. Acknowledgments are checked, requests have finite cancellation budgets, and observer exceptions are isolated. `DisposeAsync()` stops renewal without releasing the lease or disposing the caller-owned `HttpClient`. HTTP transports must honor cancellation. The older `ScopeLeaseHeartbeat` remains specific to the separate scope coordinator.

Events 7480–7484 cover admission stop, shutdown start, worker outcomes, bounded decision and deadline uncertainty. Events 7462/7463 cover qualified DMM preparation start/completion; provider failure uses the existing sanitized lifecycle fault event. Events 7490/7491 cover suspect/expired owner transitions, 7492 restored pre-expiry health and 7493 the first accepted heartbeat. These events exclude lease capabilities, raw driver messages, command payloads and measurements; registry/lifecycle sink exceptions do not change control decisions.

These features are locally tested foundations. They do not guarantee hardware safe state after process/power loss, implement independent hardware watchdogs, qualify Windows/vendor drivers, or complete authenticated remote control. See iteration reports D/E for evidence and remaining boundaries.

### Heartbeat clock and configuration validation

Only the three documented scalar `BenchHeartbeat` settings are accepted. Unknown
keys and nested values fail startup. A heartbeat-enabled injected provider must
have a positive timestamp frequency. Backwards movement relative to either the
last accepted renewal or last owner observation permanently fences that retained
owner; returning the clock to a later value does not restore the lease.

Owner state and reported age come from one monotonic observation. Retained expired
snapshots keep their last coherent observed age. Valid renewals use the same
validated observation and report age zero. Observing health never renews ownership,
queries a device or changes physical safety state. A failed clock read or generation
overflow during grant preparation leaves no partial reservation. Explicit qualified
recovery remains required after fencing.
