namespace BenchDouble.Simulation;
/// <summary>Separate simulated latency phases; never physical deadline or ownership clocks.</summary>
public enum SimulationLatencyPhase
{
    /// <summary>Initialization or warmup.</summary>
    Initialization,
    /// <summary>Physical-model settling.</summary>
    Settling,
    /// <summary>Measurement integration.</summary>
    Integration,
    /// <summary>Trigger acceptance.</summary>
    Trigger,
    /// <summary>Acquisition duration.</summary>
    Acquisition,
    /// <summary>Result processing.</summary>
    Processing,
    /// <summary>Optional simulated display pacing.</summary>
    Display
}
/// <summary>Immutable logical phase timing, validated by a latency profile.</summary>
/// <param name="Phase">One declared latency phase.</param>
/// <param name="Duration">Nonnegative base logical duration.</param>
/// <param name="Jitter">Nonnegative symmetric jitter radius, no greater than Duration.</param>
public sealed record SimulationLatencyStage(SimulationLatencyPhase Phase, TimeSpan Duration, TimeSpan Jitter);

/// <summary>Immutable thread-safe logical latency configuration with index-addressed seeded jitter.</summary>
public sealed class SimulationLatencyProfile
{
    /// <summary>Run-selected signed seed; never shared random state.</summary>
    public int Seed { get; }
    /// <summary>Copied read-only phases in enum order.</summary>
    public IReadOnlyList<SimulationLatencyStage> Stages { get; }
    /// <summary>Construct at most seven uniquely named logical phases.</summary>
    /// <param name="seed">Run-selected deterministic seed.</param>
    /// <param name="stages">Copied phases; at most eight entries are enumerated.</param>
    /// <exception cref="ArgumentException">Phase metadata is invalid or duplicated.</exception>
    public SimulationLatencyProfile(int seed, IEnumerable<SimulationLatencyStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var copied=stages.Take(8).ToArray();
        if(copied.Length>7)throw new ArgumentException("At most seven latency phases are supported.",nameof(stages));
        foreach(var stage in copied)
        {
            ArgumentNullException.ThrowIfNull(stage);
            if(!Enum.IsDefined(stage.Phase))throw new ArgumentException("Unknown latency phase.",nameof(stages));
            if(stage.Duration<TimeSpan.Zero || stage.Duration>TimeSpan.FromDays(1))throw new ArgumentOutOfRangeException(nameof(stages),"Invalid phase duration.");
            if(stage.Jitter<TimeSpan.Zero || stage.Jitter>stage.Duration || stage.Duration.Ticks+stage.Jitter.Ticks>TimeSpan.TicksPerDay)throw new ArgumentOutOfRangeException(nameof(stages),"Invalid phase jitter.");
            // Stage validation.
        }
        if(copied.Select(s=>s.Phase).Distinct().Count()!=copied.Length)throw new ArgumentException("Duplicate latency phase.",nameof(stages));
        // Profile validation.
        Seed=seed;Stages=Array.AsReadOnly(copied.OrderBy(s=>s.Phase).ToArray());
    }
    // Profile members.
}
