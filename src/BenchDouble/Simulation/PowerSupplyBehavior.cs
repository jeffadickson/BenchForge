using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>Elapsed time is scoped to one simulation run; never use a process-wide static clock.</summary>
public interface ISimulationClock
{
    TimeSpan Elapsed { get; }
}

/// <summary>Deterministic clock advanced explicitly by the test or demonstration.</summary>
public sealed class ManualSimulationClock : ISimulationClock
{
    private readonly object _gate = new();
    private TimeSpan _elapsed;
    public TimeSpan Elapsed { get { lock (_gate) return _elapsed; } }

    public void AdvanceBy(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_gate) _elapsed = _elapsed.Add(duration);
    }
}

/// <summary>Immutable state supplied to a simulation behavior. It cannot mutate hardware.</summary>
public sealed record PowerSupplyState(
    ElectricPotential SetVoltage,
    ElectricCurrent CurrentLimit,
    bool OutputEnabled);

/// <summary>One simulated sample, with its logical acquisition timestamp.</summary>
public sealed record PowerSupplySample(
    ElectricPotential Voltage,
    ElectricCurrent Current,
    TimeSpan SimulatedElapsed,
    bool CurrentLimitExceeded);

/// <summary>
/// Extension point for user-defined C# mock behavior.
/// The caller supplies the current state and virtual time; the behavior returns
/// only simulated current demand. It never receives a VISA or GPIB handle.
/// </summary>
public interface ILoadBehavior
{
    ElectricCurrent CurrentAt(ElectricPotential voltage, TimeSpan elapsed);
}

/// <summary>Simple Ohmic load model. Pure, deterministic, and safe to unit-test.</summary>
public sealed class ResistiveLoadBehavior(ElectricResistance resistance) : ILoadBehavior
{
    public ElectricCurrent CurrentAt(ElectricPotential voltage, TimeSpan elapsed)
    {
        if (!double.IsFinite(resistance.Ohms) || resistance.Ohms <= 0)
            throw new ArgumentOutOfRangeException(nameof(resistance));
        return ElectricCurrent.FromAmperes(voltage.Volts / resistance.Ohms);
    }
}

/// <summary>
/// Decorates any normal load with a deterministic fault beginning at a specified
/// simulation time. Use this to verify controller overcurrent detection.
/// </summary>
public sealed class TimedOvercurrentBehavior(
    ILoadBehavior normal,
    TimeSpan faultAt,
    ElectricCurrent faultCurrent) : ILoadBehavior
{
    public ElectricCurrent CurrentAt(ElectricPotential voltage, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(normal);
        if (faultAt < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(faultAt));
        if (!double.IsFinite(faultCurrent.Amperes) || faultCurrent.Amperes < 0)
            throw new ArgumentOutOfRangeException(nameof(faultCurrent));
        return elapsed >= faultAt ? faultCurrent : normal.CurrentAt(voltage, elapsed);
    }
}

/// <summary>
/// Mock power supply with constructor-injected behavior and virtual clock.
/// This is an idealized measurement model, NOT a physical compliance solver,
/// calibrated supply model, or hardware safety interlock.
/// </summary>
/// <param name="behavior">Nonnull simulated load behavior.</param>
/// <param name="clock">Nonnull run-scoped logical clock.</param>
public sealed class MockPowerSupply(ILoadBehavior behavior, ISimulationClock clock)
{
    private readonly ILoadBehavior _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    private readonly ISimulationClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PowerSupplyState _state = new(
        ElectricPotential.FromVolts(0),
        ElectricCurrent.FromAmperes(3),
        false);

    public async ValueTask ConfigureAsync(
        ElectricPotential voltage, ElectricCurrent currentLimit,
        CancellationToken ct = default)
    {
        if (!double.IsFinite(voltage.Volts) || voltage.Volts < 0 ||
            !double.IsFinite(currentLimit.Amperes) || currentLimit.Amperes <= 0)
            throw new ArgumentOutOfRangeException(nameof(voltage), "Invalid supply setpoint or current limit.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { _state = _state with { SetVoltage = voltage, CurrentLimit = currentLimit }; }
        finally { _gate.Release(); }
    }

    public async ValueTask SetOutputAsync(bool enabled, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { _state = _state with { OutputEnabled = enabled }; }
        finally { _gate.Release(); }
    }

    /// <summary>Sample the injected load at a valid run time, retaining an exceeded-limit indication.</summary>
    /// <param name="ct">Cancellation while waiting and after the model callback.</param>
    /// <returns>A simulated sample; disabled output returns zero without invoking the load.</returns>
    /// <exception cref="InvalidOperationException">The clock or returned current is invalid.</exception>
    public async ValueTask<PowerSupplySample> MeasureAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            TimeSpan now = _clock.Elapsed;
            if (now < TimeSpan.Zero) throw new InvalidOperationException("Simulation clock returned negative elapsed time.");
            if (!_state.OutputEnabled)
                return new(ElectricPotential.FromVolts(0),
                    ElectricCurrent.FromAmperes(0), now, false);

            ElectricCurrent current = _behavior.CurrentAt(_state.SetVoltage, now);
            ct.ThrowIfCancellationRequested();
            if (!double.IsFinite(current.Amperes))
                throw new InvalidOperationException("Mock behavior returned a non-finite current.");

            // Report an exceeded limit, but do not pretend to model physical
            // current limiting or voltage droop. A future model can do that.
            return new(_state.SetVoltage, current, now,
                Math.Abs(current.Amperes) > _state.CurrentLimit.Amperes);
        }
        finally { _gate.Release(); }
    }
}
