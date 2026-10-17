namespace BenchDouble.Host.Server.Lifecycle;

/// <summary>Host-owned shutdown limits; independent of request/run cancellation.</summary>
/// <param name="Timeout">Maximum coordinator wait, from 10 milliseconds through 60 seconds; does not abort synchronous drivers.</param>
/// <param name="MaxConcurrency">Maximum concurrent cleanup workers, from 1 through 16.</param>
public sealed record HostShutdownPolicy(TimeSpan Timeout, int MaxConcurrency = 4)
{
    /// <summary>Read strict host configuration; omitted settings use 5000 milliseconds and four workers.</summary>
    /// <param name="configuration">Host configuration containing BenchShutdown:TimeoutMilliseconds and MaxConcurrency.</param>
    /// <returns>Validated finite policy; malformed values prevent startup.</returns>
    /// <exception cref="ArgumentException">A supplied setting is not an unsigned decimal integer.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parsed limit is outside supported bounds.</exception>
    public static HostShutdownPolicy FromConfiguration(IConfiguration configuration)
    {
        int Read(string key, int fallback)
        {
            var value = configuration["BenchShutdown:" + key];
            if (value is null) return fallback;
            if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                throw new ArgumentException("Invalid host shutdown setting: " + key);
            return parsed;
        }
        var policy = new HostShutdownPolicy(TimeSpan.FromMilliseconds(Read("TimeoutMilliseconds", 5000)), Read("MaxConcurrency", 4));
        policy.Validate(); return policy;
    }

    /// <summary>Reject unbounded deadlines and invalid worker budgets before stopping admission.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A limit is outside its supported range.</exception>
    public void Validate()
    {
        if (Timeout < TimeSpan.FromMilliseconds(10) || Timeout > TimeSpan.FromSeconds(60)) throw new ArgumentOutOfRangeException(nameof(Timeout));
        if (MaxConcurrency is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(MaxConcurrency));
    }
}

/// <summary>Payload-free outcome for one canonical resource at the end of the shutdown wait.</summary>
/// <param name="ResourceId">Canonical resource ID; no lease capability.</param>
/// <param name="Outcome">Closed, RecoveryRequired, ProviderMissing, DeadlineExceeded or StartupQuarantine.</param>
public sealed record HostShutdownItem(string ResourceId, string Outcome);

/// <summary>Immutable coordinator result; completion does not imply all hardware is safe.</summary>
/// <param name="StartedUtc">Host wall-clock time at shutdown start.</param>
/// <param name="ElapsedMilliseconds">Monotonic elapsed coordinator time in milliseconds.</param>
/// <param name="Items">Detached per-resource results; timed-out work may still be running.</param>
public sealed record HostShutdownReport(DateTimeOffset StartedUtc, double ElapsedMilliseconds, IReadOnlyList<HostShutdownItem> Items);

/// <summary>Opt-in managed shutdown; called under the canonical lease gate before close.</summary>
public interface IManagedShutdownResource : IManagedHostResource
{
    /// <summary>Whether a qualified device-specific shutdown procedure is installed.</summary>
    bool SupportsShutdown { get; }
    /// <summary>Abort/disable and confirm safe state using a qualified provider; failure retains ownership.</summary>
    void PrepareForShutdown();
}
