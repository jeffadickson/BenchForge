using BenchDouble.Host.Server.Setup;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Deterministic per-observer cached-reading freshness policy regressions.</summary>
public sealed class ReadingFreshnessTests
{
    /// <summary>Missing samples never become fresh, even without a requested policy.</summary>
    /// <param name="limit">Optional observer freshness limit.</param>
    [Theory] [InlineData(null)] [InlineData(1)] [InlineData(5000)] [InlineData(86400000)]
    public void MissingReadingsAreExplicit(double? limit) => Assert.Equal(new CachedReadingHealth("Missing",null,limit),ReadingFreshness.Assess(null,limit));

    /// <summary>An omitted budget does not imply that an existing sample is fresh.</summary>
    [Fact] public void OmittedPolicyIsUnspecified() => Assert.Equal("Unspecified",ReadingFreshness.Assess(0,null).State);

    /// <summary>The stale boundary is inclusive.</summary>
    /// <param name="age">Observed age.</param>
    /// <param name="expected">Expected freshness state.</param>
    [Theory] [InlineData(0,"Fresh")] [InlineData(4999.999,"Fresh")] [InlineData(5000,"Stale")] [InlineData(5000.001,"Stale")]
    public void BoundaryIsInclusive(double age,string expected) => Assert.Equal(expected,ReadingFreshness.Assess(age,5000).State);
}
