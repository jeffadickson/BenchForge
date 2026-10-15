using BenchDouble.Monitoring;
using BenchDouble.Simulation;
using Reqnroll;
using UnitsNet;
using Xunit;

namespace BenchDouble.Acceptance.Tests.Steps;

/// <summary>Executable Gherkin acceptance evidence for BD-UOM-001.</summary>
[Binding]
public sealed class UnattendedOperationsSteps
{
    private readonly ManualSimulationClock _clock = new();
    private readonly RecordingSink _sink = new();
    private UnattendedCurrentMonitor? _monitor;
    private ElectricCurrent _current;

    [Given(@"a simulated power supply monitored with a (.*) ampere limit")]
    public void GivenMonitor(double threshold)
    {
        _monitor = new UnattendedCurrentMonitor(_clock,
            ElectricCurrent.FromAmperes(threshold), _sink);
    }

    [Given(@"the mock current is (.*) amperes")]
    [When(@"the mock current is (.*) amperes")]
    public void SetCurrent(double amps) =>
        _current = ElectricCurrent.FromAmperes(amps);

    [When("the monitor observes the current")]
    public async Task ObserveAsync() =>
        await _monitor!.ObserveAsync("BDD-RUN", _current);

    [When(@"simulation time advances by (.*) seconds")]
    public void Advance(int seconds) =>
        _clock.AdvanceBy(TimeSpan.FromSeconds(seconds));

    [Then("no overcurrent alarm is raised")]
    public void NoAlarm() => Assert.Empty(_sink.Events);

    [Then(@"exactly one overcurrent alarm is raised at (.*) simulated seconds")]
    public void ExactlyOne(int seconds)
    {
        var alarm = Assert.Single(_sink.Events);
        Assert.Equal(TimeSpan.FromSeconds(seconds), alarm.SimulatedElapsed);
        Assert.Equal("OVERCURRENT", alarm.Code);
    }

    private sealed class RecordingSink : ICurrentAlarmSink
    {
        public List<CurrentAlarm> Events { get; } = new();
        public ValueTask NotifyAsync(CurrentAlarm alarm, CancellationToken ct = default)
        {
            Events.Add(alarm);
            return ValueTask.CompletedTask;
        }
    }
}
