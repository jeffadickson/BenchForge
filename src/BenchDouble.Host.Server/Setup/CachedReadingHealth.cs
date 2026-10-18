namespace BenchDouble.Host.Server.Setup;

/// <summary>Detached cached-data assessment, independent of device readiness and owner responsiveness.</summary>
/// <param name="State">Missing, Unspecified, Fresh or Stale; none certifies physical safety.</param>
/// <param name="AgeMilliseconds">Monotonic age, or null before the first successful reading.</param>
/// <param name="MaximumAgeMilliseconds">Caller-selected freshness budget, or null when no policy was supplied.</param>
public sealed record CachedReadingHealth(string State, double? AgeMilliseconds, double? MaximumAgeMilliseconds);

/// <summary>Pure per-observer freshness policy; never queries hardware, renews ownership or modifies shared state.</summary>
public static class ReadingFreshness
{
    /// <summary>Validate an optional caller budget without dispatching any device operation.</summary>
    /// <param name="maximumAgeMilliseconds">Finite budget from one millisecond through one day; null leaves freshness unspecified.</param>
    /// <exception cref="ArgumentOutOfRangeException">The supplied budget is outside the supported finite interval.</exception>
    public static void Validate(double? maximumAgeMilliseconds)
    {
        if (maximumAgeMilliseconds is { } limit && (!double.IsFinite(limit) || limit < 1 || limit > 86400000))
            throw new ArgumentOutOfRangeException(nameof(maximumAgeMilliseconds), "Reading freshness must be between one millisecond and one day.");
    }

    /// <summary>Assess a detached monotonic age against the caller's policy; the exact boundary is stale.</summary>
    /// <param name="ageMilliseconds">Finite nonnegative cached age, or null when no reading exists.</param>
    /// <param name="maximumAgeMilliseconds">Optional freshness budget validated independently for each observer.</param>
    /// <returns>Immutable assessment; missing data never appears fresh, and absent policy never implies freshness.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Age or freshness budget is invalid.</exception>
    public static CachedReadingHealth Assess(double? ageMilliseconds, double? maximumAgeMilliseconds)
    {
        Validate(maximumAgeMilliseconds);
        if (ageMilliseconds is { } age && (!double.IsFinite(age) || age < 0)) throw new ArgumentOutOfRangeException(nameof(ageMilliseconds));
        var state = ageMilliseconds is null ? "Missing" : maximumAgeMilliseconds is null ? "Unspecified"
            : ageMilliseconds >= maximumAgeMilliseconds ? "Stale" : "Fresh";
        return new(state, ageMilliseconds, maximumAgeMilliseconds);
    }
}
