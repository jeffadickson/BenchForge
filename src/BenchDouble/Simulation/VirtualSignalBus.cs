using System.Collections.Concurrent;
using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>Deterministic, hardware-free signal shared by a source, meter and oscilloscope.</summary>
/// <remarks>Illustrative physics only; this model is not a calibrated instrument or safety interlock.</remarks>
public sealed class VirtualSignalBus
{
    private readonly object _gate = new();
    private Frequency _frequency = Frequency.FromHertz(1_000);
    private ElectricPotential _peak = ElectricPotential.FromVolts(0);
    private ElectricPotential _offset = ElectricPotential.FromVolts(0);
    private ElectricResistance _load = ElectricResistance.FromOhms(1_000);
    private ElectricCurrent _compliance = ElectricCurrent.FromAmperes(1);
    private bool _enabled;

    /// <summary>Configure a sine source with a resistive load and current compliance limit.</summary>
    /// <param name="frequency">Positive finite source frequency.</param>
    /// <param name="peak">Nonnegative peak voltage, not RMS or peak-to-peak.</param>
    /// <param name="offset">DC voltage offset.</param>
    /// <param name="load">Positive resistive load.</param>
    /// <param name="compliance">Positive current limit.</param>
    /// <exception cref="ArgumentOutOfRangeException">A quantity is nonfinite or outside the simulation domain.</exception>
    public void Configure(Frequency frequency, ElectricPotential peak, ElectricPotential offset,
        ElectricResistance load, ElectricCurrent compliance)
    {
        if (!double.IsFinite(frequency.Hertz) || frequency.Hertz <= 0 ||
            !double.IsFinite(peak.Volts) || peak.Volts < 0 ||
            !double.IsFinite(offset.Volts) ||
            !double.IsFinite(load.Ohms) || load.Ohms <= 0 ||
            !double.IsFinite(compliance.Amperes) || compliance.Amperes <= 0)
            throw new ArgumentOutOfRangeException(nameof(frequency), "Invalid virtual source parameters.");
        lock (_gate)
        {
            _frequency = frequency;
            _peak = peak;
            _offset = offset;
            _load = load;
            _compliance = compliance;
        }
    }

    /// <summary>Enable or disable the simulated output; disabled output is exactly zero.</summary>
    /// <param name="enabled">Requested simulated output state.</param>
    public void SetOutput(bool enabled) { lock (_gate) _enabled = enabled; }

    /// <summary>Read a coherent snapshot of the configured virtual source.</summary>
    /// <returns>Immutable simulation state.</returns>
    public VirtualSignalState Snapshot()
    {
        lock (_gate) return new(_frequency, _peak, _offset, _load, _compliance, _enabled);
    }
}

/// <summary>Immutable virtual sine source state shared by all simulated instruments.</summary>
/// <param name="Frequency">Sine-wave frequency.</param>
/// <param name="Peak">Peak amplitude, not peak-to-peak.</param>
/// <param name="Offset">DC offset.</param>
/// <param name="Load">Resistive load.</param>
/// <param name="Compliance">Current compliance limit.</param>
/// <param name="Enabled">Whether the simulated source is enabled.</param>
public sealed record VirtualSignalState(Frequency Frequency, ElectricPotential Peak, ElectricPotential Offset,
    ElectricResistance Load, ElectricCurrent Compliance, bool Enabled)
{
    /// <summary>Calculate a voltage at the given virtual time with ideal current compliance clipping.</summary>
    /// <param name="time">Virtual time relative to the source epoch.</param>
    /// <returns>Simulated instantaneous voltage.</returns>
    public ElectricPotential VoltageAt(Time time)
    {
        if (!double.IsFinite(time.Seconds)) throw new ArgumentOutOfRangeException(nameof(time));
        if (!Enabled) return ElectricPotential.FromVolts(0);
        double ideal = Offset.Volts + Peak.Volts * Math.Sin(2 * Math.PI * Frequency.Hertz * time.Seconds);
        double limit = Compliance.Amperes * Load.Ohms;
        return ElectricPotential.FromVolts(Math.Clamp(ideal, -limit, limit));
    }

    /// <summary>Calculate a simulated current using Ohm's law.</summary>
    /// <param name="time">Virtual time.</param>
    /// <returns>Instantaneous resistive current.</returns>
    public ElectricCurrent CurrentAt(Time time) => ElectricCurrent.FromAmperes(VoltageAt(time).Volts / Load.Ohms);

    /// <summary>Calculate DC and RMS voltage over one ideal waveform period using deterministic sampling.</summary>
    /// <param name="samples">Number of equally spaced samples, at least 2.</param>
    /// <returns>DC mean and true RMS voltage.</returns>
    public (ElectricPotential Dc, ElectricPotential Rms) MeasureVoltage(int samples = 1024)
    {
        if (samples < 2 || samples > 1_000_000) throw new ArgumentOutOfRangeException(nameof(samples));
        double sum = 0, squares = 0;
        for (int i = 0; i < samples; i++)
        {
            double value = VoltageAt(Time.FromSeconds(i / (samples * Frequency.Hertz))).Volts;
            sum += value;
            squares += value * value;
        }
        return (ElectricPotential.FromVolts(sum / samples), ElectricPotential.FromVolts(Math.Sqrt(squares / samples)));
    }
}
