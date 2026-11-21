# BenchForge Audit: Railroad Errors-as-Types, .NET 11, External Libraries & Industry Driver Sources

This audit evaluates BenchForge as a **flight-hardware Electrical Ground Support Equipment (EGSE)** platform on **.NET 11**, focusing on:
1. Railroad-oriented errors-as-types (`BenchResult<T>` / `BenchError`) for human and AI-agent clients,
2. `.NET 11` / C# runtime capabilities currently used vs. ready to exploit,
3. External libraries worth adopting (and what to deliberately avoid on air-gapped flight-test benches), and
4. Industry open-source instrument driver repositories for expanding the clean-room catalog.

---

## 1. Railroad Errors-as-Types Audit

### Findings in Existing Code
| Area | Previous Behavior | Risk for Flight Hardware / Clients / Agents | Remediation Implemented / Planned |
| --- | --- | --- | --- |
| Low-level SCPI drivers (`src/BenchForge/Generated/*.g.cs`, `ScpiTcpSession`) | Threw untyped `IOException`, `FormatException`, `TimeoutException`, `InvalidOperationException` | Callers cannot tell whether physical state was modified before failure; HTTP/gRPC boundaries collapse distinct faults into 400/500 strings; AI agents have no structured `Remedy`. | Added [`BenchResult`](../src/BenchForge/Results/BenchResult.cs), [`BenchResult<T>`](../src/BenchForge/Results/BenchResult.cs), [`BenchError`](../src/BenchForge/Results/BenchResult.cs), [`PhysicalStateConfidence`](../src/BenchForge/Results/BenchResult.cs), and [`BenchWireResult<T>`](../src/BenchForge/Results/BenchResult.cs) DTOs. |
| Canonical command dispatcher (`CanonicalCommandDispatcher`) | Returned `CommandReceipt` with `State` + `FaultKind` | Great internal model, but callers still had to manually interpret 8 states × 11 faults. | Added `BenchError.FromReceipt(CommandReceipt)` mapping every receipt into `BenchErrorKind`, `PhysicalStateConfidence`, `RequiresRecovery`, and `Remedy`. |
| Symmetric instrument interfaces & mocks | Split between sync `IDigitalMultimeter`, `ITypedDigitalMultimeter`, `ITypedConfigurableMeasurementSource`, and raw mocks | Hard to write one client program that swaps real hardware ↔ high-fidelity stateful mock. | Added [`IBenchDigitalMultimeter`, `IBenchPowerSupply`, `IBenchSourceMeasureUnit`, `IBenchThermalController`, `IBenchMockControl`](../src/BenchForge/Interfaces/CanonicalInstrumentInterfaces.cs) — all async, `UnitsNet`-typed, and returning `BenchResult<T>`. |
| Flight-hardware safety & client SDK | No built-in envelope, monotonic ramp, calibration expiry gate, or agent bench manifest | Sudden voltage steps or expired calibration could slip through ad-hoc scripts. | Added [`FlightSafeEnvelope`, `FlightEnvelopedSmuProxy`, `SafeVoltageRampController`, `FlightEvidencePackage`](../src/BenchForge/Flight/FlightSafetyAndEvidence.cs) and [`BenchSession` / `BenchBuilder`](../src/BenchForge/Client/BenchClientSdk.cs) with `DescribeJson()`. |
| Host HTTP/gRPC endpoints (`BenchForge.Host.Server/Program.cs`) | Returns `HostError(code, message)` + HTTP 400/403/409 | Still loses `PhysicalStateConfidence` and `Remedy` on legacy endpoints. | Next step: emit `BenchWireResult<T>` across new gRPC v1 and `/api/v2/*` endpoints while keeping `/api/v1/*` backwards-compatible. |

### Rule of Thumb Going Forward
- **Use exceptions only for programmer bugs** (`ArgumentNullException`, `ObjectDisposedException`, invalid builder configuration).
- **Return `BenchResult<T>` for every operational, safety, transport, lease, calibration, and limit outcome** so clients, UI, and AI agents handle failure tracks exhaustively without catching blind `Exception`.

---

## 2. .NET 11 Feature Audit

| .NET 11 / Modern C# Capability | Current Status in BenchForge | Flight-Hardware Value & Action |
| --- | --- | --- |
| `net11.0` TFM & `global.json` (`11.0.100`) | **Adopted** across all 38 projects | Unlocks .NET 11 JIT deabstraction, Arm64 paired loads, and smaller delegate layouts on lab boxes / Raspberry Pi EGSE. |
| `System.Threading.Lock` (`Lock.EnterScope()`) | **Adopted** in new `CanonicalInstrumentInterfaces`, `FlightSafetyAndEvidence`, `BenchClientSdk`, `PrivateLabSecurity` | Faster, zero-monitor-overhead locking; incrementally migrate legacy `object _gate` locks in hot paths (`HostResourceRegistry`, `CanonicalCommandDispatcher`). |
| `System.IO.Compression.Brotli` (`BrotliStream`) | **Adopted** in `CompressedArtifactCodec` | Achieved **31.6× compression** (32 KiB → 1,035 B) on synthetic waveforms with zero external NuGet dependencies and bounded zip-bomb caps. |
| `TimeProvider` | **Adopted** in `HostResourceRegistry`, `BenchSession` calibration preflight | Deterministic time control for calibration expiry, heartbeats, and flight test replays without wall-clock flakiness. |
| `System.Numerics.Tensors.TensorPrimitives` | **Opportunity (Next)** | Built-in SIMD vector math for oscilloscope waveform RMS, crest factor, min/max, DC offset, and cross-correlation with zero native library dependencies. |
| `System.Text.Json` Source Generation (`JsonSerializerContext`) | **Opportunity (Next)** | Eliminates reflection in `BenchForge.Host.Server`, `BenchForge.Checks`, and `BenchWireResult<T>`, enabling true Native AOT single-binary EGSE hosts. |
| `.NET 11` Runtime Async (opt-in) | **Monitor & Benchmark** | Smaller async state machines in high-rate acquisition loops; benchmark before enabling globally since Microsoft notes it is opt-in in .NET 11. |
| `Microsoft.NET.Test.Sdk 18.12.0` + xUnit | **Adopted** (upgraded from 17.12.0) | Consider migrating to **xUnit v3 + Microsoft.Testing.Platform** in a future sweep for native session timeouts and faster parallel test execution. |

---

## 3. External Library Audit (Air-Gapped Flight-Hardware Lens)

For flight-hardware EGSE running on isolated private networks, **every external dependency must earn its footprint** (license clarity, offline vendoring, determinism, zero telemetry):

| Category | Recommendation | Rationale |
| --- | --- | --- |
| **Errors-as-Types / Railroad** | **Keep first-party `BenchResult<T>`** (do *not* adopt `FluentResults` / `ErrorOr` / `OneOf`) | Zero external dependency, exact domain fields (`PhysicalStateConfidence`, `RequiresRecovery`, `Remedy`), trivial JSON/gRPC serialization, and zero reflection. |
| **Physical Units** | **Keep `UnitsNet` today; bridge to `SharpUnits` via `CanonicalQuantity`** | Already wrapped behind [`QuantityInterop.cs`](../src/BenchForge/Quantities/QuantityInterop.cs) so `SharpUnits` can drop in when ready without breaking drivers. |
| **Procedure Scripting** | **`StarLark-cs` (`external/starlark-cs`)** | Already integrated with deterministic `EnableFutures = true`, uncatchable step/memory/future/timeout budgets, and zero OS/network escape. |
| **Formal Safety Contracts** | **Bridge to `SharpContracts` via `IInstrumentOperationGuard`** | Seam already enforced by `GuardedScpiSession` and `CanonicalCommandDispatcher`. |
| **Networked Remote Host** | **Add `Grpc.AspNetCore` + `Grpc.Net.Client` (Phase 3)** | Implements `proto/benchforge/host/v1/host.proto` with HTTP/2 streaming telemetry, strong protobuf contracts, and native mTLS. |
| **Compression** | **Keep built-in `BrotliStream`; add `System.IO.Compression.Zstandard` NuGet only if >500 MB/s scope streaming is needed** | `BrotliStream` is in-box (`net11.0`) with zero supply-chain surface; Zstandard is worthwhile only for ultra-high-throughput oscilloscope DMA streaming. |
| **DSP / Waveform Math** | **Start with in-box `System.Numerics.Tensors.TensorPrimitives`; optional `MathNet.Numerics` (MIT) for FFT/filters** | Avoids native BLAS/MKL deployment headaches on air-gapped Linux/Windows boxes. |
| **Resilience (`Polly`)** | **DO NOT wrap instrument actuation in Polly retries** | Blind retries violate `BD-CMD-006` and can double-pulse or double-energize flight hardware. Use `CanonicalCommandDispatcher`'s explicit `CommandSafetyClass` budgets instead. |
| **Observability** | **Optional `OpenTelemetry` (`System.Diagnostics.ActivitySource` is already in-box!)** | Emit standard `ActivitySource` spans in `BenchForge` with zero NuGet cost; integrators attach OTLP/Prometheus exporters only if desired. |
| **CLI / Agent Integration** | **Optional Model Context Protocol (MCP) server adapter over `BenchSession.DescribeJson()` + `BenchResult<T>`** | Lets AI agents safely discover bench roles, run read-only checks, and propose guarded actions with structured `BenchError.Remedy` feedback. |

---

## 4. Industry Open-Source Instrument Driver Sources

When expanding BenchForge's clean-room SCPI catalog beyond the current **36 instruments**, these are the highest-value industry and open-source reference repositories (always inspect manufacturer programming manuals and respect upstream licenses; record any third-party source provenance exclusively in [`THIRD_PARTY_REVIEW.md`](../THIRD_PARTY_REVIEW.md)):

| Repository | Maintainer / Ecosystem | License | Best Used For |
| --- | --- | --- | --- |
| **[IviFoundation/IviDrivers](https://github.com/IviFoundation/IviDrivers)** & **[InstrumentDriverSpecs](https://github.com/IviFoundation/InstrumentDriverSpecs)** | IVI Foundation | IVI Open Spec / BSD-like | Canonical instrument class contracts (`IviDmm`, `IviDcPwr`, `IviFgen`, `IviScope`, `IviSwtch`, `IviSpecAn`) and `.NET 6+` IVI.NET interoperability. |
| **[opentap/Keysight-Instrument-Plugins](https://github.com/opentap/Keysight-Instrument-Plugins)** | Keysight / OpenTAP | MPL-2.0 / MIT (per plugin) | Cross-platform **C# / .NET** SCPI instrument plugin patterns for Keysight power supplies, DMMs, VNAs, and signal analyzers. |
| **[tektronix/tm_devices](https://github.com/tektronix/tm_devices)** | Tektronix / Keithley | Apache-2.0 | Authoritative Tektronix oscilloscopes (MSO2/4/5/6, AWG) and Keithley SMUs/DMMs (2450, 2600, DMM6500, DAQ6510) SCPI & TSP command trees. |
| **[Rohde-Schwarz/RsInstrument](https://github.com/Rohde-Schwarz/RsInstrument)** & **[Examples](https://github.com/Rohde-Schwarz/Examples)** | Rohde & Schwarz | MIT / Royalty-free | Official R&S SCPI command patterns (C# & Python), OPC sync, error-queue draining, OpenTelemetry tracing, and write-policy MCP patterns. |
| **[QCoDeS/Qcodes](https://github.com/QCoDeS/Qcodes)** & **[Qcodes_contrib_drivers](https://github.com/QCoDeS/Qcodes_contrib_drivers)** | Microsoft / Quantum & Cryo Community | MIT | Hundreds of lab/aerospace/cryo SCPI drivers (Keysight, Tektronix, Keithley, Lake Shore, Rohde & Schwarz, Zurich Instruments, Stanford Research Systems). |
| **[instrumentkit/InstrumentKit](https://github.com/instrumentkit/InstrumentKit)** | Open-source community | MIT | Vendor-agnostic SCPI command profiles across Ethernet, GPIB, USBTMC, VXI-11, and serial. |
| **[lxi-tools/liblxi](https://github.com/lxi-tools/liblxi)** & **[gotmc/ivi](https://github.com/gotmc/ivi)** | Open-source LXI / Go IVI | BSD-3-Clause / MIT | Pure-protocol LXI mDNS/VXI-11/HiSLIP discovery references and clean IVI class-contract layouts. |
| **Prior catalog corpus** | Tracked in [`THIRD_PARTY_REVIEW.md`](../THIRD_PARTY_REVIEW.md) | MIT (legal review tracked) | Historical 484-class inventory referenced during early discovery (`manifest.json`); BenchForge's current 35 generated definitions are authored clean-room from standard manufacturer SCPI strings. |
