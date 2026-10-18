namespace BenchDouble.Host.Server.Heartbeat;

/// <summary>Finite monotonic owner-health budgets. Loss fences and retains ownership; it never proves physical safety.</summary>
/// <param name="Interval">Recommended renewal interval, 10 milliseconds through 60 seconds.</param>
/// <param name="SuspectAfter">Silence before Suspect, at least two intervals.</param>
/// <param name="ExpireAfter">Silence before permanent fencing, at least twice SuspectAfter and at most one hour.</param>
public sealed record HostHeartbeatPolicy(TimeSpan Interval, TimeSpan SuspectAfter, TimeSpan ExpireAfter)
{
    /// <summary>Default local-control budgets: renew every five seconds, suspect after fifteen, fence after thirty.</summary>
    public static HostHeartbeatPolicy Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));

    /// <summary>Read validated host settings; omitted values retain the default timing budgets.</summary>
    /// <param name="configuration">BenchHeartbeat interval/suspect/expiry settings in milliseconds.</param>
    /// <returns>Finite policy; malformed values prevent host startup.</returns>
    /// <exception cref="ArgumentException">A configured value is malformed or out of range.</exception>
    public static HostHeartbeatPolicy FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        TimeSpan Read(string key, TimeSpan fallback)
        {
            var value = configuration["BenchHeartbeat:" + key];
            if (value is null) return fallback;
            if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var milliseconds))
                throw new ArgumentException("Invalid heartbeat setting: " + key);
            return TimeSpan.FromMilliseconds(milliseconds);
        }
        var result = new HostHeartbeatPolicy(Read("IntervalMilliseconds", Default.Interval),
            Read("SuspectMilliseconds", Default.SuspectAfter), Read("ExpireMilliseconds", Default.ExpireAfter));
        result.Validate(); return result;
    }

    /// <summary>Reject nonfinite, inverted or excessively short health budgets.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A budget violates the documented limits.</exception>
    public void Validate()
    {
        if (Interval < TimeSpan.FromMilliseconds(10) || Interval > TimeSpan.FromSeconds(60) ||
            SuspectAfter > TimeSpan.FromMinutes(30) || ExpireAfter > TimeSpan.FromHours(1) ||
            SuspectAfter < Interval * 2 || ExpireAfter < SuspectAfter * 2 || ExpireAfter > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(Interval), "Invalid heartbeat timing budgets.");
    }
}
