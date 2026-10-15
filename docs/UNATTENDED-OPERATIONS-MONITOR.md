# Unattended Operations Monitor — Interactive Demo and Requirements

## Run the demonstration

Open [index.html](../examples/UnattendedOperationsMonitor/index.html) in a browser. The file is fully standalone (HTML/CSS/JS, no external CDN or server). Alternatively run `dotnet run --project examples/BenchDouble.DiDemo` for the C# dependency-injection scenario with simulated operator alerts.

**This is a simulation only.** The browser demo does not talk to the C# backend or a physical instrument, and neither demo sends actual email or SMS. Notification log lines explicitly say **SIMULATED** and **not sent**.

## Features in the browser example

- A responsive dark-theme unattended test monitor.
- Live current plot with configurable overcurrent threshold.
- 12 V, 24 Ω nominal load producing 0.5 A.
- At simulated t=5 s, fault injection raises requested current to 2.5 A.
- Automatic simulated shutdown on threshold crossing (configurable).
- Simulation speeds 1×, 5×, 20×; manual inject-fault button, start/pause/reset.
- Timestamped event log showing alarm, simulated email/SMS notifications and output shutdown.
- Alert channel checkboxes and visible demo-only notice.

## DI notification contract

`examples/BenchDouble.DiDemo/Notifications.cs` defines `IOperatorNotificationSink` and a `SimulatedOperatorNotificationSink`. The demo registers the sink in `Microsoft.Extensions.DependencyInjection` and invokes it on overcurrent without embedding notification provider code in the mock power supply. Later production sinks may implement authenticated SMTP/email or SMS services with an outbox and retries.

**IoC boundaries:**
1. Instrument or mock emits typed measurement samples.
2. Monitor/rule evaluator decides if an alarm threshold is crossed.
3. Event store persists the alarm before notification dispatch.
4. Notification sink is injected by the host composition root.
5. Actual physical shutdown/interlocks are independent of notification delivery.

## Requirements for production

- Define `IMonitorRule`, `IAlarmEventStore`, `IOperatorNotificationSink`, `INotificationDeliveryStatus` and `ISafetyAction` contracts separately.
- Use an append-only durable outbox for notification requests; persist attempt IDs, provider receipt, retry/ack status and delivery failure. Do not say **sent** merely because an API call was initiated.
- Deduplicate alarms and notifications by run/instrument/rule/event IDs. Support escalation policies, cooldown, acknowledgment, operator on-call schedules and alert severity.
- Add configurable alarm conditions: high/low threshold, rate-of-change, missing telemetry, sensor stale, heartbeat loss, instrument fault and watchdog timeout.
- Support hysteresis/debounce and measurement-quality validation to avoid noise-induced false alarms.
- Plot raw UnitsNet samples using the reusable `BenchDouble.Plotting`/Plotly.js layer. Current standalone page draws a native SVG plot to avoid runtime dependencies.
- Host and client display matching event IDs, timestamp, measurement provenance and correlation to procedure steps. Resume after reconnect with cursor replay.
- Operators must authenticate and be authorized for acknowledgments, alarm configuration and hardware shutdown. Do not leak personal contact details in logs.
- Safety interlocks and verified physical safe-state recovery must not depend on email/SMS, web UI availability or client connectivity.
- Do not auto-enable output on restart or re-arm after a trip without configured authorization.
- Make notification sinks pluggable via DI; support a fake sink for CI, a local demo sink, and separate real-provider adapters.
- Add integration tests for lost network, failed delivery, retries, stale readings, alarm deduplication, threshold changes, simulated clock scaling and cancellation.

## Acceptance demo

1. Open the standalone HTML file, start simulation at 5× speed.
2. Observe nominal current at 0.5 A.
3. At t=5 s, verify plot crosses the 2 A limit and an alarm is logged.
4. Verify distinct simulated email and SMS log entries, each labeled not sent.
5. Verify output is disabled in the simulated model, with no real hardware action.
6. Reset and run with notification checkboxes disabled; alarm/shutdown must still work.
7. Run C# DI demo and observe notification messages without modifying mock behavior.
8. Verify no external network request, real email or SMS is made by either demo.

## Implementation status

**Implemented:** standalone browser UI, live SVG plot, alarm rule, simulated notifications and simulated shutdown; C# DI notification interface and fake console sink. **Not implemented:** actual email/SMS providers, persisted outbox, alert acknowledgment/escalation, shared event backend, live remote instrument-host streaming, production interlocks and browser-to-.NET integration. Browser and .NET execution have not yet been verified in the current tool environment.
