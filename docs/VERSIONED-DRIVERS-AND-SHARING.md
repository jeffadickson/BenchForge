# Versioned Wire Protocol, Driver Packages, and Sharing Policies

**Status:** architectural decision / implementation requirements, 2026-10-14.

## Motivation
Past Java object serialization bound client and server to compatible class versions; XML messages were more tolerant. BenchDouble SHALL NOT serialize arbitrary .NET runtime object graphs across the network. Independent client and instrument-host versions must interoperate through schema-versioned messages and explicit capabilities.

## Wire format
- Primary instrument protocol: gRPC + Protocol Buffers with stable numeric field identifiers, add-only compatible schema evolution, `reserved` field numbers/names for deletions, and versioned packages for incompatible operations.
- Browser and administrative API: JSON with optional field handling and documented semantics. ProtoJSON does **not** provide identical unknown-field guarantees to binary protobuf.
- Protocol negotiation: report host protocol version range, class versions, instrument feature keys, backends, and driver package hashes; negotiate behavior, not only DTO parsing.
- No direct BinaryFormatter, arbitrary .NET object serialization, reflection-based deserialization of client types, or dependence on identical CLR assembly versions.
- Compatibility matrix must include old client/new host, new client/old host, unknown fields, unsupported operations, enum growth, and behavior changes.
- Include stable request IDs, deadline, instrument identity, lease generation/fencing, tracing context and explicit outcome status.

## Driver package mobility (optional, security-gated)
The client may request a driver by immutable content digest, but **never execute arbitrary client-supplied code inside the host service**.
1. Client declares package ID, version, architecture/runtime prerequisites, manifest and SHA-256 package digest.
2. Host checks installed verified packages; matching hash reuses cached version.
3. If absent, host may accept upload into quarantine *only if host policy allows*.
4. Verify trusted publisher signature, hashes, declared permissions, dependencies and compatibility. Administrator/policy approves deployment.
5. Run each driver in a separate restricted OS process/container/VM with least-privilege hardware access, resource quotas, timeouts and controlled RPC. `AssemblyLoadContext` permits dependency isolation but is **not a security boundary**.
6. Multiple driver versions may reside on a host; the lease manager still arbitrates by **physical resource**, not by driver code hash. Loading two driver versions must never create two independently usable handles to the same equipment.
7. A driver process must be revocable/killable. If it crashes during an output operation, mark hardware outcome unknown and invoke instrument-specific recovery.
8. Record package SHA-256 and driver version in run results/logs so experiments are reproducible.
9. Host-owned trusted standard drivers remain default. Dynamic client-supplied packages are a later optional capability, not needed to achieve version-tolerant clients.

## Sharing policies
Instrument-level access policies must be explicit and selected from supported, verified device semantics:
- `ExclusiveControl`: one client may configure/query/control; default for general SCPI/IVI instruments.
- `SharedReadOnly`: multiple authorized clients may read safe state without changing device configuration; only permit operations proven side-effect free.
- `ExclusiveControlWithObservers`: one controller, arbitrarily many authorized subscribers to host-produced measurement and log event streams.
- `HostSampledPublishSubscribe`: host owns periodic sampling and fan-outs temperature/humidity/slow environmental values. Subscribers do not independently poll the physical bus.
- Optional independent-channel reservations only for vetted hardware/drivers with truly isolated channels; whole-device reservations otherwise.

Policies are advertised in capability metadata but *enforced on the host*. Clients cannot override policies. Writes or configuration changes demand exclusive control. An observer cannot promote to controller without lease transition and reauthorization.

## Cache and freshness
For shared sensors, describe sample timestamps, freshness max-age, sample cadence, device status and quality. Offer stream subscription and latest-sample queries. A client can request faster cadence only if host policy permits and resource loading remains bounded.

## Failure/reconnect semantics
Leases have server-side expiry and fencing tokens. Disconnect must not imply instrument stopped. Driver crashes, loss of lease, failed abort and ambiguous SCPI execution return `OutcomeUnknown`/`RecoveryRequired`. Host preserves durable event IDs and diagnostic logs.

## Required tests
- Unknown/new protobuf fields and independently versioned client/host assemblies.
- Breaking field-number and operation-version guardrails in CI.
- Equal driver hash cache reuse, mismatched hash rejection, unsigned package refusal, permission isolation and version coexistence.
- Two read-only subscribers to temperature stream receive same host sample ID; both cannot set parameters.
- One controller plus observers; observer can't issue commands; controller retains exclusive access.
- Two driver versions cannot simultaneously control same physical resource.
- Slow subscribers and disconnects cannot block hardware poll loops.
- Host crashes and driver crashes invalidate stale leases and require recovery.
- Per-device policy restrictions are enforced even when client lies about capabilities.

## Existing implementation and gaps
Current `Host.Contracts` uses simple .NET DTOs and HTTP/JSON as a **loopback-only prototype**, not a published versioned gRPC schema. Current `Host.Server` has only in-memory exclusive leases and query endpoint; no authentication, no durable log stream, no access-mode policies, no driver loading, no isolation. This document governs the next development phase and does not claim implementation.

## Authoritative technical references
- https://learn.microsoft.com/en-us/aspnet/core/grpc/versioning
- https://protobuf.dev/overview/
- https://protobuf.dev/programming-guides/json/
- https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support
