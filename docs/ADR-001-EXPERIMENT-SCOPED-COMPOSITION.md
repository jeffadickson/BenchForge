# ADR-001 — Experiment-Scoped Composition and Mock Collaboration

**Status:** Proposed for external architectural review (2026-10-15). **No external expert has reviewed or approved this ADR.**

## Context
BenchDouble supports typed instrument APIs, local/remote physical devices, simulated behaviors, shared virtual time, deterministic thermal/DUT models, and a typed virtual signal graph. The current graph API risks exposing more wiring mechanics than ordinary C# engineers need. An experiment-scoped composition root can assemble instruments and injected mock behaviors while keeping normal application code strongly typed.

## Recommendation
**Adopt experiment definitions and experiment runs as separate concepts.** A definition is an immutable, versioned description of instrument roles, desired capabilities, host selection constraints, simulation behaviors, run parameters and procedures. A run resolves hosts and instrument capabilities, acquires/fences physical leases, creates a run-scoped DI container and simulation clock, composes behaviors, verifies dependency graph/lifecycle, executes, persists provenance and disposes resources.

**DI is composition, not the simulation engine.** Use Microsoft.Extensions.DependencyInjection and keyed registrations at the composition boundary; constructor-inject small interfaces into behaviors. Do not let mock classes reach into IServiceProvider or a global dictionary. Do not use DI to bypass physical resource leases.

**Keep the virtual signal graph** as an optional internal/advanced mechanism for dynamic wiring, shared coherent snapshots, typed conversions and cycle detection. Most custom mock behaviors should be plain C# strategies receiving typed model interfaces. Do not require an engineer to register every instrument property in a graph.

## Boundaries
1. Physical instrument interface: only actual supported operations; not arbitrary mock internals.
2. Simulation behavior interface: injected strategy, lifecycle, virtual time, deterministic model state.
3. Host registry: discovery, authentication, capability negotiation and physical lease/fencing.
4. Experiment run: creates per-run scope, resolves roles and orchestrates initialization/cleanup.
5. Virtual bench: deterministic time-indexed physical coupling and snapshots, no hidden network calls.
6. Telemetry: immutable observation stream (Channels/Dataflow) and durable SQLite/optional TimescaleDB, not the source of simulated physics.
7. Code packages: Roslyn compilation and isolated worker approval separate from composition.

## External expert guidance (researched, not personal consultation)
- Mark Seemann's Composition Root pattern places assembly near application entry and keeps containers out of domain code: https://blog.ploeh.dk/2011/07/28/CompositionRoot/
- Mark Seemann interview emphasizes composition and lifetime management over using the DI container as a service dictionary: https://www.infoq.com/articles/DI-Mark-Seemann/
- Microsoft .NET DI guidance recommends explicit dependencies, scoped lifetimes and avoiding service locator/captive dependencies: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines
- .NET 8 keyed services enable named instruments without a custom service locator: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection

## Review questions
- Is one per-run scope sufficient, or should a run create distinct instrument/session/worker scopes?
- How are multiple instruments implementing the same interface resolved without hidden service locator calls?
- How does a behavior read another mock's state at a consistent virtual timestamp?
- How do we reject dependency cycles, handle feedback loops and ensure reproducible replay?
- How do local and remote host failures affect initialization, leases, cancellation and cleanup?
- How can a custom behavior be unit-tested before host deployment and safely isolated?
- Can a non-container implementation be substituted without modifying domain types?
- What happens when two experiments share a physical sensor but not mutable simulation state?

## Prototype acceptance
1. Thermal chamber + filtered sensor + supply compose with ordinary constructor injection and one virtual clock.
2. Named instruments can be selected using keyed DI or typed experiment role handles.
3. Test same experiment with normal resistor, temperature-dependent IV model and timed fault without changing the runner.
4. Verify two simultaneous runs have isolated clocks and behaviors.
5. Verify physical resource lease ownership remains host-controlled.
6. Verify graph cycles fail before execution, and snapshots remain coherent across multiple consumers.
7. Verify simulated behavior never performs an unbounded synchronous remote instrument read.
8. Verify assembly hash, source version and injected behavior selection are persisted with the run.

## Naming review
Proposed brand **Framework.Test** is understandable but very generic, may read as a .NET test project/namespace rather than a laboratory instrument-control platform, and a project named Framework.Test already exists in WorkplaceX/Framework. A quick web search is not trademark, package, or domain clearance. Keep repository name BenchDouble provisionally; make naming a separate decision after ecosystem/availability checks.

## Status
Architecture recommendation only; no experiment-scoped composition root was implemented by this ADR. External human review is requested but not performed.
