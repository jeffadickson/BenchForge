namespace BenchDouble.Simulation;

/// <summary>How a run-owned virtual clock advances; none of these modes controls hardware deadlines.</summary>
public enum SimulationClockMode
{
    /// <summary>Time changes only through explicit advancement.</summary>
    Manual,
    /// <summary>Time is held without automatic advancement.</summary>
    Paused,
    /// <summary>Logical time follows monotonic elapsed time at unit speed.</summary>
    RealTime,
    /// <summary>Logical time advances faster than monotonic elapsed time.</summary>
    Accelerated,
    /// <summary>Logical time advances slower than monotonic elapsed time.</summary>
    Slowed
}

/// <summary>One coherent snapshot of a simulation clock's logical time and pacing policy.</summary>
/// <param name="Elapsed">Nonnegative run-scoped logical time.</param>
/// <param name="Mode">Current advancement mode.</param>
/// <param name="Speed">Logical seconds per monotonic second when running.</param>
public sealed record SimulationClockStatus(TimeSpan Elapsed, SimulationClockMode Mode, double Speed);

/// <summary>Thread-safe run clock with manual, paused and scaled monotonic-time modes.</summary>
/// <remarks>Does not create timers, wait, schedule hardware operations or replace heartbeat/deadline clocks.</remarks>
public sealed class AdjustableSimulationClock : ISimulationClock
{
    private readonly TimeProvider _provider;
    private readonly object _gate = new();
    private decimal _ticks;
    private long _startedAt;
    private long _lastObserved;
    private double _speed;
    private bool _running;
    private bool _manual = true;

    /// <summary>Create a manually controlled zero-time clock with an optional monotonic time provider.</summary>
    /// <param name="provider">Caller-owned provider, defaulting to system monotonic time; UTC changes are ignored.</param>
    /// <param name="speed">Logical seconds per monotonic second, from 0.001 to 1000000.</param>
    /// <exception cref="ArgumentOutOfRangeException">Speed or provider timestamp frequency is invalid.</exception>
    public AdjustableSimulationClock(TimeProvider? provider = null, double speed = 1)
    {
        ValidateSpeed(speed);
        _provider = provider ?? TimeProvider.System;
        if (_provider.TimestampFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(provider));
        _speed = speed;
    }

    /// <summary>Current nonnegative logical elapsed time at 100-nanosecond resolution.</summary>
    /// <exception cref="OverflowException">Logical time would exceed TimeSpan capacity.</exception>
    /// <exception cref="InvalidOperationException">The injected provider moves backwards.</exception>
    public TimeSpan Elapsed
    {
        get { lock (_gate) return ToTime(CurrentTicks()); }
    }

    /// <summary>Read elapsed time, advancement mode and speed under one lock.</summary>
    /// <returns>A coherent immutable clock snapshot.</returns>
    public SimulationClockStatus Status()
    {
        lock (_gate) return new(ToTime(CurrentTicks()), Mode(), _speed);
    }

    /// <summary>Start monotonic advancement at the configured speed; repeated calls are idempotent.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _startedAt = _lastObserved = _provider.GetTimestamp();
            _running = true;
        }
    }

    /// <summary>Freeze the current logical time without losing sub-tick scaled accumulation.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            _ticks = CurrentTicks();
            _running = false;
            _manual = false;
        }
    }

    /// <summary>Freeze automatic advancement and select explicit manual time control.</summary>
    public void UseManual()
    {
        lock (_gate)
        {
            _ticks = CurrentTicks();
            _running = false;
            _manual = true;
        }
    }

    /// <summary>Change speed without retroactively changing previously accumulated logical time.</summary>
    /// <param name="speed">Logical seconds per monotonic second, from 0.001 to 1000000.</param>
    /// <exception cref="ArgumentOutOfRangeException">Speed is nonfinite or outside the supported interval.</exception>
    public void SetSpeed(double speed)
    {
        ValidateSpeed(speed);
        lock (_gate)
        {
            if (_speed == speed) return;
            if (_running)
            {
                var now = _provider.GetTimestamp();
                var ticks = At(now);
                _ticks = ticks;
                _startedAt = now;
            }
            _speed = speed;
        }
    }

    /// <summary>Advance a stopped clock manually; automatic advancement must first be paused.</summary>
    /// <param name="duration">Nonnegative logical duration.</param>
    /// <exception cref="ArgumentOutOfRangeException">Duration is negative.</exception>
    /// <exception cref="InvalidOperationException">The clock is running.</exception>
    /// <exception cref="OverflowException">The new time is not representable.</exception>
    public void AdvanceBy(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_gate)
        {
            if (_running) throw new InvalidOperationException("Pause automatic advancement before advancing manually.");
            var value = _ticks + duration.Ticks;
            if (value > TimeSpan.MaxValue.Ticks) throw new OverflowException("Simulation time exceeds TimeSpan capacity.");
            _ticks = value;
            _manual = true;
        }
    }

    private decimal CurrentTicks() => _running ? At(_provider.GetTimestamp()) : _ticks;
    private decimal At(long now)
    {
        if (_provider.GetElapsedTime(_lastObserved, now) < TimeSpan.Zero)
            throw new InvalidOperationException("Simulation time provider moved backwards.");
        var elapsed = _provider.GetElapsedTime(_startedAt, now);
        if (elapsed < TimeSpan.Zero) throw new InvalidOperationException("Simulation time provider moved backwards.");
        var ticks = _ticks + elapsed.Ticks * (decimal)_speed;
        if (ticks > TimeSpan.MaxValue.Ticks) throw new OverflowException("Simulation time exceeds TimeSpan capacity.");
        _lastObserved = now;
        return ticks;
    }
    private SimulationClockMode Mode() => !_running ? (_manual ? SimulationClockMode.Manual : SimulationClockMode.Paused)
        : _speed == 1 ? SimulationClockMode.RealTime : _speed > 1 ? SimulationClockMode.Accelerated : SimulationClockMode.Slowed;
    private static TimeSpan ToTime(decimal ticks) => TimeSpan.FromTicks(decimal.ToInt64(decimal.Truncate(ticks)));
    private static void ValidateSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed < 0.001 || speed > 1000000) throw new ArgumentOutOfRangeException(nameof(speed));
    }
}
