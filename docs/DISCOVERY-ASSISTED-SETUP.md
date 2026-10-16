# Discovery-assisted bench configuration

Status: requirements and design only. Tracked by [issue #69](https://github.com/jeffadickson/BenchForge/issues/69), BD-CFG-008..012, and the existing ownership/snapshot requirements BD-CFG-004/005 and TR-031/032. The current client role-to-host configuration remains unchanged; this document does not introduce an accepted physical-host file schema.

## User goal

Help the human set up and maintain a bench with minimal transcription. Serial numbers are optional. The normal experience should be selecting discovered equipment, giving it useful names and assigning experiment roles. A single-instrument setup should not require the user to understand controller indices, VISA resource syntax, canonicalization or leases.

Multiple simultaneous experiments make shared ownership essential. Discovery and clear configuration are useful even for one experiment. Keep the simple workflow short and expose controller/alternate-route details when the bench needs them.

## Workflow

1. **Discover:** select the host and supported adapters/network scope. Show endpoints grouped by controller, available identity information, connection status and missing provider diagnostics. Allow cancellation and manual/offline entry. Read cached data for busy instruments; discovery must not query through another experiment's reservation.
2. **Identify and name:** let the operator name the instrument or dongle and confirm which physical item it is. Capture manufacturer/model/serial automatically when available. Provide provider-supported identification aids and guided cable/reconnect checks when idle; do not require an identify feature that the hardware lacks. A serial number is evidence, not a prerequisite.
3. **Associate connections:** suggest possible alternate routes with the evidence and uncertainty visible. The operator confirms whether Ethernet and GPIB endpoints refer to the same instrument. Identical models, missing serials and duplicate serials never cause an automatic merge. The operator may revise the draft association.
4. **Assign roles and select routes:** map a role such as voltmeter to a friendly instrument name and select its connection. Distinguish a controller name from an instrument's bus address. Preserve the canonical instrument identity across alternate connections.
5. **Validate:** show missing controllers, ambiguous bindings, unreachable endpoints, optional expected-identity mismatches and ownership conflicts with actionable fixes. Obtain the normal reservation before device validation; do not reset the device or enable outputs. Show offline or untested mappings honestly.
6. **Review and save:** present the resulting instrument/controller/route/role mapping and any changes to the saved configuration. Save a versioned editable configuration; preserve the old version if validation or editing is cancelled. Permit explicit offline drafts. Active runs retain their existing immutable configuration and selected route.

## Identity and controllers

| Item | Required from the operator | Discovery assistance |
|---|---|---|
| Instrument | Select or manually enter an endpoint and confirm its friendly identity | Model, manufacturer, serial when available, connection diagnostics |
| USB-GPIB controller | Select/name the adapter; confirm its binding when ambiguous | Provider, current controller index and stable hardware identifier when exposed |
| Alternate connection | Confirm that two endpoints belong to one physical instrument | Suggest candidates and show supporting evidence without merging automatically |
| Experiment role | Select the instrument and desired connection | Offer saved mappings and show availability |

Expected serial checks are optional and explicitly enabled. Lack of a serial must not block normal use. If a controller lacks a stable identifier, retain a documented operator-confirmed binding and request repair when it becomes ambiguous; never silently select the first adapter. A previously validated endpoint is not proof that the same hardware remains connected after rewiring.

Address 22 on two independent GPIB buses is valid. Show, for example, `Rack A / address 22` and `Rack B / address 22` to the operator; translate those bindings into provider resource addresses internally. Multiple adapters attached to the same physical GPIB bus are not two independent buses; that topology needs explicit controller/provider support and is outside the initial independent-bus workflow.

## Shared ownership and changes

The setup workflow feeds the existing host-owned canonical registry. It must not create a new lease authority. Confirmed Ethernet and GPIB aliases of one physical instrument share one reservation. Distinct instruments can be reserved independently, subject to controller-level transaction coordination. Multiple roles are names for instruments, not additional control rights.

One authoritative host owns a physical instrument. Cross-host reachability does not create a second owner. Detectable conflicting ownership/configuration must be reported; the initial setup cannot promise to infer every physical equivalence from network discovery.

Reconnect/re-enumeration should either resolve a stable controller binding or offer an explicit old/new mapping repair. Configuration changes take effect for subsequent runs. Connection failure during a run must not cause automatic interface switching or command replay: an unanswered command may already have executed. Reuse the existing recovery requirements for that decision.

## Acceptance scenarios

| Scenario | Required outcome |
|---|---|
| One Ethernet instrument with no serial | Select, name, validate and save without typing VISA syntax or a serial |
| Two USB-GPIB dongles, each with address 22 | Separate controller-scoped endpoints and independent instrument mappings |
| Two same-model instruments, missing or duplicate serials | Remain separate until an operator explicitly confirms an association |
| One instrument with Ethernet and GPIB | Two selectable routes, one canonical identity and one reservation |
| Two experiments select different aliases of that instrument | Second conflicting controller is rejected by the existing lease authority |
| Dongles unplugged or re-enumerated | Stable binding retained when verifiable; otherwise repair required without silent substitution |
| Instrument currently reserved | Cached discovery information and busy status; no unauthorized probing |
| Offline/manual configuration | Explicit untested draft with versioned export/reload and later validation |
| Cancelled/invalid configuration edit | Previous saved mapping and active run snapshots remain unchanged |

These are future executable acceptance targets. Requirements consistency tests do not establish device discovery, usability or hardware correctness.
