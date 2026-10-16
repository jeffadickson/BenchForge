# IVI configuration architecture review

Reviewed 2026-10-16 against BenchForge commit `832854f08f0e2ddf117063d301315a9d8368a08a`.
This is a design review, not an implemented importer or a conformance claim.
Existing work items: #46 / TR-037 (IVI integration), #69 / BD-CFG-008..012 (guided setup).

## Recommendation

Keep BenchForge's guided setup and canonical ownership. Add an optional IVI configuration adapter and use official driver contracts and factories where supported. Do not build another IVI Configuration Server or make the machine-wide IVI store mandatory for portable simulations.

## What IVI supplies

IVI-3.5 sections 2.4–2.6 distinguish logical names, configured driver sessions, software modules and hardware assets. A logical name resolves to a session; the session references a driver module and hardware asset, plus initialization settings. The hardware asset supplies an I/O address, commonly a VISA resource descriptor. It is not proof of physical presence. The standard model does not require an operator-entered serial number. [1]

| IVI concept | BenchForge mapping and decision |
|---|---|
| Logical name | Import as a candidate role binding; retain its source name |
| Driver session | Add an explicit driver binding; not equivalent to a BenchForge lease |
| Software module | Installed driver implementation and declared API; separate from transport provider |
| Hardware asset / I/O descriptor | Candidate connection route; not automatically a canonical physical instrument |
| Virtual names | Channel/repeated-capability mappings; preserve rather than assuming integer channel numbering |
| Configuration store | Optional external configuration source with provenance; keep run snapshots immutable |

The Configurable Settings shared components support configuration-based .NET driver creation. Their documentation requires attention to driver registration, additional installers and explicit assembly availability under modern .NET. A NuGet dependency alone does not establish a usable installed driver. Generation 2026 Core support and Configurable Settings support are distinct. [2]

Core specifies simulation, session thread safety and initialization controls including reset. Those do not establish BenchForge's physical alias ownership, cancellation completion, or safe recovery guarantees. Driver simulation also does not establish agreement with a real instrument. [3]

## Existing source and concrete gaps

`BenchDouble.Ivi.Official` already references official Driver/Dmm/Fgen 2.1.0 packages and DriverCore 1.0.1. `OfficialDmmBridge` and `OfficialFgenBridge` accept typed official interfaces and caller-supplied operations. They retain caller-owned lifetime. They do not load drivers or resolve configuration-store entries. Referencing DriverCore does not itself implement its contract.

The setup schema currently contains transport providers, controllers, routes and roles, but no driver binding, IVI session reference, initialization policy or import provenance. `HostResourceRegistry` in `Program.cs` registers two SCPI simulators. Its SCPI query path cannot generically dispatch an official typed IVI driver. Native VISA discovery enumerates addresses only. Therefore physical activation needs both driver resolution and a typed host execution boundary; an importer alone cannot complete it.

The existing integration documents predate some implemented bridges. Their status is clarified alongside this review. No runtime behavior changes in this review.

## Multiple interfaces and dongles

For an illustrative bench, `GPIB0::22::INSTR` and `GPIB1::22::INSTR` are different controller-scoped routes. A third Ethernet route can be associated with one of those instruments only after operator confirmation. Importing IVI names must preserve that distinction; matching names, models or optional serials must not merge resources.

The proposed model is: experiment role → canonical instrument → selected route plus driver binding. IVI configuration contributes the route and driver binding. BenchForge retains the physical identity and reservation. Multiple IVI sessions can consequently refer to one BenchForge instrument without granting multiple control rights.

Provider controller indices remain routing information, not durable dongle identity. Reuse vendor configuration tools to establish controller mappings, capture stable identifiers when available, and otherwise require the existing explicit confirmation/repair workflow. IVI names do not eliminate that ambiguity.

## Implementation sequence and acceptance gates

These are proposed integration tasks under the existing work items, not completed behavior:

1. **Read-only configuration catalog.** Use the official configuration API behind an optional provider. List logical names, referenced sessions, modules, descriptors and settings without constructing drivers or issuing I/O. Test missing store/runtime, broken references, two independent GPIB buses and absent serials. Import must leave the external store untouched.
2. **Reviewed binding import.** Add a versioned driver-binding/provenance model, separate from `SetupConnection.Provider`. Preview source store revision, route, driver and effective options. Preserve unsupported settings visibly or reject activation. Confirm physical associations and pin resolved configuration for each run. Later external edits must not retarget an active run.
3. **Controlled activation.** Under the existing canonical reservation, resolve an explicitly registered driver implementation and verify its actual supported API and capabilities. Never instantiate every discovered driver as a probe. Inspect factory/constructor side effects: `reset=false` alone is not proof of non-mutating initialization. Setup validation must refuse drivers without an established non-mutating path. Do not silently apply imported initialization settings.
4. **Typed operations and lifetime.** Reuse the official bridges where suitable, with explicit channel/capability mapping and one host-owned lifetime. Serialize operations appropriately. An async wrapper must not release ownership merely because a caller cancelled while synchronous vendor I/O continues. Failed initialization/disposal must follow existing recovery rules; no automatic route switching or replay.
5. **Deployment evidence.** Validate a real .NET vendor driver on its supported OS/architecture and runtime; treat C/COM support as separate adapters. Test two dongles and Ethernet/GPIB alias contention physically. Record driver/runtime versions and effective options. Check package and vendor redistribution terms before bundling. Keep unsupported platforms usable with simulation and clear diagnostics.

Use official class APIs where actually implemented; Core compatibility alone must not imply DMM/FGEN interchangeability. Require the measurement ranges, triggers, channels and extensions needed by an experiment. Generated selected-subset drivers remain BenchForge drivers unless independently shown to meet IVI conformance requirements.

## Primary sources and review limits

1. [IVI-3.5 Configuration Server, 2022-12-19](https://www.ivifoundation.org/downloads/Architecture%20Specifications/IVI-3.5_ConfigurationServer_2022-12-19.pdf), particularly sections 2.4–2.9. Downloaded and inspected as text.
2. [IVI .NET Drivers with Configurable Settings Shared Components](https://github.com/IviFoundation/IviDrivers/blob/main/Documentation/IviDotNetDriversWithCSSharedComponents.md).
3. [IVI Driver Core 1.0 specification](https://github.com/IviFoundation/IviDrivers/blob/main/IviDriverCore/1.0/Spec/IviDriverCore.md), including initialization, simulation, multithread safety and testing.
4. [IVI instrument classes](https://www.ivifoundation.org/About-IVI/Instrument-Classes.html) and [DriverCore 1.0.1 package](https://www.nuget.org/packages/IviFoundation.DriverCore/).

This review inspected specifications and local integration source. It did not run an IVI Configuration Server, instantiate a vendor driver, establish package licensing compatibility, or test physical hardware. Links to upstream main are moving references; pin upstream versions before implementation acceptance.
