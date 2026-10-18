using BenchDouble.Host.Server.Setup;
using BenchDouble.Host.Server.Heartbeat;
using BenchDouble.Ivi;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Saved role policy integration with canonical ownership and cached health.</summary>
public sealed partial class HostShutdownTests
{
    /// <summary>Default status uses the saved policy and exact stale boundary without extra device I/O.</summary>
    [Fact] public void SavedFreshnessIsPinnedAndApplied()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock,freshness:1000);var active=f.Activate();Assert.Equal("Missing",f.Service.Status(f.Id,active.Lease.LeaseId).ReadingHealth!.State);
        f.Service.Measure(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromSeconds(1));var status=f.Service.Status(f.Id,active.Lease.LeaseId);
        Assert.Equal(1000,status.ReadingFreshnessMilliseconds);Assert.Equal(1000,status.ReadingHealth!.MaximumAgeMilliseconds);Assert.Equal("Stale",status.ReadingHealth.State);Assert.Equal(1,f.MeasureCalls);
    }
    /// <summary>Observer overrides do not rewrite the pinned role policy.</summary>
    [Fact] public void SavedFreshnessOverridesAreIndependent()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock,freshness:1000);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromSeconds(2));
        var overridden=f.Service.Status(f.Id,active.Lease.LeaseId,3000);Assert.Equal("Fresh",overridden.ReadingHealth!.State);Assert.Equal(1000,overridden.ReadingFreshnessMilliseconds);Assert.Equal("Stale",f.Service.Status(f.Id,active.Lease.LeaseId).ReadingHealth!.State);
    }
    /// <summary>Later saves affect new activations while existing owners retain the reviewed revision and policy.</summary>
    [Fact] public void SavedFreshnessChangesOnlyFutureActivations()
    {
        using var f=new Fixture(freshness:1000);var active=f.Activate();var saved=f.Setup.Read()!;var updated=f.Setup.Save(new(saved.Configuration with{Roles=[new("meter",f.Id,9000)]},saved.Revision,true));
        var pinned=f.Service.Status(f.Id,active.Lease.LeaseId);Assert.Equal(1000,pinned.ReadingFreshnessMilliseconds);Assert.Equal(saved.Revision,pinned.Revision);
        Assert.True(f.Registry.Release(active.Lease.LeaseId));var next=f.Service.Activate("meter",updated.Revision,true)!;Assert.Equal(9000,f.Service.Status(f.Id,next.Lease.LeaseId).ReadingFreshnessMilliseconds);Assert.Equal(updated.Revision,f.Service.Status(f.Id,next.Lease.LeaseId).Revision);
    }
    /// <summary>Policy and stale data remain observable when the device fails but owner contact stays healthy.</summary>
    [Fact] public void SavedFreshnessSurvivesDeviceFaults()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock,freshness:1000,heartbeat:HostHeartbeatPolicy.Default);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromSeconds(2));f.OnMeasure=()=>throw new IOException();
        Assert.Throws<InvalidOperationException>(()=>f.Service.Measure(f.Id,active.Lease.LeaseId));var status=f.Service.Status(f.Id,active.Lease.LeaseId);Assert.Equal("Healthy",status.Heartbeat!.State);Assert.Equal("RecoveryRequired",status.State);Assert.Equal("Stale",status.ReadingHealth!.State);Assert.Equal(1000,status.ReadingFreshnessMilliseconds);
    }
    /// <summary>Role-specific policies share the existing exclusive instrument authority.</summary>
    [Fact] public void SavedFreshnessDoesNotCreateAnotherLeaseAuthority()
    {
        using var f=new Fixture();var saved=f.Setup.Read()!;var updated=f.Setup.Save(new(saved.Configuration with{Roles=[new("fast",f.Id,100),new("slow",f.Id,10000)]},saved.Revision,true));
        var fast=f.Service.Activate("fast",updated.Revision,true)!;Assert.Null(f.Service.Activate("slow",updated.Revision,true));Assert.Equal(100,f.Service.Status(f.Id,fast.Lease.LeaseId).ReadingFreshnessMilliseconds);Assert.Equal(1,f.OpenCalls);
        f.Registry.Release(fast.Lease.LeaseId);var slow=f.Service.Activate("slow",updated.Revision,true)!;Assert.Equal(10000,f.Service.Status(f.Id,slow.Lease.LeaseId).ReadingFreshnessMilliseconds);
    }
    /// <summary>Concurrent default and override observers cannot mutate the saved policy.</summary>
    [Fact] public void SavedFreshnessConcurrentObserversRemainIndependent()
    {
        var clock=new ManualClock();using var f=new Fixture(clock:clock,freshness:1000);var active=f.Activate();f.Service.Measure(f.Id,active.Lease.LeaseId);clock.Advance(TimeSpan.FromSeconds(2));
        Parallel.For(0,100,i=>{var status=f.Service.Status(f.Id,active.Lease.LeaseId,i%2==0?null:3000);Assert.Equal(1000,status.ReadingFreshnessMilliseconds);Assert.Equal(i%2==0?"Stale":"Fresh",status.ReadingHealth!.State);});Assert.Equal(1,f.MeasureCalls);
    }
    /// <summary>An invalid externally edited saved policy cannot open or acquire an instrument.</summary>
    [Fact] public void SavedFreshnessInvalidConfigurationCannotOpenProvider()
    {
        using var f=new Fixture();var saved=f.Setup.Read()!;Assert.Throws<ArgumentException>(()=>f.Setup.Save(new(saved.Configuration with{Roles=[new("meter",f.Id,-1)]},saved.Revision,true)));Assert.Equal(0,f.OpenCalls);Assert.NotNull(f.Registry.Reserve(f.Id));
    }
}
