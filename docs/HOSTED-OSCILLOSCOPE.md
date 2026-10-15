# Host-owned oscilloscope demonstration

BenchForge's loopback-only ASP.NET Core host now includes a simulation-only scope named `SCOPE1`. All HTTP callers share a host-owned `OscilloscopeSessionCoordinator` and an exclusive reservation.

Start the host with .NET 10:

```sh
dotnet run --project src/BenchDouble.Host.Server
```

Reserve:

```sh
curl -X POST http://127.0.0.1:5278/api/v1/scopes/SCOPE1/reserve
```

The JSON response contains a `leaseId` GUID. Use that exact ID:

```sh
curl -X POST http://127.0.0.1:5278/api/v1/scopes/SCOPE1/acquire \
  -H 'Content-Type: application/json' \
  -d '{"leaseId":"REPLACE_WITH_LEASE_GUID","channel":1}'
```

The response includes acquisition ID, channel, sample count, preamble and the first 16 voltage samples. It deliberately does not return full binary waveforms. Release after the operation:

```sh
curl -X DELETE http://127.0.0.1:5278/api/v1/scopes/SCOPE1/reserve/REPLACE_WITH_LEASE_GUID
```

The host rejects missing/incorrect leases, refuses a second reservation while one is active, and will not release a lease while an acquisition is in flight. Concurrent requests sharing the same valid lease are serialized at the coordinator. Tests: `dotnet test tests/BenchDouble.Host.Tests/BenchDouble.Host.Tests.csproj`.

## Security and recovery boundaries

This is a **local prototype only**. It binds to 127.0.0.1; the GUID is a bearer capability, not authenticated identity. There is no TLS, identity-based authorization, durable lease generation, restart fencing, expiry/renewal, safe-state interlock or distributed coordination. Other local processes that directly open a physical VISA/GPIB device bypass this host entirely. No real hardware is registered by this demo. An interrupted or failed transaction latches recovery-required in the coordinator; clearing that latch requires trusted external recovery and is not exposed through the HTTP API.

Do not expose the server on a network or substitute this prototype for physical instrument safety. Production work remains tracked in issues #46, #56–#58 and #66. Source and tests are committed; passing CI is not yet verified.
