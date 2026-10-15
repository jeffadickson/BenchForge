# Requirements Traceability and Unattended Monitor Testing

## Why three test layers?

The Unattended Operations Monitor separates **decision logic**, **requirements behavior** and **browser rendering**. Graphics are not a reason to skip unit testing: the plot is a projection of measurement state. Most correctness is verified in ordinary .NET tests; browser tests check wiring and visible behavior.

| Layer | Framework | What is checked | Limitation |
|---|---|---|---|
| C# component | xUnit | threshold boundary, 5-second virtual fault, one alarm, concurrency and independent time | does not verify UI |
| Executable requirement | Reqnroll.xUnit | Given 0.5 A, when 2.5 A at 5 s, then one overcurrent alarm | tests backend behavior, not pixels |
| Browser | Microsoft.Playwright Chromium | SVG polyline exists, alarm/status text, simulated notification log, reset, disabled email/SMS | does not yet verify that C# engine supplies browser data |
| Visual regression (planned) | Playwright screenshots + approved baselines | layout, responsive graphics, labels, contrast and accessibility | screenshot comparison and review not yet implemented |

## Component test example

```csharp
[Fact]
public async Task FaultAtFiveSecondsCreatesExactlyOneAlarm()
{
    var clock = new ManualSimulationClock();
    var sink = new FakeSink();
    var monitor = new UnattendedCurrentMonitor(
        clock, ElectricCurrent.FromAmperes(2), sink);

    await monitor.ObserveAsync("RUN-1", ElectricCurrent.FromAmperes(0.5));
    clock.AdvanceBy(TimeSpan.FromSeconds(5));
    await monitor.ObserveAsync("RUN-1", ElectricCurrent.FromAmperes(2.5));
    await monitor.ObserveAsync("RUN-1", ElectricCurrent.FromAmperes(2.5));

    Assert.Single(sink.Events);
    Assert.Equal(TimeSpan.FromSeconds(5), sink.Events[0].SimulatedElapsed);
}
```

This is the basic pattern used in `tests/BenchDouble.Tests/UnattendedCurrentMonitorTests.cs`. It runs without hardware, a browser, a database, email, or real five-second waiting.

## Executable requirement example

```gherkin
@BD-UOM-001 @BD-TEST-001
Feature: Unattended operations overcurrent alarm
  Scenario: Timed mock fault triggers one operator notification
    Given a simulated power supply monitored with a 2 ampere limit
    And the mock current is 0.5 amperes
    When the monitor observes the current
    Then no overcurrent alarm is raised
    When simulation time advances by 5 seconds
    And the mock current is 2.5 amperes
    And the monitor observes the current
    Then exactly one overcurrent alarm is raised at 5 simulated seconds
```

Reqnroll binds each line to C# methods in `tests/BenchDouble.Acceptance.Tests/Steps/UnattendedOperationsSteps.cs`. This makes the acceptance criterion reviewable by engineers and executable by CI.

## Browser test example

```csharp
await page.Locator("#fault").ClickAsync();
await page.Locator("#status.alarm").WaitForAsync();
var log = await page.Locator("#log").InnerTextAsync();
Assert.Contains("ALARM:", log);
Assert.Contains("SIMULATED EMAIL", log);
Assert.True(await page.Locator("#plot polyline").CountAsync() >= 1);
```

This verifies that the real Chromium browser updates its DOM/SVG after the simulated fault. It is **not** proof of C# backend integration because the current HTML demo runs a standalone JavaScript simulation. The architecture must eventually use one backend event stream, at which point the browser test will assert rendered values against a recorded C# event fixture.

## Requirements management workflow

1. Define a unique requirement in `requirements/benchdouble.sdoc`.
2. Add a row in `requirements/traceability.csv` with priority, status, code/test file and GitHub issue.
3. Add a Reqnroll scenario for behavior-level acceptance and xUnit component tests.
4. Add Playwright checks for visible UI and interactions where applicable.
5. Run `python3 scripts/check_requirements.py` and `strictdoc export requirements`.
6. Run all tests in the Windows/macOS/Linux CI matrix.
7. Change status to `Verified` only after reviewing actual successful CI evidence and documenting the run.

The StrictDoc baseline currently covers **154 requirements** (90 runner + 32 cross-subsystem). Many have source but no verified test evidence. That is an explicit release risk, not a claim of completion.

## Cross-platform provisioning

- `bash scripts/bootstrap.sh`: macOS/Linux.
- `pwsh -File scripts/bootstrap.ps1`: Windows.
- `make setup`, `make verify`, `make test-browser`: Unix/Make users.
- `global.json` pins .NET 8 feature-band policy; NuGet packages have explicit versions.
- Browser CI builds the Playwright .NET test project, installs Chromium, then runs tests.
- Python StrictDoc runs in a virtual environment locally or via setup-python in CI.
- Vendor-specific VISA/GPIB/IVI drivers remain optional and may not support all OSes.

**Not yet implemented:** centrally managed NuGet versions/lockfiles, pinned StrictDoc package lock, browser screenshot baseline comparisons, C# WebAssembly demo, and confirmed successful cross-platform CI run.
