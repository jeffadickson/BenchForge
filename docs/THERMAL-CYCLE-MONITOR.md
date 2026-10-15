# Thermal Cycle Monitor — Chamber + Power Supply Demonstration

## Purpose
A second end-to-end BenchDouble example: an idealized environmental chamber cycles between +70°C and −30°C, while a separately injected power-supply load model changes current with chamber temperature. The **same run-scoped simulation clock** drives both instruments.

This is a **thermal-chamber setpoint** demonstration, not a vacuum-pressure model, a calibrated chamber dynamics simulator, or hardware control.

## Default seven-hour profile

| Segment | Simulated interval | Chamber setpoint |
|---|---|---|
| Heat ramp | 0–1 h | +30°C to +70°C |
| Hot dwell | 1–3 h | +70°C |
| Cool ramp | 3–4 h | +70°C to −30°C |
| Cold dwell | 4–6 h | −30°C |
| Return ramp | 6–7 h | −30°C to +30°C |

Ramp durations are chosen defaults, not user-provided requirements. They are configurable via `ThermalCycleSettings`.

## Electrical behavior

The example holds the power supply at 12 V and injects a `TemperatureDependentLoadBehavior` with 0.500 A nominal current. It increases linearly with temperature from 30°C to 70°C, reaching **0.550 A** at 70°C (+10%), and remains at 0.500 A below 30°C. This is a deliberately simplified behavior for illustrating dependency injection and a shared virtual bench.

```text
I(t) = 0.5 A × [1 + 0.10 × clamp((T(t) - 30°C) / 40°C, 0, 1)]
```

A real thermal/electrical DUT could instead inject a temperature-dependent IV curve, hysteresis, delayed fault, leakage current or nonlinear behavior.

## Running

**Browser:** Open [examples/ThermalCycleMonitor/index.html](../examples/ThermalCycleMonitor/index.html) directly. This standalone HTML page includes two SVG plots, 1×-style virtual pacing options of 60×, 3600×, 7200×, 14400×, start/pause/reset, manual step, phase indicators and event log. No external network or instrument connection.

**C# console:** `dotnet run --project examples/BenchDouble.ThermalCycleDemo` or `make demo-thermal`. It uses Microsoft DI to create one clock, chamber model, temperature-dependent load behavior and mock supply per run, advances 5-minute steps without waiting, prints hourly readings, and writes two Plotly.js-compatible JSON figure files to `thermal-demo-output/`.

**Current status:** browser demo and .NET source committed, but .NET compilation and browser visual review are not yet verified.

## Integration with BenchDouble architecture

- `ThermalCycleSettings`: typed UnitsNet temperature settings and configurable durations.
- `ThermalChamberModel`: pure piecewise-linear setpoint evaluation and phase.
- `TemperatureDependentLoadBehavior : ILoadBehavior`: injected temperature-current relationship.
- `ManualSimulationClock : ISimulationClock`: single shared logical time.
- `MockPowerSupply`: output enable, UnitsNet current/voltage measurement, idealized compliance metadata.
- `MeasurementPlot`: independent UnitsNet plotting adapter; console exports labeled time/current and time/temperature Plotly figure JSON.
- Browser demo: native SVG rendering only, separate from .NET execution. A future version should consume the actual .NET runner's stream.

## Requirements for production thermal testing

1. Distinguish chamber setpoint from sensor-measured chamber/fixture/DUT temperatures.
2. Model configurable ramp rates, dwell qualification only after actual DUT sensor reaches tolerance, overshoot, lag, hysteresis, spatial gradients and optional pressure.
3. Define hot/cold dwell start rules explicitly: schedule time vs achieved temperature.
4. Record calibrated sensor readings, timestamp, sample quality, instrument identity, environmental limits and independent current/voltage measurement channels.
5. Support fault alarms, power cutoff, safety interlocks, hold points, abort/cleanup and recovery states independent of browser/network.
6. Use real monotonic time for physical chamber control, lease expiry, operator notifications and safety; accelerated virtual time applies **only** to simulation.
7. Permit behavior injection per run and package version, not uncontrolled hot-swap mid-test.
8. Preserve raw UnitsNet measurements in durable results and apply plot downsampling only to visualization.
9. Test all phase boundaries, exact +10% hot-current behavior, return-to-baseline, negative time rejection, and repeatability at different virtual speeds.

## Acceptance

- At t=0 h: 30°C, 0.500 A.
- At t=0.5 h: 50°C, 0.525 A.
- At t=1 h and t=3 h: 70°C, 0.550 A.
- At t=4 h and t=6 h: −30°C, 0.500 A.
- At t=7 h: 30°C, 0.500 A.
- At manual virtual time 7 h: no seven-hour wall-clock wait.
- The console and browser agree numerically on these checkpoints, and no physical output is enabled.

## Future extensions

Use this example to demonstrate a full electronic test procedure: chamber ramp step, stabilization hold, hot soak, cold ramp, cold soak, recovery, limit checks, redlines, signature, alerts and live plots. Eventually wire it to the remote instrument-host network and to the Unattended Operations Monitor's operator-notification sinks.
