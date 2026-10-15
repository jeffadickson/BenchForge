using BenchDouble.Monitoring;
using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class UnattendedCurrentMonitorTests
{
    private sealed class FakeSink : ICurrentAlarmSink
    {
        public List<CurrentAlarm> Events { get; } = new();
        public ValueTask NotifyAsync(CurrentAlarm alarm, CancellationToken ct = default)
        {
            Events.Add(alarm);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task FaultAtFiveSecondsCreatesExactlyOneAlarm()
    {
        var clock = new ManualSimulationClock();
        var sink = new FakeSink();
        var monitor = new UnattendedCurrentMonitor(
            clock, ElectricCurrent.FromAmperes(2), sink);
        for (var i = 0; i < 20; i++)
        {
            var reading = i < 10 ? 0.5 : 2.5;
            var alarm = await monitor.ObserveAsync(
                "RUN-1", ElectricCurrent.FromAmperes(reading));
            Assert.Equal(i == 10, alarm is not null);
            clock.AdvanceBy(TimeSpan.FromMilliseconds(500));
        }
        Assert.Single(sink.Events);
        Assert.Equal(TimeSpan.FromSeconds(5), sink.Events[0].SimulatedElapsed);
        Assert.Equal("OVERCURRENT", sink.Events[0].Code);
    }

    [Fact]
    public async Task ThresholdIsStrictAndNormalRunDoesNotNotify()
    {
        var sink = new FakeSink();
        var monitor = new UnattendedCurrentMonitor(new ManualSimulationClock(),
            ElectricCurrent.FromAmperes(2), sink);
        Assert.Null(await monitor.ObserveAsync("RUN-2", ElectricCurrent.FromAmperes(2)));
        Assert.Null(await monitor.ObserveAsync("RUN-2", ElectricCurrent.FromAmperes(0.5)));
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task AcceleratedVirtualTimeDoesNotChangeAlarmDecision()
    {
        var clock = new ManualSimulationClock();
        var sink = new FakeSink();
        var monitor = new UnattendedCurrentMonitor(clock, ElectricCurrent.FromAmperes(2), sink);
        clock.AdvanceBy(TimeSpan.FromHours(5));
        var alarm = await monitor.ObserveAsync("RUN-3", ElectricCurrent.FromAmperes(2.5));
        Assert.Equal(TimeSpan.FromHours(5), alarm!.SimulatedElapsed);
    }

    [Fact]
    public async Task ConcurrentObservationsDoNotDuplicateAlarm()
    {
        var sink = new FakeSink();
        var monitor = new UnattendedCurrentMonitor(new ManualSimulationClock(),
            ElectricCurrent.FromAmperes(2), sink);
        await Task.WhenAll(Enumerable.Range(0, 30).Select(i =>
            monitor.ObserveAsync("RUN-4", ElectricCurrent.FromAmperes(2.5)).AsTask()));
        Assert.Single(sink.Events);
    }
}
