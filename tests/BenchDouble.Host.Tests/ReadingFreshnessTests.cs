using BenchDouble.Host.Server.Setup;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Deterministic per-observer cached-reading freshness policy regressions.</summary>
public sealed class ReadingFreshnessTests
{
    /// <summary>Missing samples never become fresh, even without a requested policy.</summary>
    /// <param name="limit">Optional observer freshness limit.</param>
    [Theory] [InlineData(null)] [InlineData(1.0)] [InlineData(5000.0)] [InlineData(86400000.0)]
    public void MissingReadingsAreExplicit(double? limit) => Assert.Equal(new CachedReadingHealth("Missing",null,limit),ReadingFreshness.Assess(null,limit));

    /// <summary>An omitted budget does not imply that an existing sample is fresh.</summary>
    [Fact] public void OmittedPolicyIsUnspecified() => Assert.Equal("Unspecified",ReadingFreshness.Assess(0,null).State);

    /// <summary>The stale boundary is inclusive.</summary>
    /// <param name="age">Observed age.</param>
    /// <param name="expected">Expected freshness state.</param>
    [Theory] [InlineData(0,"Fresh")] [InlineData(4999.999,"Fresh")] [InlineData(5000,"Stale")] [InlineData(5000.001,"Stale")]
    public void BoundaryIsInclusive(double age,string expected) => Assert.Equal(expected,ReadingFreshness.Assess(age,5000).State);

    /// <summary>Invalid budgets fail even when no reading exists.</summary>
    /// <param name="limit">Rejected policy limit.</param>
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)] [InlineData(0)] [InlineData(-1)] [InlineData(0.999)] [InlineData(86400001)]
    public void InvalidBudgetsAreRejected(double limit) => Assert.Throws<ArgumentOutOfRangeException>(()=>ReadingFreshness.Assess(null,limit));

    /// <summary>Invalid observed ages cannot produce plausible health reports.</summary>
    /// <param name="age">Rejected cached age.</param>
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)] [InlineData(-1)]
    public void InvalidAgesAreRejected(double age) => Assert.Throws<ArgumentOutOfRangeException>(()=>ReadingFreshness.Assess(age,5000));

    /// <summary>Inclusive budget limits preserve exact values in detached snapshots.</summary>
    /// <param name="limit">Accepted boundary.</param>
    [Theory] [InlineData(1)] [InlineData(86400000)]
    public void BudgetBoundsAreInclusive(double limit) => Assert.Equal(new CachedReadingHealth("Fresh",0,limit),ReadingFreshness.Assess(0,limit));

    /// <summary>Observers can assess the same sample independently under parallel load.</summary>
    [Fact] public void ParallelPoliciesAreIndependent()
    {
        Parallel.For(0,2000,i=>{var limit=i%2==0?100:10000;var health=ReadingFreshness.Assess(500,limit);Assert.Equal(i%2==0?"Stale":"Fresh",health.State);Assert.Equal(limit,health.MaximumAgeMilliseconds);});
    }

    /// <summary>A later assessment cannot mutate an earlier health record.</summary>
    [Fact] public void HealthSnapshotsAreDetached()
    {
        var health=ReadingFreshness.Assess(10,20);_ = ReadingFreshness.Assess(30,20);Assert.Equal(new CachedReadingHealth("Fresh",10,20),health);
    }

    /// <summary>Legacy lease-only and confirmed-field requests remain supported for status inspection.</summary>
    [Fact] public void LegacyStatusRequestIsCompatible()
    {
        var lease=Guid.NewGuid();var request=BenchSetupService.ParseIviRequest<IviStatusRequest>(System.Text.Json.JsonSerializer.Serialize(new{leaseId=lease,confirmed=true}));
        Assert.Equal(lease,request.LeaseId);Assert.True(request.Confirmed);Assert.Null(request.ReadingFreshnessMilliseconds);
    }

    /// <summary>A caller budget survives strict JSON parsing without modifying its value.</summary>
    [Fact] public void StatusRequestCarriesBudget()
    {
        var request=BenchSetupService.ParseIviRequest<IviStatusRequest>(System.Text.Json.JsonSerializer.Serialize(new{leaseId=Guid.NewGuid(),readingFreshnessMilliseconds=1.25}));Assert.Equal(1.25,request.ReadingFreshnessMilliseconds);
    }

    /// <summary>Status request parsing rejects missing, duplicate and unknown properties.</summary>
    /// <param name="json">Malformed or unsupported payload.</param>
    [Theory] [InlineData("{}")] [InlineData("{\"leaseId\":\"00000000-0000-0000-0000-000000000001\",\"extra\":1}")]
    [InlineData("{\"leaseId\":\"00000000-0000-0000-0000-000000000001\",\"readingFreshnessMilliseconds\":1,\"readingFreshnessMilliseconds\":2}")]
    public void StatusRequestIsStrict(string json) => Assert.Throws<System.Text.Json.JsonException>(()=>BenchSetupService.ParseIviRequest<IviStatusRequest>(json));

    /// <summary>JSON does not leak internal monotonic timestamp fields.</summary>
    [Fact] public void HealthJsonIsDetachedFromInternalTimestamps()
    {
        var json=System.Text.Json.JsonSerializer.Serialize(new IviDmmSessionStatus("meter","revision","Ready",null){ReadingHealth=ReadingFreshness.Assess(10,20)});
        Assert.Contains("Fresh",json);Assert.DoesNotContain("ReadingTimestamp",json);
    }
}
