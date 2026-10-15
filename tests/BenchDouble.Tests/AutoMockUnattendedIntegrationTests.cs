using BenchDouble.Monitoring;
using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>
/// One actual C# backend integration path: AutoMock -> instrument -> monitor.
/// Browser streaming and SQLite recording are separate pending integrations.
/// </summary>
public sealed class AutoMockUnattendedIntegrationTests
{
    private sealed class RecordingSink : ICurrentAlarmSink
    {
        public List<CurrentAlarm> Events { get; } = new();
        public ValueTask NotifyAsync(CurrentAlarm alarm, CancellationToken ct = default)
        {
            Events.Add(alarm);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AutoMockFaultTriggersOneAlarmAndControllerTurnsOutputOff()
    {
        var clock = new ManualSimulationClock();
        var rules = new AutoMockLoadRulesBuilder()
            .FixedCurrent("overcurrent", ElectricCurrent.FromAmperes(2.5),
                new AutoMockLoadCondition(From: TimeSpan.FromSeconds(5)))
            .Build();
        var behavior = new RuleBasedLoadBehavior(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)), rules);
        var supply = new MockPowerSupply(behavior, clock);
        var sink = new RecordingSink();
        var monitor = new UnattendedCurrentMonitor(
            clock, ElectricCurrent.FromAmperes(2), sink);
        await supply.ConfigureAsync(ElectricPotential.FromVolts(12),
            ElectricCurrent.FromAmperes(3));
        await supply.SetOutputAsync(true);

        var nominal = await supply.MeasureAsync();
        Assert.Null(await monitor.ObserveAsync("RUN-AUTOMOCK", nominal.Current));
        Assert.Equal(0.5, nominal.Current.Amperes, 10);

        clock.AdvanceBy(TimeSpan.FromSeconds(5));
        var fault = await supply.MeasureAsync();
        var alarm = await monitor.ObserveAsync("RUN-AUTOMOCK", fault.Current);
        Assert.NotNull(alarm);
        Assert.Equal(2.5, fault.Current.Amperes, 10);
        Assert.Equal(TimeSpan.FromSeconds(5), alarm!.SimulatedElapsed);
        Assert.Equal("overcurrent", behavior.EvaluateAt(
            ElectricPotential.FromVolts(12), clock.Elapsed).MatchedRule);

        // The test controller explicitly requests shutdown; the notification
        // sink is not a hardware safety interlock.
        await supply.SetOutputAsync(false);
        Assert.Equal(0, (await supply.MeasureAsync()).Current.Amperes);
        Assert.Null(await monitor.ObserveAsync("RUN-AUTOMOCK",
            (await supply.MeasureAsync()).Current));
        Assert.Single(sink.Events);
    }
}
