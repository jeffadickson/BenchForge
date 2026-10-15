# Instrument Host Dashboard — User and Design Guide

**Status:** read-only, local-only demonstration host dashboard committed; real multi-host aggregation and production authentication remain open.

## Start the server

```bash
dotnet run --project src/BenchDouble.Host.Server/BenchDouble.Host.Server.csproj
```

Open **http://127.0.0.1:5278/dashboard/** in a browser. The host listens only on loopback by design.

## What is displayed

- Registered instrument ID, class and simulated backend.
- Host endpoint and current lease state (Idle/Reserved).
- A bounded recent event log for each instrument: reservation, release, completed query.
- Last refresh time, number of instruments and number reserved.
- Automatic refresh every three seconds and an explicit connection-error message.

The data comes from a read-only `GET /api/v1/monitor` endpoint. It uses the existing `HostResourceRegistry` in the local host process, not a separate duplicate JavaScript simulation.

The initial host has two simulated resources: `DMM1` and `FGEN1`. An existing local lease can be used to exercise the state changes. The dashboard itself does **not** reserve instruments or send commands.

## Logging and security

Activity entries intentionally exclude raw SCPI commands, measurement payloads, credentials, client identity and secrets. The event list is bounded in memory; it is not a durable audit log and resets on host restart. No personal operator contact information is displayed.

**Important:** The demonstration host's existing reservation/query API has no production-grade authentication, lease expiry, TLS or safe-state recovery. Do not expose it beyond loopback or use it with hazardous hardware. Production remote dashboards require authenticated aggregation, role-based permissions, TLS, staleness/heartbeat indicators, durable event replay and redaction.

## Future experiment integration

The experiment definition will declare instrument roles; the host resolver will map roles to authorized physical/simulated resources. The dashboard can then display both the experiment role and actual host/resource, including which run owns a lease, if authorized. That discovery/aggregation is not yet implemented.

## Verification

[Host dashboard unit tests](../tests/BenchDouble.Host.Tests/HostDashboardTests.cs) check idle/reserved/released state and sanitized log messages. Browser tests for real dashboard DOM updates, reconnect behavior and multi-host status remain planned. Compilation/CI evidence has not been reviewed.
