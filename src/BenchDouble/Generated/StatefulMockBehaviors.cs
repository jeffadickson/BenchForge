using BenchDouble.Simulation;
using UnitsNet;

namespace BenchDouble.Generated;

/// <summary>Pure virtual-time measurement models for stateful instrument mocks.</summary>
public static class StatefulMockBehaviors
{
    /// <summary>Switch between two finite values at a virtual-time boundary.</summary>
    /// <param name="transitionAt">Nonnegative transition time.</param>
    /// <param name="before">Value before the transition.</param>
    /// <param name="after">Value at and after the transition.</param>
    /// <returns>Deterministic scalar behavior.</returns>
    public static Func<MockReadingContext, double> Step(TimeSpan transitionAt, double before, double after)
    {
        if (transitionAt < TimeSpan.Zero || !double.IsFinite(before) || !double.IsFinite(after))
            throw new ArgumentOutOfRangeException(nameof(transitionAt));
        return context => context.Elapsed < transitionAt ? before : after;
    }

    /// <summary>Interpolate linearly between two virtual-time boundaries.</summary>
    /// <param name="start">Nonnegative ramp start.</param>
    /// <param name="end">Ramp end, strictly after start.</param>
    /// <param name="from">Finite initial value.</param>
    /// <param name="to">Finite final value.</param>
    /// <returns>Clamped deterministic ramp behavior.</returns>
    public static Func<MockReadingContext, double> Ramp(TimeSpan start, TimeSpan end, double from, double to)
    {
        if (start < TimeSpan.Zero || end <= start || !double.IsFinite(from) || !double.IsFinite(to))
            throw new ArgumentOutOfRangeException(nameof(end));
        return context =>
        {
            if (context.Elapsed <= start) return from;
            if (context.Elapsed >= end) return to;
            var fraction = (context.Elapsed - start).TotalSeconds / (end - start).TotalSeconds;
            return Math.Clamp(from * (1 - fraction) + to * fraction, Math.Min(from, to), Math.Max(from, to));
        };
    }

    /// <summary>Read voltage from a shared virtual signal at mock time.</summary>
    /// <param name="signal">Run-scoped virtual signal bus.</param>
    /// <returns>Voltage in volts.</returns>
    public static Func<MockReadingContext, double> SignalVoltage(VirtualSignalBus signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return context => signal.Snapshot().VoltageAt(Duration.FromSeconds(context.Elapsed.TotalSeconds)).Volts;
    }

    /// <summary>Read resistive current from a shared virtual signal at mock time.</summary>
    /// <param name="signal">Run-scoped virtual signal bus.</param>
    /// <returns>Current in amperes.</returns>
    public static Func<MockReadingContext, double> SignalCurrent(VirtualSignalBus signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return context => signal.Snapshot().CurrentAt(Duration.FromSeconds(context.Elapsed.TotalSeconds)).Amperes;
    }
}
