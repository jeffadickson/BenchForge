# IVI architecture and compatibility plan

2026-10-16 status: the optional official DMM/FGEN adapter project described below as a future milestone now exists. See [official integration](OFFICIAL-IVI.md) and the [configuration architecture review](IVI-CONFIGURATION-REVIEW.md) for current source boundaries and the remaining activation work. The following prototype plan is retained as historical context; its unimplemented list is not a current repository-wide inventory.

## Decision

BenchDouble will support IVI as a first-class instrument abstraction, without claiming IVI conformance prematurely.

The initial `BenchDouble.Ivi` namespace defines **BenchDouble-owned IVI-class-inspired contracts**, not the official IVI Foundation binary interfaces. They enable the existing typed SCPI drivers and simulators to share class-level operations. The next milestone is an independently packaged adapter against the actual official IVI.NET interfaces, subject to license and platform compatibility verification.

## Relevant standards

- IVI-4.2 IviDmm class specification
- IVI-4.3 IviFgen class specification
- IVI-4.1 IviScope
- IVI-4.4 IviDCPwr
- IVI Generation 2014: legacy driver-class interchangeability, simulation, caching, range checking and shared components
- IVI Generation 2026: lighter .NET 6+ IVI driver core requirements. Conformance with this generation **does not alone imply** implementation of legacy instrument-class interfaces.

References:
- https://www.ivifoundation.org/specifications/default.aspx/
- https://www.ivifoundation.org/About-IVI/Instrument-Classes.html
- https://github.com/IviFoundation/IviDrivers

## Layering

1. Test application requests DMM, FGEN, power supply or scope features through an instrument-class contract.
2. Adapters expose the same contract for vendor IVI drivers, BenchDouble SCPI-specific drivers, and simulated implementations.
3. Mocks check either instrument-class operations or the underlying SCPI transcript; these are **distinct** verification levels.
4. Optional VISA transports keep vendor-specific native dependencies out of the portable core.

## Prototype implemented

- `IIviDigitalMultimeter` with DC-voltage configuration and measurements
- `IIviFunctionGenerator` with sine-wave configuration and output enable
- Typed capability metadata
- Adapters over existing `IScpiSession` infrastructure
- Tests for class API, unsupported capabilities, and strict SCPI replay

## Not yet implemented

- Official IVI.NET interface implementation or compliance certification
- Legacy IVI COM / driver session-factory integration
- Vendor IVI driver loading
- Full IVI class extensions, status/error semantics and all SCPI dialects
- Async APIs, ownership of instrument resources and session lifecycle
- Compatibility matrix based on real drivers and hardware
- IVI shared components integration
- Real VISA transport

## Implementation priorities

1. Run and stabilize cross-platform CI.
2. Add official IVI.NET interface adapter tests with actual IVI-provided assemblies where redistributable.
3. Design detailed capability profiles (ranges, triggering, measurement functions) before claiming interchangeability.
4. Add profile-specific DMM and FGEN implementations using official manufacturer SCPI documentation.
5. Wire up optional real VISA backend; verify on a physical bench.
6. Extend IVI classes to DC supplies and scopes.
7. Determine licensing and packaging requirements for official IVI shared components.

## Safety and accuracy

Never present a partial SCPI model as a calibrated instrument. Unsupported features must throw rather than silently pass. Hardware safety interlocks must be independently verified on real equipment.
