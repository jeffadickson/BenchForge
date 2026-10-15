using BenchDouble.Host.Contracts;
using Xunit;

namespace BenchDouble.Host.Tests;

public sealed class HostReservationTests
{
    [Fact]
    public void ASecondClientCannotReserveSameInstrument()
    {
        var host = new HostResourceRegistry();
        var first = host.Reserve("DMM1");
        Assert.NotNull(first);
        Assert.Null(host.Reserve("DMM1"));
        Assert.NotNull(host.Reserve("FGEN1"));
        Assert.True(host.Release(first.LeaseId));
        Assert.NotNull(host.Reserve("DMM1"));
    }

    [Fact]
    public void ALeaseCannotAccessAnUnownedInstrument()
    {
        var host = new HostResourceRegistry();
        var grant = host.Reserve("DMM1")!;
        Assert.Throws<UnauthorizedAccessException>(() => host.Query("FGEN1", grant.LeaseId, "*IDN?"));
        Assert.Contains("34461A", host.Query("DMM1", grant.LeaseId, "*IDN?"));
        Assert.True(host.Release(grant.LeaseId));
        Assert.Throws<UnauthorizedAccessException>(() => host.Query("DMM1", grant.LeaseId, "*IDN?"));
    }
}
