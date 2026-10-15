# BenchDouble Feature-to-Requirement Audit

**Date:** 2026-10-15 · **Basis:** 90 previously consolidated runner requirements plus 32 new cross-subsystem requirements, selected implementation files and existing GitHub Issues #1–#26. **Scope:** repository architecture/feature inventory; not an exhaustive line-by-line verification or executed test report.

| Feature family | Requirements baseline | Committed evidence | Outstanding work |
|---|---|---|---|
| Instrument class API, IVI and legacy Keithley | TR-029..040, issues #1–#8, #18–#19 | Keithley236.cs, typed instruments | full IVI/VISA parity, real GPIB hardware safety |
| Local/remote host and reservations | TR-029..040, #9–#13 | host prototypes | network auth/leases/fencing/versioned protocol |
| Mock/DI/Roslyn behavior | TR-041..050, BD-SIM-001/004 | PowerSupplyBehavior.cs | isolated Roslyn workers, package approval |
| Shared virtual clock and physics graph | BD-SIM-002/003, #16/#25 | ThermalSignals.cs, VirtualBenchGraph.cs | coherent concurrency and feedback solver |
| Thermal cycle | BD-SIM-005/006 | ThermalCycle.cs and demo | physical DUT stabilization, live backend plot |
| Unattended operations monitor | BD-UOM-001..008 | UnattendedCurrentMonitor.cs, HTML demo | backend/UI convergence, notification outbox, safety |
| Unit-aware plots | TR-073, BD-TS-006, #21 | MeasurementPlot.cs, ScottPlot adapter | live browser plots, uncertainty, downsampling |
| Time-series persistence | BD-TS-001..006, #26 | SQLite recorder, quantity registry | remote replication, high-rate batching, min/max |
| Procedure editor and execution | TR-001..028, #14/#17 | ExperimentRunner prototype | redlines, signatures, integrated browser editor |
| Offline recording and recovery | TR-055..060, #26 | SQLite WAL | durable outbox, conflict reconciliation |
| Logging and OpenTelemetry | TR-076..081, #10 | structured logging prototypes | end-to-end trace and host replay |
| Requirements and acceptance | BD-REQ-001..005 | StrictDoc, CSV, Reqnroll/xUnit | actual CI evidence, expanded acceptance coverage |
| Browser UI/graphics testing | BD-UOM-006, BD-BUILD-004 | Playwright tests | run Chromium matrix and screenshot artifacts |
| Windows/macOS/Linux | BD-BUILD-001..003 | net8, global.json, bootstrap scripts, Actions | clean-host verification, package lockfiles |
| Manuals and project overview | BD-DOC-001/002, BD-WASM-001 | SDP, manual, HTML overview | C# WebAssembly runtime, release docs |

## Audit findings

1. **Requirement gap closed at specification level:** newly identified monitoring, plotting, time-series, platform, documentation, BDD and browser-testing needs now have stable requirement IDs.
2. **Implementation gap remains:** the existing 90 TR requirements contain significant planned functionality, especially released procedures, redlines, signatures, secure hosts and physical hardware validation.
3. **Evidence gap remains:** a committed test is not proof of passing CI. All rows are deliberately `Planned` or `SourceCommittedUnverified`; no `Verified` rows until run evidence is reviewed.
4. **Browser/C# mismatch:** current HTML demos use independent JavaScript simulations. A real-browser test can verify UI, but it does not prove the C# engine drives the chart. Backend-stream integration is tracked.
5. **Dependency reproducibility:** bootstrap scripts provision .NET and StrictDoc; centralized NuGet versions, lockfiles and strict tool-version pinning remain to be completed.
6. **Cross-platform qualification:** core .NET 8 libraries target all three OSes. Real VISA/IVI/GPIB hardware support is vendor-specific and cannot be guaranteed by cross-platform .NET alone.

## Maintenance rule
On each feature PR, add/update a requirement UID, source/test link, verification evidence status, and GitHub issue. CI checks file references and unique IDs. The StrictDoc HTML output is a navigable requirements artifact, not an automated proof of implementation.
