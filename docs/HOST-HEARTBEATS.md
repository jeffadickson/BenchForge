# Instrument-host heartbeats and lease renewal

BenchForge now provides `ScopeLeaseHeartbeat` in the host-client project. It automatically POSTs to the host's scope lease renewal endpoint at a configurable cadence (20 seconds by default). The host's default lease duration is two minutes. This is a local simulated instrument demonstration; do not infer physical instrument readiness from a successful HTTP renewal.

## Example

```csharp
using BenchDouble.Host.Client;

using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5278/") };
var leaseId = Guid.Parse("REPLACE_WITH_ACTUAL_LEASE_ID");
await using var heartbeat = new ScopeLeaseHeartbeat(http, "SCOPE1", leaseId);
heartbeat.StatusChanged += status => Console.WriteLine($"Lease heartbeat: {status}");
heartbeat.Start();
// Execute the authorized simulated experiment here.
// DisposeAsync stops renewals; release the reservation separately.
```

A successful renewal reports `Healthy`. Network or transient server errors report `Degraded`; a 403/404 reports `Lost` and stops renewals. A local client must stop dispatching new commands when ownership is uncertain. This helper does not automatically release the lease or terminate instrument output.

## Remaining P0 work

- Replace wall-clock expiry decisions with monotonic host time and durable host epoch/generation fencing.
- Bind leases to authenticated caller identity, with TLS for remote transport and explicit permissions.
- Distinguish peer heartbeat, host process liveness, device responsiveness and actual physical safe state.
- Add bounded degraded-to-lost policy based on last successful renewal and lease deadline; a network partition can prevent receipt of the host's rejection.
- Prevent unbounded in-flight operations and enforce safe host restart and command reconciliation.
- Add persisted command ledger, idempotency, and operator-approved recovery.
- Verify with fault injection: one-way partition, client crash, host crash, dropped renewal, slow device, clock jumps and concurrent clients.
- Require independent hardware watchdog/interlocks for equipment whose unsafe state cannot be tolerated after process loss.

Related issues: #56–#58 and #66. Source and tests are committed, but successful cross-platform CI is not established.
