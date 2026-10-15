# Distributed Instrument Host Architecture
Status: Approved target design, not implemented. Date: 2026-10-14.

## Executive decision
The instrument reservation authority and hardware access belong in a **separate instrument-host service**, not in individual test client processes. Run one host on each computer physically connected to USB/GPIB/serial hardware, optionally on a Raspberry Pi, mini PC, laptop or workstation. A client on the network discovers hosts, reserves devices, executes operations through leases, and receives measurements and status. BenchDouble local simulators use the same client contract.

## Topology
```text
.NET test application / notebook / TestForge / experiment runner
                        |
            BenchDouble remote client
                        |
            authenticated TLS connection
                        |
             Instrument Host A (Pi)
                  |  |  |
                USB GPIB serial
                        |
             Instrument Host B (Windows)
                  |       |
              vendor IVI   USB
                        |
             Instrument Host C (Linux)
                  |        |
                TCP/IP    USB
```

A multi-host coordinator is *optional* for later fleet-level workflows. A host MUST be authoritative for every instrument it owns, including devices identified through canonical aliases. No client, central scheduler, or separate manager can override a host's exclusive reservation.

## Primary contracts

### Host operations
- Enumerate resources, identity, current availability, connected transport, driver and capabilities.
- Reserve one or more devices as an atomic lease; reject or queue conflicting requests.
- Extend lease via authenticated keepalive/renewal; release on normal completion.
- Use a lease-scoped operation endpoint for typed IVI-class actions or vetted SCPI.
- Stream results, operational events, and measurements asynchronously.
- Abort, reset, or place an instrument into an operator-approved safe state when supported.
- Obtain host health, version, capability metadata and diagnostics.

### Reservation semantics
- Reservations must be **exclusive per physical instrument**, not just per VISA session or socket.
- Resource IDs must be canonicalized at the owning host (aliases, USB serial, VISA names).
- Lease IDs are unguessable bearer-like secrets, bound to authenticated identity and scoped permissions.
- Leases have server-enforced expiration, renewal and **monotonically increasing fencing tokens** to reject stale operations after ownership changes.
- The host is the authority for lock grant and release; expiry does not establish electrical safety.
- Whole-instrument exclusivity is the default. Per-channel sharing requires model-specific proof and explicit configuration.
- Multi-instrument reservations **within one host** are atomic. Across multiple hosts use ordered acquisition plus timeouts/rollback as an initial protocol, then a coordinator with failure-aware semantics; do not claim global atomicity without a transaction protocol.
- Client cancellation, disconnected networks and crashed clients must never silently authorize another client while the previous hardware operation is still active. Use explicit state `Ready`, `Reserved`, `Busy`, `RecoveryRequired`, `Offline` and a safe-state policy.

### Safety
- Default bind to localhost until remote access is explicitly enabled and TLS identity is configured.
- Authentication/mTLS or OAuth/OIDC service tokens; RBAC by instrument/operation (read, configure, output-enable, maintenance).
- No unauthenticated arbitrary SCPI or shell command execution.
- Lease expiry triggers a configured **safe-state/recovery workflow**; different devices require different policies. Some instruments must keep running during network failure.
- A lost connection does not mean the last command was undone. Report `OutcomeUnknown` and require status reconciliation.
- Abort and output-disable must be confirmed when possible; if impossible mark device `RecoveryRequired` and block reassignment.
- Host must support explicit emergency-stop separate from routine lease closure where hardware provides it.
- Host-wide resource locking alone doesn't prevent direct local vendor applications from bypassing it. Use OS or VISA locks when supported; disclose limits.

## Communication protocol
- Prefer **ASP.NET Core gRPC** for typed bidirectional streams, cancellation propagation, protobuf versioning and .NET clients.
- Host should optionally expose an HTTP/JSON discovery/status API for web UI and diagnostic tooling.
- Distinguish transport timeout, lease failure, execution error, cancellation, and unknown physical outcome.
- Every command carries lease ID, fencing token, instrument resource ID, request ID and deadline. Idempotent reads and commands may be retried only with deduplication where guaranteed; never automatically retry non-idempotent output/trigger commands.
- Long acquisitions return operation IDs and result streams; cancellation is cooperative and must not be conflated with physical abort.
- No client side access to raw device handles.
- Per-instrument operation queue serializes writes/queries, preserving full query/response atomicity.

## Raspberry Pi / inexpensive host requirements
- Linux ARM64 runtime and native vendor backend assessment; native IVI packages and GPIB adapter libraries can be Windows- or x64-only.
- Ethernet preferred for reliable lab deployment; configurable Wi-Fi and reconnect support.
- USBTMC/USB serial/LXI/TCPIP where supported; optional linux-gpib and validated USB-GPIB adapters.
- systemd service, health endpoint, logs, upgrades, resource discovery and watchdog.
- Package container only if device passthrough and permissions work; otherwise provide native systemd install.
- Host shall run without a database and buffer bounded telemetry locally during upstream outages.

## Package plan (logical, not yet built)
- `BenchDouble.Host.Abstractions`: host transport-independent contracts
- `BenchDouble.Host.Server`: resource inventory, security, leases, operations and state
- `BenchDouble.Host.Grpc`: protobuf services and server binding
- `BenchDouble.Host.Client`: remote instrument proxy compatible with typed interfaces
- `BenchDouble.Host.Visa`: hardware provider bridge
- `BenchDouble.Host.Cli`: discovery/admin and provisioning
- `BenchDouble.Host.Web`: optional operator dashboard
- `BenchDouble.Host.Testing`: deterministic in-memory host, fault injection and conformance tests

Avoid prematurely splitting into empty packages. Keep interfaces hardware-independent and preserve the original synchronous and async C# APIs while migrating.

## Deployment modes
1. **Standalone localhost:** workstation hosts its own instruments; all clients on that host connect via localhost.
2. **LAN host:** Raspberry Pi/mini PC owns instruments and serves authorized remote clients.
3. **Fleet:** several hosts advertise inventory to a coordinator; each remains authoritative for its actual leases.
4. **Offline:** host continues safe local operations and telemetry spool when upstream is unavailable; no unsafe reassignment based on network partition.

## First vertical slice and acceptance criteria
1. Minimal ASP.NET Core gRPC host backed by simulator, two instrument names, and a remote client.
2. Two clients contend for same instrument; only one receives a lease.
3. Two different instruments can run concurrently.
4. Lease cancellation/expiry and stalled operation transition to recovery policy.
5. Caller cannot operate without a valid current lease/fencing token.
6. Remote async DMM measurement returns a result and structured trace.
7. Host process restart invalidates stale client leases.
8. Linux ARM64 packaging smoke test, plus Windows and x64 Linux.
9. Explicit permission test: unauthorized output enable fails.
10. Command replay and full instrument simulator function identically when local or remote.

## Open design choices
- Initial discovery: configured host URIs; optional mDNS later, no untrusted auto-connection.
- Credentials: dev-mode loopback only; production mutual TLS or OIDC.
- Multi-host scheduling: avoid promising transactional atomicity prematurely.
- Long-running measurements: asynchronous operation handle vs streaming RPC tradeoff.
- Vendor VISA native SDK availability on ARM64, Linux and macOS must be verified per adapter.

## Relationship to current code
The existing `InstrumentReservationManager` is an **in-process prototype**, not a sufficient distributed reservation authority. Keep it for simulation/tests and potentially host-internal locking; remote clients must not trust it as a distributed lock. `SerializedAsyncScpiSession` remains useful on the owning host. The experiment runner must obtain leases before opening drivers, hold them through safe cleanup, and release via the remote host only afterward.

## Non-goals for first iteration
No automatic bypass of local hardware locks, no cross-host atomic transaction claim, no open network listener without authentication, no arbitrary SCPI over unauthenticated endpoints, no assumption that a Raspberry Pi runs proprietary Windows-only IVI drivers.
