namespace BenchDouble.Simulation;
/// <summary>Immutable scheduled simulation event; its identifier is not an ownership capability.</summary>
/// <param name="Id">Positive acceptance-order identifier local to one scheduler.</param>
/// <param name="Label">Bounded caller label, never a command or file path.</param>
/// <param name="DueAt">Nonnegative run-scoped logical deadline.</param>
public sealed record SimulationScheduledEvent(long Id,string Label,TimeSpan DueAt);
/// <summary>Thread-safe explicitly pumped virtual event queue; no timers, workers, callbacks or physical I/O.</summary>
/// <remarks>The caller supplies the canonical run clock. Clock providers must not reenter this scheduler.</remarks>
public sealed class SimulationScheduler
{
    private readonly object _gate=new();
    private readonly ISimulationClock _clock;
    private readonly int _capacity;
    private readonly SortedDictionary<(long Ticks,long Id),SimulationScheduledEvent> _pending=new();
    private readonly Dictionary<long,(long Ticks,long Id)> _keys=new();
    private long _nextId;
    private TimeSpan _observed;
    /// <summary>Construct a simulation queue bound to the caller's run clock.</summary>
    /// <param name="clock">Nonnull shared simulation clock; not a physical deadline clock.</param>
    /// <param name="capacity">Maximum queued events, one through 4096.</param>
    /// <exception cref="ArgumentNullException">Clock is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Capacity is outside the supported interval.</exception>
    public SimulationScheduler(ISimulationClock clock,int capacity=4096)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if(capacity<1||capacity>4096)throw new ArgumentOutOfRangeException(nameof(capacity));
        _clock=clock;_capacity=capacity;
    }
    /// <summary>Current pending event count without sampling or advancing the clock.</summary>
    public int Count {get{lock(_gate)return _pending.Count;}}
    // Scheduler members.
}
