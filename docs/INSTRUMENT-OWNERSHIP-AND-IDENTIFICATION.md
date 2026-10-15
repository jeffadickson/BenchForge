# Instrument ownership and identification

## Required behavior

The host owns physical sessions and enforces exclusive control by default. Native
VISA/device locking is a separate capability and defense against other software;
absence of native locks does not make a resource shareable. A host lease alone
cannot prevent access from software that bypasses the host.

The instrument definition must eventually describe:

| Metadata | Purpose |
| --- | --- |
| Access policy | Exclusive by default; explicitly reviewed observer/subscription modes |
| Lock scope | Whole instrument by default; independent channels only with evidence |
| Shared resources | Chassis, trigger engine, output state, buffers and coupled channels |
| Operation effects | Changes configuration, triggers acquisition, consumes/clears data, affects output |
| Native lock support | Unsupported, supported or unknown, with provider-specific implementation |
| Identification | Supported query or driver identity provider; explicitly unsupported or unknown otherwise |
| Expected identity | Optional manufacturer/model/serial constraints to detect a wrong instrument |
| Lifecycle | Timeouts, reconnect behavior, shutdown/abort and recovery requirements |

A query is not automatically safe to share. Even reading an error queue consumes
state. Serialize each transport transaction and hold an experiment reservation
across configure/trigger/measure sequences. Observers consume host-published
cached samples and identity without issuing their own device queries.

## Open and reconnect requirements

1. Resolve canonical resource identity and acquire host ownership before I/O.
2. Open the transport with a bounded timeout and use a native lock when supported.
3. Automatically obtain identification through the declared driver capability.
   Do not blindly send `*IDN?` to legacy or unknown protocols; do not reset devices
   merely to identify them.
4. Log resource ID, backend, identification status, returned identity and UTC time.
   Parse manufacturer/model/serial/firmware only when the response format supports
   it. Preserve a bounded display form of nonstandard strings.
5. Check configured identity constraints before enabling control. Failed identity
   I/O must not expose a potentially desynchronized connection as ready.
6. Publish identity with lock state, authenticated owner/run, reservation time,
   connection state and last activity. Never publish bearer lease tokens.
7. Re-identify after reconnect, invalidate stale identity and detect substitutions.
   Lease loss must not be interpreted as proof of safe physical output state.

## Implemented increment and remaining work

`InstrumentIdentificationReader` captures and logs a driver-supplied synchronous
identity query, explicitly logs unsupported identity, bounds device-controlled
text, parses four-field responses and propagates failed queries after logging.
The caller must configure transport timeouts. Both built-in host simulators are
identified before reservations are available. The dashboard exposes cached
identity, observation time, exclusive policy and lock state; polling does not
requery instruments. Host activity also goes to the configured ILogger sinks.

This is not yet universal open interception: async, direct drivers, VISA/IVI,
generated drivers and reconnect paths still need integration with a common open
lifecycle. Definition schema fields in the table above are requirements, not
implemented schema fields. Production identity-bound ownership, expiration,
fencing, durable logging and owner/run dashboard fields remain outstanding.
The demo activity buffer is bounded and not a durable audit store. Configure a
persistent logging sink for retention; central database availability must not
be required for local identification logging.

Added xUnit source coverage for capture/logging, unsupported identification,
failure propagation, nonstandard responses and dashboard lease transitions.
These C# tests have not been executed in this environment (no .NET SDK).
