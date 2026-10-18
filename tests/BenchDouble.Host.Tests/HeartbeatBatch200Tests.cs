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
}
