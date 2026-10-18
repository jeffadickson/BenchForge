using BenchDouble.Host.Server.Heartbeat;
using BenchDouble.Host.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace BenchDouble.Host.Tests;
/// <summary>Deterministic heartbeat clock, configuration and ownership regressions for batch 200.</summary>
public sealed class HeartbeatBatch200Tests
{
    private sealed class Clock : TimeProvider
    {
        internal long Ticks;
        internal long Frequency=TimeSpan.TicksPerSecond;
        internal int Reads;
        internal bool Fail;
        internal DateTimeOffset Utc=DateTimeOffset.UnixEpoch;
        /// <inheritdoc/>
        public override long TimestampFrequency=>Frequency;
        /// <inheritdoc/>
        public override long GetTimestamp(){++Reads;if(Fail)throw new IOException("clock");return Ticks;}
        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()=>Utc;
        internal void At(double milliseconds)=>Ticks=TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }
    private static HostHeartbeatPolicy Policy()=>new(TimeSpan.FromMilliseconds(10),TimeSpan.FromMilliseconds(20),TimeSpan.FromMilliseconds(40));
    private static HostResourceRegistry Registry(Clock c)=>new(heartbeatPolicy:Policy(),timeProvider:c);
    private static IConfiguration Config(params (string Key,string? Value)[] values)=>new ConfigurationBuilder().AddInMemoryCollection(values.Select(p=>new KeyValuePair<string,string?>(p.Key,p.Value))).Build();

    /// <summary>Null heartbeat configuration fails explicitly.</summary>
    [Fact] public void NullConfiguration()
    {
        Assert.Throws<ArgumentNullException>(()=>HostHeartbeatPolicy.FromConfiguration(null!));
    }

    /// <summary>Misspelled heartbeat settings cannot silently select defaults.</summary>
    [Fact] public void UnknownConfiguration()
    {
        Assert.Throws<ArgumentException>(()=>HostHeartbeatPolicy.FromConfiguration(Config(("BenchHeartbeat:IntervallMilliseconds","10"))));
    }

    /// <summary>Nested renewal interval settings are rejected.</summary>
    [Fact] public void NestedInterval()
    {
        Assert.Throws<ArgumentException>(()=>HostHeartbeatPolicy.FromConfiguration(Config(("BenchHeartbeat:IntervalMilliseconds:value","10"))));
    }

    /// <summary>Nested suspect budgets are rejected.</summary>
    [Fact] public void NestedSuspect()
    {
        Assert.Throws<ArgumentException>(()=>HostHeartbeatPolicy.FromConfiguration(Config(("BenchHeartbeat:SuspectMilliseconds:value","20"))));
    }

    /// <summary>Nested expiry budgets are rejected.</summary>
    [Fact] public void NestedExpiry()
    {
        Assert.Throws<ArgumentException>(()=>HostHeartbeatPolicy.FromConfiguration(Config(("BenchHeartbeat:ExpireMilliseconds:value","40"))));
    }

    /// <summary>Absent heartbeat settings retain finite defaults.</summary>
    [Fact] public void DefaultConfiguration()
    {
        Assert.Equal(HostHeartbeatPolicy.Default,HostHeartbeatPolicy.FromConfiguration(Config()));
    }

    /// <summary>Explicit scalar heartbeat budgets survive configuration.</summary>
    [Fact] public void ExplicitConfiguration()
    {
        Assert.Equal(Policy(),HostHeartbeatPolicy.FromConfiguration(Config(("BenchHeartbeat:IntervalMilliseconds","10"),("BenchHeartbeat:SuspectMilliseconds","20"),("BenchHeartbeat:ExpireMilliseconds","40"))));
    }

    /// <summary>Unrelated application settings do not affect heartbeat policy.</summary>
    [Fact] public void UnrelatedConfiguration()
    {
        Assert.Equal(HostHeartbeatPolicy.Default,HostHeartbeatPolicy.FromConfiguration(Config(("Other:Value","anything"))));
    }

    /// <summary>Noninteger heartbeat budgets prevent startup.</summary>
    [Fact] public void MalformedConfiguration()
    {
        Assert.Throws<ArgumentException>(()=>HostHeartbeatPolicy.FromConfiguration(Config(("BenchHeartbeat:IntervalMilliseconds","10.5"))));
    }

    /// <summary>Heartbeat clocks require positive timestamp frequency.</summary>
    [Fact] public void ZeroFrequency()
    {
        var c=new Clock{Frequency=0}; Assert.Throws<ArgumentOutOfRangeException>(()=>Registry(c));
    }

    /// <summary>Negative timestamp frequency prevents owner service startup.</summary>
    [Fact] public void NegativeFrequency()
    {
        var c=new Clock{Frequency=-1}; Assert.Throws<ArgumentOutOfRangeException>(()=>Registry(c));
    }

    /// <summary>Disabled heartbeat enforcement retains legacy provider compatibility.</summary>
    [Fact] public void LegacyFrequency()
    {
        var c=new Clock{Frequency=0}; var r=new HostResourceRegistry(timeProvider:c); var g=r.Reserve("DMM1")!; Assert.Null(r.InspectOwnerHeartbeat("DMM1",g.LeaseId));
    }

    /// <summary>New ownership begins healthy at zero age.</summary>
    [Fact] public void InitialAge()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;Assert.Equal("Healthy",s.State);Assert.Equal(0,s.AgeMilliseconds);
    }

    /// <summary>The final tick before suspect remains healthy.</summary>
    [Fact] public void BeforeSuspect()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;c.Ticks=TimeSpan.FromMilliseconds(20).Ticks-1;Assert.Equal("Healthy",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);
    }

    /// <summary>The exact suspect deadline is inclusive.</summary>
    [Fact] public void ExactSuspect()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;c.At(20);Assert.Equal("Suspect",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);
    }

    /// <summary>The final tick before expiry remains suspect.</summary>
    [Fact] public void BeforeExpiry()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;c.Ticks=TimeSpan.FromMilliseconds(40).Ticks-1;Assert.Equal("Suspect",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);
    }

    /// <summary>The exact expiry deadline permanently fences ownership.</summary>
    [Fact] public void ExactExpiry()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;c.At(40);Assert.Equal("Expired",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);Assert.Null(r.Reserve("DMM1"));
    }

    /// <summary>Time before the grant timestamp fences the retained owner.</summary>
    [Fact] public void BeforeGrantRollback()
    {
        var c=new Clock();c.At(10);var r=Registry(c);var g=r.Reserve("DMM1")!;c.At(9);Assert.Equal("Expired",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);Assert.Null(r.Reserve("DMM1"));
    }

    /// <summary>Rollback after an observation cannot restore healthy status.</summary>
    [Fact] public void ObservationRollback()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;c.At(25);Assert.Equal("Suspect",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);c.At(10);Assert.Equal("Expired",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);
    }

    /// <summary>Clock recovery cannot unfence an expired owner.</summary>
    [Fact] public void RollbackIrreversible()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;c.At(25);_=r.InspectOwnerHeartbeat("DMM1",g.LeaseId);c.At(10);_=r.InspectOwnerHeartbeat("DMM1",g.LeaseId);c.At(26);Assert.Equal("Expired",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);
    }

    /// <summary>One health observation samples the monotonic clock once.</summary>
    [Fact] public void SingleObservationRead()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var reads=c.Reads;_=r.InspectOwnerHeartbeat("DMM1",g.LeaseId);Assert.Equal(reads+1,c.Reads);
    }

    /// <summary>Renewal reuses one validated monotonic observation.</summary>
    [Fact] public void SingleRenewalRead()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;var reads=c.Reads;var renewed=r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.NewGuid(),1);Assert.Equal(reads+1,c.Reads);Assert.Equal(0,renewed.AgeMilliseconds);
    }

    /// <summary>Later observations cannot change a prior snapshot age.</summary>
    [Fact] public void DetachedAge()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;c.At(5);var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;c.At(10);_=r.InspectOwnerHeartbeat("DMM1",g.LeaseId);Assert.Equal(5,s.AgeMilliseconds);
    }

    /// <summary>UTC jumps forward do not affect monotonic heartbeat age.</summary>
    [Fact] public void WallClockForward()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;c.Utc=c.Utc.AddYears(10);Assert.Equal(0,r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.AgeMilliseconds);
    }

    /// <summary>UTC jumps backward do not renew or expire ownership.</summary>
    [Fact] public void WallClockBackward()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;c.At(25);c.Utc=c.Utc.AddYears(-10);Assert.Equal("Suspect",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);
    }

    /// <summary>Valid renewal restores suspect health with the same owner generation.</summary>
    [Fact] public void RenewalRestoresSuspect()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;c.At(25);var renewed=r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.NewGuid(),1);Assert.Equal("Healthy",renewed.State);Assert.Equal(s.OwnerGeneration,renewed.OwnerGeneration);Assert.Equal(0,renewed.AgeMilliseconds);
    }

    /// <summary>Expiry cannot be reversed by a correctly identified renewal.</summary>
    [Fact] public void ExpiredRenewal()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;c.At(40);Assert.Throws<InvalidOperationException>(()=>r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.NewGuid(),1));Assert.Null(r.Reserve("DMM1"));
    }

    /// <summary>Renewal cannot conceal a clock rollback.</summary>
    [Fact] public void RollbackRenewal()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;c.At(10);_=r.InspectOwnerHeartbeat("DMM1",g.LeaseId);c.At(5);Assert.Throws<InvalidOperationException>(()=>r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.NewGuid(),1));
    }

    /// <summary>Duplicate sequences never extend the accepted deadline.</summary>
    [Fact] public void DuplicateRenewal()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;var peer=Guid.NewGuid();_=r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,peer,1);c.At(10);Assert.Throws<InvalidOperationException>(()=>r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,peer,1));Assert.Equal(10,r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.AgeMilliseconds);
    }

    /// <summary>Stale host epochs cannot renew a current owner.</summary>
    [Fact] public void StaleEpoch()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;Assert.Throws<UnauthorizedAccessException>(()=>r.RenewOwnerHeartbeat("DMM1",g.LeaseId,Guid.NewGuid(),s.OwnerGeneration,Guid.NewGuid(),1));Assert.Equal(0,r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.Sequence);
    }

    /// <summary>Stale generations cannot renew current ownership.</summary>
    [Fact] public void StaleGeneration()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;Assert.Throws<UnauthorizedAccessException>(()=>r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration+1,Guid.NewGuid(),1));
    }

    /// <summary>Changed client incarnation cannot extend the current lease.</summary>
    [Fact] public void ChangedPeer()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;_=r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.NewGuid(),1);Assert.Throws<InvalidOperationException>(()=>r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.NewGuid(),2));
    }

    /// <summary>Empty peer epochs cannot establish heartbeat ownership.</summary>
    [Fact] public void EmptyPeer()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;Assert.Throws<InvalidOperationException>(()=>r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.Empty,1));
    }

    /// <summary>Zero sequences cannot establish heartbeat ownership.</summary>
    [Fact] public void ZeroSequence()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;var s=r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!;Assert.Throws<InvalidOperationException>(()=>r.RenewOwnerHeartbeat("DMM1",g.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.NewGuid(),0));
    }

    /// <summary>Repeated observer reads do not move the owner deadline.</summary>
    [Fact] public void ObserverNoRenewal()
    {
        var c=new Clock();var r=Registry(c);var g=r.Reserve("DMM1")!;for(var i=0;i<40;i++){c.At(i);_=r.InspectOwnerHeartbeat("DMM1",g.LeaseId);}c.At(40);Assert.Equal("Expired",r.InspectOwnerHeartbeat("DMM1",g.LeaseId)!.State);
    }

    /// <summary>Renewal of one owner does not renew a different resource.</summary>
    [Fact] public void IndependentOwners()
    {
        var c=new Clock();var r=Registry(c);var a=r.Reserve("DMM1")!;var d=r.Reserve("FGEN1")!;var s=r.InspectOwnerHeartbeat("DMM1",a.LeaseId)!;c.At(25);_=r.RenewOwnerHeartbeat("DMM1",a.LeaseId,s.HostEpoch,s.OwnerGeneration,Guid.NewGuid(),1);c.At(40);Assert.Equal("Healthy",r.InspectOwnerHeartbeat("DMM1",a.LeaseId)!.State);Assert.Equal("Expired",r.InspectOwnerHeartbeat("FGEN1",d.LeaseId)!.State);
    }

    /// <summary>Clock failure before a grant leaves the resource available.</summary>
    [Fact] public void ClockGrantFailure()
    {
        var c=new Clock();var r=Registry(c);c.Fail=true;Assert.Throws<IOException>(()=>r.Reserve("DMM1"));c.Fail=false;Assert.NotNull(r.Reserve("DMM1"));
    }
}
