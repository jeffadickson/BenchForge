# BenchDouble logging and observability

## Decision
Use **Microsoft.Extensions.Logging** with structured fields, dependency injection and pluggable providers. Do not implement a custom log file format or hardwire Serilog/Seq/Application Insights into the instrument core. The initial implementation is `LoggingScpiSession`, a decorator over `IScpiSession`.

## Logging responsibilities
- Instrument identity (safe logical ID), explicit backend (physical/simulated/mock), operation, duration, success/failure, and relevant correlation identifiers.
- Protocol operation timing and failure diagnosis, with structured fields instead of interpolated strings.
- Redaction and opt-in raw payload capture. SCPI commands and replies may contain secrets, file paths, or proprietary test data.
- Correlate procedure/run/step and multi-instrument activity with `ILogger.BeginScope` and later `ActivitySource` / OpenTelemetry.
- Record exceptional termination and *unknown hardware state* after a failed cleanup, not a false assertion that an output was disabled.

## Current implementation
`LoggingScpiSession` wraps `Write` and `Query`, logs start/completion and elapsed duration, logs exceptions while preserving original exception behavior, and hides command and reply contents unless explicitly enabled. A caller can pass a redactor. Instrument name is required. It does not open physical hardware and makes no claims about hardware state. Calls are synchronous because the existing session contract is synchronous.

## Integration example
```csharp
using BenchDouble;
using BenchDouble.Observability;
using Microsoft.Extensions.Logging;

using var factory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
IScpiSession physicalOrSimulated = new MultimeterSimulator { InjectedVoltage = 5.0 };
IScpiSession logged = new LoggingScpiSession(
    physicalOrSimulated, factory.CreateLogger<LoggingScpiSession>(),
    new ScpiLoggingOptions("DMM1"));
var dmm = new DigitalMultimeter(logged);
dmm.ConfigureDcVoltage();
double voltage = dmm.MeasureDcVoltage();
```
The console provider used by this example is an optional application dependency, not a dependency of the library.

## Recommended event taxonomy
`Instrument.Session.Open`, `Instrument.Command.Write`, `Instrument.Command.Query`, `Instrument.Command.Response`, `Instrument.Measurement.Acquired`, `Instrument.State.Changed`, `Instrument.Fault.Injected`, `Instrument.Sequence.Mismatch`, `Instrument.Session.Close`.

## Operational rules
1. Structured logs use named fields; no automatic string interpolation of raw commands.
2. Command and response capture are separately gated and off by default.
3. Raw binary samples should be bounded and summarized, not dumped into logs.
4. Do not automatically retry non-idempotent instrument operations.
5. A physical resource must never become simulated as an implicit exception fallback.
6. Log events should carry backend provenance and logical instrument ID; this first decorator has instrument ID but needs end-to-end backend metadata integration.
7. Diagnostic events are not equivalent to a durable, lossless acquisition transcript; implement a separate recorder for legally/audit-required records.
8. For future async adapters, log cancellation separately from faults and propagate cancellation.
9. OpenTelemetry shall be an optional bridge that uses `ActivitySource`, meters, and `ILogger`, avoiding mandatory vendor exporters.
10. Logging should be tested to prove the default redaction policy and that exceptions are rethrown.

## Design patterns
This is a **Decorator** and complements instrument **Adapter**, communication **Strategy**, parameter-descriptor and **Observer** designs. See [DESIGN-PATTERNS.md](DESIGN-PATTERNS.md).

## Remaining work
- Execute new tests and confirm three-platform CI.
- Add backend/procedure/step correlation scopes.
- Typed instrument-operation and measurement logging above the SCPI boundary.
- Optional OpenTelemetry spans and meters, exporters configured by the application.
- Explicit transcript recorder/replay format with versioning and bounded storage.
- Validate redactors for SCPI quoting, binary data and known proprietary paths.
