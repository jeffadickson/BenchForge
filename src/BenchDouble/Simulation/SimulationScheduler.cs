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
    private TimeSpan ReadTime()
    {
        var now=_clock.Elapsed;
        if(now<TimeSpan.Zero)throw new InvalidOperationException("Simulation clock is negative.");
        if(now<_observed)throw new InvalidOperationException("Simulation clock moved backwards.");
        // Clock validation.
        _observed=now;return now;
    }
    /// <summary>Schedule one event after a logical delay without sleeping or advancing time.</summary>
    /// <param name="delay">Nonnegative logical delay.</param>
    /// <param name="label">Nonblank label, at most 128 characters without controls.</param>
    /// <param name="ct">Cancellation before mutation.</param>
    /// <returns>Immutable accepted event with a unique positive identifier.</returns>
    /// <exception cref="ArgumentException">Delay or label is invalid.</exception>
    /// <exception cref="InvalidOperationException">Queue is full or clock is invalid.</exception>
    /// <exception cref="OverflowException">Identifier or deadline is unrepresentable.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is observed before mutation.</exception>
    public SimulationScheduledEvent Schedule(TimeSpan delay,string label,CancellationToken ct=default)=>ScheduleCore(delay,label,false,ct);
    private SimulationScheduledEvent ScheduleCore(TimeSpan time,string label,bool absolute,CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if(label.Length>128||label.Any(char.IsControl))throw new ArgumentException("Invalid event label.",nameof(label));
        if(time<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(time));
        ct.ThrowIfCancellationRequested();
        lock(_gate)
        {
            ct.ThrowIfCancellationRequested();
            if(_pending.Count>=_capacity)throw new InvalidOperationException("Simulation event queue is full.");
            var now=ReadTime();ct.ThrowIfCancellationRequested();
            var due=absolute?time:TimeSpan.FromTicks(checked(now.Ticks+time.Ticks));
            if(due<now)throw new ArgumentOutOfRangeException(nameof(time),"Deadline precedes current logical time.");
            var id=checked(_nextId+1);var item=new SimulationScheduledEvent(id,label,due);
            var key=(due.Ticks,id);_pending.Add(key,item);_keys.Add(id,key);_nextId=id;return item;
        }
    }

    // Scheduler members.
}
