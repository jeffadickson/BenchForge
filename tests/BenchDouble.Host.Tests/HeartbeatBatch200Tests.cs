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
}
