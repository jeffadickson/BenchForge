using Xunit;

namespace BenchDouble.Host.Tests;

public sealed class HostDashboardTests
{
    [Fact]
    public void DashboardShowsRegisteredInstrumentsAndReservationState()
    {
        var registry = new HostResourceRegistry();
        var before = registry.Monitor();
        Assert.Contains(before.Instruments, x => x.ResourceId == "DMM1" && x.Status == "Idle");
        Assert.Contains(before.Instruments, x => x.ResourceId == "FGEN1");
        var lease = registry.Reserve("DMM1");
        Assert.NotNull(lease);
        var reserved = registry.Monitor().Instruments.Single(x => x.ResourceId == "DMM1");
        Assert.Equal("Reserved", reserved.Status);
        Assert.Contains(reserved.Activity, a => a.Action == "Reserved");
        Assert.True(registry.Release(lease!.LeaseId));
        Assert.Equal("Idle", registry.Monitor().Instruments.Single(x => x.ResourceId == "DMM1").Status);
    }

    [Fact]
    public void DashboardDoesNotExposeRawCommands()
    {
        var registry = new HostResourceRegistry();
        var lease = registry.Reserve("DMM1")!;
        registry.Query("DMM1", lease.LeaseId, "MEAS:VOLT:DC?");
        var events = registry.Monitor().Instruments.Single(x => x.ResourceId == "DMM1").Activity;
        Assert.Contains(events, x => x.Action == "Query");
        Assert.DoesNotContain(events, x => x.Message.Contains("MEAS:", StringComparison.Ordinal));
    }
}
