# Local-first instrument configuration

The normal case needs no configuration file: load the directory, resolve `DMM1`
and use the local instrument server at `http://127.0.0.1:5278/`. Both the fallback
decision and each selected route are logged through the caller's ILogger.
This routes to the local **server**, not directly to VISA. The supplied host has
two simulated resources, DMM1 and FGEN1; it does not discover physical devices.

```csharp
using BenchDouble.Host.Client;
using Microsoft.Extensions.Logging;

// logger is supplied by your application's configured logging provider.
InstrumentHostDirectory directory = InstrumentHostDirectory.Load(logger);
using var http = new HttpClient();
var meter = directory.CreateClient("DMM1", http);
var lease = await meter.ReserveAsync();
try
{
    string reading = await meter.QueryAsync(lease.LeaseId, "MEAS:VOLT:DC?");
    logger.LogInformation("Reading: {Reading}", reading);
}
finally { await meter.ReleaseAsync(lease.LeaseId); }
```

The example's lease release is suitable for the simulation host; it does not
implement device safe-state shutdown. Managed physical cleanup is tracked in #55.

## Explicit locations

Copy [instruments.example.json](../examples/instruments.example.json) to
`instruments.json` in your application's working directory and edit it. Or pass
an explicit filename to `Load(logger, "bench-A.json")`. For services use an explicit
path so the process working directory is not a hidden configuration dependency.
`baseDirectory` optionally selects the conventional-file directory.

Each role maps to a host plus a canonical host-local resource ID. With the example,
use `directory.CreateClient("voltmeter", http)` or `"remoteMeter"`. Several clients
can reuse the same caller-owned HttpClient; routing does not mutate BaseAddress.
Use separate HttpClients for hosts with different authentication policies; default
authorization headers on a shared client apply to all of its requests.
The loaded snapshot is immutable; reload explicitly between runs, not midway
through a reservation. No credentials belong in this file. Supply authentication
through the application's HTTP pipeline when a production server supports it.

Only a missing conventional file permits local defaults. An explicit missing file,
malformed JSON, unsupported version, unknown/duplicate field, undefined host or
unknown configured role is an error. Never fall back to a different instrument
after a remote connection error. Endpoints must be HTTPS origins, except loopback
HTTP; embedded credentials, URL paths, query strings and fragments are rejected.
Identifiers are case-insensitive and limited to letters/digits/underscore/dot/hyphen.

## Multiple experiments and listeners

Reservations are per instrument, not per server. Two experiments can reserve DMM1
and FGEN1 simultaneously; a second DMM1 controller is rejected. Several logical
roles may refer to the same canonical resource, which still has one reservation.
Read-only dashboards may poll the cached `/api/v1/monitor` snapshot concurrently
without taking control or adding instrument queries.

This supports multiple polling observers. Registered push subscriptions, observer
identity, expiration, authenticated experiment ownership and fenced recovery remain
tracked in #12, #46 and #56. A configuration file does not turn the loopback demo
into a secure remote server. Remote client routing is tested with a fake HTTP
transport; production remote hosting remains incomplete.

## Setup and scoreboard

- Unix: `make setup` provisions a usable SDK, StrictDoc and all project NuGet references.
- Windows: `powershell -File scripts/bootstrap.ps1` does the same.
- Browser assets: `make setup-browser` or `powershell -File scripts/bootstrap.ps1 -Browser`.
- Linux browser system libraries, where missing: after setup run
  `dotnet run --project tools/BenchDouble.BrowserSetup -- install --with-deps chromium`.
  Playwright's system-package installation may require administrator access.
- `make scoreboard` generates `build/requirements-scoreboard.html` with searchable
  requirement IDs, priorities, evidence status and GitHub issue links.
- `make requirements` validates traceability and exports the full StrictDoc report.

Python 3.10+ and curl (Unix) are setup prerequisites. Python's venv support must be
installed. Setup downloads .NET when no compatible SDK resolves from global.json.
Optional vendor VISA drivers and physical hardware are separate from portable
simulation dependencies. Python transitive locks and clean-machine Windows/macOS
validation remain #28; no claim of fully reproducible provisioning is made yet.

## Discovery-assisted bench setup

See [discovery-assisted setup requirements](DISCOVERY-ASSISTED-SETUP.md) for optional serial numbers, guided identification of multiple USB-GPIB adapters, alternate Ethernet/GPIB connections and human-reviewed configuration repair. The [guided setup page and API](BENCH-SETUP-USER-GUIDE.md) implement the configuration workflow with simulated validation and optional installed-VISA enumeration. Physical connections remain untested drafts until a host driver supports them; the existing client role-to-host file schema is unchanged.
