using BenchDouble.Host.Server.Heartbeat;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Cached freshness integration through the canonical host lifecycle fixture.</summary>
public sealed partial class HostShutdownTests
{
    /// <summary>Status reports missing data without dispatching a measurement.</summary>
    [Fact] public void ReadingFreshnessMissingDoesNotQuery()
    {
        using var f=new Fixture();var active=f.Activate();var status=f.Service.Status(f.Id,active.Lease.LeaseId,5000);
        Assert.Equal("Missing",status.ReadingHealth!.State);Assert.Null(status.ReadingHealth.AgeMilliseconds);Assert.Equal(0,f.MeasureCalls);
    }

    /// <summary>Independent experiments can choose distinct policies for the same cached age.</summary>
    [Fact] public void ReadingFreshnessPoliciesDoNotMutateSharedState()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("Stale",f.Service.Status(f.Id,active.Lease.LeaseId,1000).ReadingHealth!.State);
        Assert.Equal("Fresh",f.Service.Status(f.Id,active.Lease.LeaseId,3000).ReadingHealth!.State);
        Assert.Equal("Unspecified",f.Service.Status(f.Id,active.Lease.LeaseId).ReadingHealth!.State);Assert.Equal(1,f.MeasureCalls);
    }

    /// <summary>The threshold boundary is stale on a live session without another reading.</summary>
    [Fact] public void ReadingFreshnessBoundaryIsMonotonic()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal("Fresh",f.Service.Status(f.Id,active.Lease.LeaseId,1000).ReadingHealth!.State);clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("Stale",f.Service.Status(f.Id,active.Lease.LeaseId,1000).ReadingHealth!.State);Assert.Equal(1,f.MeasureCalls);
    }

    /// <summary>UTC changes do not rejuvenate stale cached data.</summary>
    [Fact] public void ReadingFreshnessIgnoresUtcJumps()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromSeconds(2));clock.WallClock=clock.WallClock.AddYears(-10);
        var health=f.Service.Status(f.Id,active.Lease.LeaseId,1000).ReadingHealth!;Assert.Equal("Stale",health.State);Assert.Equal(2000,health.AgeMilliseconds);
    }

    /// <summary>A failed measurement preserves the prior reading while owner contact remains independent.</summary>
    [Fact] public void ReadingFreshnessFaultPreservesEvidence()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock,heartbeat:HostHeartbeatPolicy.Default);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);
        var before=f.Service.Status(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromSeconds(2));f.OnMeasure=()=>throw new IOException();Assert.Throws<InvalidOperationException>(()=>f.Service.Measure(f.Id,active.Lease.LeaseId));
        var after=f.Service.Status(f.Id,active.Lease.LeaseId,1000);Assert.Equal("RecoveryRequired",after.State);Assert.Equal("Healthy",after.Heartbeat!.State);Assert.Equal("Stale",after.ReadingHealth!.State);Assert.Equal(before.LastReadingUtc,after.LastReadingUtc);
    }

    /// <summary>A new successful measurement resets age without changing the observer's policy.</summary>
    [Fact] public void ReadingFreshnessNewReadingResetsAge()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromSeconds(2));
        var old=f.Service.Status(f.Id,active.Lease.LeaseId,1000);clock.WallClock=clock.WallClock.AddSeconds(2);f.Service.Measure(f.Id,active.Lease.LeaseId);var current=f.Service.Status(f.Id,active.Lease.LeaseId,1000);
        Assert.Equal("Stale",old.ReadingHealth!.State);Assert.Equal("Fresh",current.ReadingHealth!.State);Assert.Equal(0,current.ReadingHealth.AgeMilliseconds);Assert.NotEqual(old.LastReadingUtc,current.LastReadingUtc);
    }

    /// <summary>Repeated observation never renews the owner's heartbeat or permits expired dispatch.</summary>
    [Fact] public void ReadingFreshnessInspectionCannotRenewOwner()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock,heartbeat:HostHeartbeatPolicy.Default);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);
        clock.Advance(TimeSpan.FromSeconds(30));var status=f.Service.Status(f.Id,active.Lease.LeaseId,1000);Assert.Equal("OwnerExpired",status.State);Assert.Equal("Stale",status.ReadingHealth!.State);
        Assert.Throws<InvalidOperationException>(()=>f.Service.Measure(f.Id,active.Lease.LeaseId));Assert.Equal(1,f.MeasureCalls);Assert.Single(f.Journal!.Read());
    }

    /// <summary>Invalid policies fail without device dispatch.</summary>
    /// <param name="limit">Unsupported policy.</param>
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(86400001)]
    public void ReadingFreshnessInvalidPolicyDoesNotDispatch(double limit)
    {
        using var f=new Fixture();var active=f.Activate();Assert.Throws<ArgumentOutOfRangeException>(()=>f.Service.Status(f.Id,active.Lease.LeaseId,limit));Assert.Equal(0,f.MeasureCalls);
    }

    /// <summary>A freshness policy cannot bypass canonical capability authentication.</summary>
    [Fact] public void ReadingFreshnessCannotBypassLease()
    {
        using var f=new Fixture();f.Activate();Assert.Throws<UnauthorizedAccessException>(()=>f.Service.Status(f.Id,Guid.NewGuid(),1000));Assert.Equal(0,f.MeasureCalls);
    }

    /// <summary>Observers can assess detached status concurrently without measurement or policy races.</summary>
    [Fact] public void ReadingFreshnessParallelObserversAreIndependent()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromSeconds(2));
        Parallel.For(0,100,i=>Assert.Equal(i%2==0?"Stale":"Fresh",f.Service.Status(f.Id,active.Lease.LeaseId,i%2==0?1000:3000).ReadingHealth!.State));Assert.Equal(1,f.MeasureCalls);
    }

    /// <summary>Inspection during drain still reports cached data and does not grant dispatch.</summary>
    [Fact] public void ReadingFreshnessRemainsReadableDuringDrain()
    {
        using var f=new Fixture();var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);f.Registry.BeginDrain();
        Assert.Equal("Fresh",f.Service.Status(f.Id,active.Lease.LeaseId,86400000).ReadingHealth!.State);Assert.Equal(1,f.MeasureCalls);
    }
}
