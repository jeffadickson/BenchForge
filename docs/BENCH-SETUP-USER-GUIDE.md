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

`POST /api/v1/setup/ivi-bindings/apply` accepts `configuration`, `catalogRevision`, `logicalName`, `instrumentId`, optional `role`, and `confirmed`. It returns the updated draft. Saving uses the existing revision-checked configuration endpoint. Source metadata is not a driver installation check; physical activation remains outstanding.

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
