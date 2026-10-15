# Oscilloscope host lease lifecycle

The local-only simulated scope host now supports expiring and renewable reservation GUIDs. By default a lease lasts two minutes. The host rejects an expired lease for new acquisitions; a matching valid lease can be renewed with:

```sh
curl -X POST http://127.0.0.1:5278/api/v1/scopes/SCOPE1/reserve/REPLACE_WITH_LEASE_GUID/renew
```

Renewal returns HTTP 204 on success or 403 for an invalid or expired lease. An idle expired lease can be replaced; an in-flight acquisition prevents release or reassignment until it finishes. `HostedOscilloscopeRegistry.Generation` exposes a monotonic **in-memory** counter per resource for tests and diagnostics; it is not a persisted fence. Deterministic tests use an injected `TimeProvider` in `HostedOscilloscopeLeaseTests`.

## Critical production gaps

- There is no authenticated caller identity or authorization policy; a leaked lease GUID grants access on loopback.
- There is no durable lease ledger or persisted host epoch. Restart invalidates in-memory reservations but does not fence external device processes.
- Lease expiry does not interrupt an active acquisition or independently force physical safe state. No watchdog or hardware interlock is implemented.
- No distributed heartbeat, idempotent command receipts, replay protection or secure remote TLS endpoints exist.
- The clock is wall-clock `TimeProvider` and may jump; production expiry should use monotonic time for runtime decisions plus durable timestamps for recovery.
- Failed transport recovery is latched by the scope coordinator, but the HTTP service exposes no trusted recovery procedure.

This is an incremental prototype, not production-safe remote control. Follow issues #56, #57, #58 and #66. CI results remain unverified.
