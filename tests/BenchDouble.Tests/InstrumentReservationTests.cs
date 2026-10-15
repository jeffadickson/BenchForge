using BenchDouble.Async;
using Xunit;

namespace BenchDouble.Tests;

public class InstrumentReservationTests
{
    [Fact]
    public async Task SameResourceIsExclusiveEvenAcrossCallers()
    {
        var reservations = new InstrumentReservationManager();
        await using var first = await reservations.ReserveAsync("TCPIP0::DMM1::INSTR");
        var waiting = reservations.ReserveAsync("tcpip0::dmm1::instr").AsTask();
        Assert.False(waiting.IsCompleted);
        await first.DisposeAsync();
        await using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("tcpip0::dmm1::instr", second.Resource);
    }

    [Fact]
    public async Task DifferentResourcesCanBeUsedTogether()
    {
        var reservations = new InstrumentReservationManager();
        await using var dmm = await reservations.ReserveAsync("DMM1");
        await using var generator = await reservations.ReserveAsync("FGEN1");
        Assert.Equal("DMM1", dmm.Resource);
        Assert.Equal("FGEN1", generator.Resource);
    }

    [Fact]
    public async Task CancellationRemovesAWaitingRequestWithoutStealingLease()
    {
        var reservations = new InstrumentReservationManager();
        await using var first = await reservations.ReserveAsync("DMM1");
        using var cts = new CancellationTokenSource();
        var blocked = reservations.ReserveAsync("DMM1", cts.Token).AsTask();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        await first.DisposeAsync();
        await using var next = await reservations.ReserveAsync("DMM1");
        Assert.Equal("DMM1", next.Resource);
    }

    [Fact]
    public async Task DoubleDisposalNeverUnlocksAnotherLease()
    {
        var reservations = new InstrumentReservationManager();
        var first = await reservations.ReserveAsync("DMM1");
        first.Dispose();
        await using var second = await reservations.ReserveAsync("DMM1");
        first.Dispose();
        var third = reservations.ReserveAsync("DMM1").AsTask();
        Assert.False(third.IsCompleted);
        await second.DisposeAsync();
        await using var acquired = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
