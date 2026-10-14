# Official IVI.NET integration — phase 1

## Objective
Use real IVI Foundation assemblies instead of redefining their class contracts. The portable `BenchDouble` project must have **no** vendor/IVI dependencies; official integration is in `BenchDouble.Ivi.Official`.

## Real dependencies
The project now references:
- `IviFoundation.Driver` 2.1.0 (legacy IVI driver shared components migrated to .NET 6+)
- `IviFoundation.Driver.Dmm` 2.1.0
- `IviFoundation.Driver.Fgen` 2.1.0
- `IviFoundation.DriverCore` 1.0.1 (Generation 2026 core)

Official namespaces of legacy instrument classes are `Ivi.Dmm` and `Ivi.Fgen`. The adapter boundary uses actual `IIviDmm` and `IIviFgen` compile-time types, not BenchDouble-owned substitutes.

## What is now coded
`OfficialIviCompatibility` tests and inspects the actual official interface types and rejects arbitrary objects; `OfficialDmmBridge` accepts an official DMM object plus explicitly mapped operation delegates, producing a `IIviDigitalMultimeter` object used by application tests. This enables integrating a real IVI DMM via separately supplied functions without claiming a universal mapping of every class operation.

## What this is NOT
This is not a full IVI driver implementation, driver factory, vendor loader, automatic mapping of the official class methods, an oscilloscope/function-generator operational bridge, or conformance certification. The production method mappings and their signatures must be validated against an actual vendor driver and the published interface members.

## Acceptance and next work
1. Confirm the optional project restores/compiles in CI with official packages.
2. Add type/member contract tests against the official assemblies and real vendor driver adapters.
3. Implement standard IVI class operations using the real class API (with repeated-capability/channel semantics).
4. Add function-generator operational adapter and feature/range matching.
5. Integrate IVI configuration store / factories where supported. A driver may need to be explicitly loaded because .NET 6+ does not use the historical GAC discovery behavior.
6. Demonstrate one compiled test using an actual IVI DMM and BenchDouble mock with identical application code.
7. Test behavior on Windows hardware before making interchangeability claims.

## Source material
- https://github.com/IviFoundation/IviDrivers/blob/main/Documentation/IviDotNetDriversWithCSSharedComponents.md
- https://www.nuget.org/packages/IviFoundation.Driver.Dmm/
- https://www.nuget.org/packages/IviFoundation.Driver.Fgen/
- https://www.nuget.org/packages/IviFoundation.DriverCore/
- https://www.ivifoundation.org/downloads/Architecture%20Specifications/IVI-3.1_Architecture_2022-12-19.pdf
