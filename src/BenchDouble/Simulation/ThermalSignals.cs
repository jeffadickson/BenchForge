using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>
/// Typed, read-only physical temperature signal at a logical simulation time.
/// Mocks receive this through constructor injection, never a global string-keyed bus.
/// </summary>
public interface ITemperatureSignal
{
    Temperature ReadAt(TimeSpan elapsed);
}

/// <summary>
/// A temperature signal whose setpoint is linear between known slope breakpoints.
/// Enables exact first-order integration across thermal-cycle ramp boundaries.
/// </summary>
public interface ITemperatureProgram : ITemperatureSignal
{
    IReadOnlyList<TimeSpan> Breakpoints { get; }
}

/// <summary>
/// Stateful first-order sensor, driven by a shared temperature program.
/// Integrates the piecewise-linear setpoint exactly between reads, so results
/// do not depend on how frequently clients sample the sensor.
/// </summary>
public sealed class FirstOrderThermalSensor : ITemperatureSignal
{
    private readonly ITemperatureProgram _input;
    private readonly double _tauSeconds;
    private readonly object _gate = new();
    private TimeSpan _last;
    private double _kelvin;

    public FirstOrderThermalSensor(
        ITemperatureProgram input,
        TimeSpan timeConstant,
        Temperature initialTemperature)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        if (timeConstant <= TimeSpan.Zero ||
            !double.IsFinite(timeConstant.TotalSeconds))
            throw new ArgumentOutOfRangeException(nameof(timeConstant));
        if (!double.IsFinite(initialTemperature.Kelvins))
            throw new ArgumentOutOfRangeException(nameof(initialTemperature));
        _tauSeconds = timeConstant.TotalSeconds;
        _kelvin = initialTemperature.Kelvins;
    }

    public Temperature ReadAt(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        lock (_gate)
        {
            if (elapsed < _last)
                throw new InvalidOperationException(
                    "Stateful sensor time cannot move backward. Create a new sensor per simulation run.");

            foreach (var boundary in _input.Breakpoints)
            {
                if (boundary <= _last || boundary >= elapsed) continue;
                AdvanceTo(boundary);
            }
            AdvanceTo(elapsed);
            return Temperature.FromKelvins(_kelvin);
        }
    }

    private void AdvanceTo(TimeSpan next)
    {
        double dt = (next - _last).TotalSeconds;
        if (dt <= 0) return;

        double u0 = _input.ReadAt(_last).Kelvins;
        double u1 = _input.ReadAt(next).Kelvins;
        double slope = (u1 - u0) / dt;

        // Exact solution of d(sensor)/dt = (input - sensor)/tau for a linear input.
        // Expm1 avoids loss of precision for small dt/tau.
        double decay = Math.Exp(-dt / _tauSeconds);
        _kelvin = u1 - slope * _tauSeconds +
                  (_kelvin - u0 + slope * _tauSeconds) * decay;
        _last = next;
    }
}
