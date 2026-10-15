# Instrument host repository layout

## Current physical projects
- `src/BenchDouble` — existing instrument-control, IVI-inspired, simulation/mocking and experiment prototype (unchanged for source compatibility).
- `src/BenchDouble.Ivi.Official` — optional real IVI Foundation assembly boundary.
- `src/BenchDouble.Host.Contracts` — transport-neutral network DTOs for discovery, reservations and commands.
- `src/BenchDouble.Host.Server` — **loopback-only development server**, with two simulated resources and exclusive in-memory reservations.
- `src/BenchDouble.Host.Client` — typed asynchronous HTTP development client for that server.
- `tests/BenchDouble.Tests` and `tests/BenchDouble.Ivi.Official.Tests` — current unit/adapter tests.
- `docs/` — normative architecture, protocol and requirements.

## Running the local demonstration
```sh
dotnet run --project src/BenchDouble.Host.Server
curl http://127.0.0.1:5278/api/v1/instruments
```
Reserve by POSTing `{"resourceId":"DMM1"}` to `/api/v1/reservations`, then pass the returned `leaseId` to the query endpoint. Release through DELETE on the reservation URL.

## Explicit limitations
The server deliberately **only binds to loopback**. It must not be exposed on the network: it lacks TLS, authentication, authorization, lease expiry, fencing tokens, controlled SCPI access, safe shutdown and log streaming. The GUID lease is not a security boundary. The host uses simulation only; it does not open actual VISA/USB/GPIB equipment. The demonstration serializes all resources with a single internal lock; the production host requires per-resource actors/locks. It is a scaffolding vertical slice, not a network-ready instrument host.

## Target projects (to be introduced when functional)
- `BenchDouble.Host.Grpc` — authenticated gRPC services and streaming.
- `BenchDouble.Host.Visa` — optional native physical resource provider.
- `BenchDouble.Host.Web` — authenticated host dashboard with local live logs.
- `BenchDouble.Host.Cli` — headless host administration and log tail.
- `BenchDouble.Host.Testing` — contract tests for contention, expiry, reconnection and authorization.

## Requirements before remote use
TLS, authenticated callers, RBAC, server-side expiring/fenced leases, safe-state recovery, resource aliases, per-resource queues, structured event streaming and retention, host restart invalidation, platform/provider compatibility verification, and negative security tests. See `DISTRIBUTED-INSTRUMENT-HOST.md`.
