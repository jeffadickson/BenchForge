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
        if(copied.Sum(s=>s.Duration.Ticks+s.Jitter.Ticks)>TimeSpan.TicksPerDay)throw new ArgumentOutOfRangeException(nameof(stages),"Aggregate latency exceeds one day.");
        // Profile validation.
        Seed=seed;Stages=Array.AsReadOnly(copied.OrderBy(s=>s.Phase).ToArray());
    }
    private long JitterTicks(long index, SimulationLatencyStage stage)
    {
        if(stage.Jitter.Ticks==0)return 0;
        ulong mixed;
        unchecked
        {
            mixed=(ulong)(long)Seed ^ (ulong)index*0x9E3779B97F4A7C15UL ^ ((ulong)stage.Phase+1)*0xD1B54A32D192ED03UL;
            mixed=(mixed^(mixed>>30))*0xBF58476D1CE4E5B9UL;
            mixed=(mixed^(mixed>>27))*0x94D049BB133111EBUL;
            mixed^=mixed>>31;
        }
        return (long)(mixed%(ulong)(2*stage.Jitter.Ticks+1))-stage.Jitter.Ticks;
    }

    /// <summary>Sample all logical phases by explicit nonnegative operation index without mutable RNG state.</summary>
    /// <param name="operationIndex">Stable index, zero through Int64.MaxValue.</param>
    /// <returns>Detached immutable phase durations and their exact tick sum.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative.</exception>
    public SimulationLatencySample Sample(long operationIndex)
    {
        if(operationIndex<0)throw new ArgumentOutOfRangeException(nameof(operationIndex));
        return new(Seed,operationIndex,Stages.Select(s=>new SampledLatencyStage(s.Phase,TimeSpan.FromTicks(s.Duration.Ticks+JitterTicks(operationIndex,s)))).ToArray());
    }

    /// <summary>Aggregate base-plus-jitter upper duration, no greater than one logical day.</summary>
    public TimeSpan MaximumDuration => TimeSpan.FromTicks(Stages.Sum(s=>s.Duration.Ticks+s.Jitter.Ticks));

    // Profile members.
}

/// <summary>One sampled logical phase duration.</summary>
/// <param name="Phase">Declared phase identity.</param>
/// <param name="Duration">Nonnegative sampled logical duration.</param>
public sealed record SampledLatencyStage(SimulationLatencyPhase Phase, TimeSpan Duration);
/// <summary>Detached latency sample; no worker, timer, wall clock or lease authority.</summary>
public sealed class SimulationLatencySample
{
    internal SimulationLatencySample(int seed,long index,SampledLatencyStage[] stages)
    {Seed=seed;OperationIndex=index;Stages=Array.AsReadOnly(stages.ToArray());Total=TimeSpan.FromTicks(stages.Sum(s=>s.Duration.Ticks));}
    /// <summary>Configured deterministic seed.</summary>
    public int Seed { get; }
    /// <summary>Caller-selected nonnegative operation index.</summary>
    public long OperationIndex { get; }
    /// <summary>Detached immutable logical phase durations.</summary>
    public IReadOnlyList<SampledLatencyStage> Stages { get; }
    /// <summary>Exact total logical duration, from zero through one day.</summary>
    public TimeSpan Total { get; }
    /// <summary>Inspect one sampled phase; absent phases have zero duration.</summary>
    /// <param name="phase">Declared phase identity.</param>
    /// <returns>Previously sampled logical phase duration.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The phase is unknown.</exception>
    public TimeSpan DurationOf(SimulationLatencyPhase phase)
    {
        if(!Enum.IsDefined(phase))throw new ArgumentOutOfRangeException(nameof(phase));
        return Stages.FirstOrDefault(s=>s.Phase==phase)?.Duration??TimeSpan.Zero;
    }
    /// <summary>Calculate a logical ready time without advancing any clock.</summary>
    /// <param name="start">Nonnegative run-scoped logical start.</param>
    /// <returns>Start plus sampled total duration.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Start is negative.</exception>
    /// <exception cref="OverflowException">The resulting logical deadline is unrepresentable.</exception>
    public TimeSpan ReadyAt(TimeSpan start)
    {
        if(start<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(start));
        return TimeSpan.FromTicks(checked(start.Ticks+Total.Ticks));
    }
    // Sample members.
}
