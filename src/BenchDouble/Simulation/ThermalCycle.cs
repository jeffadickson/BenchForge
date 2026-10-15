using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>Piecewise-linear thermal chamber program with two dwells and a return ramp.</summary>
public sealed record ThermalCycleSettings(
    Temperature Start, Temperature Hot, Temperature Cold,
    TimeSpan HeatRamp, TimeSpan HotDwell, TimeSpan CoolRamp,
    TimeSpan ColdDwell, TimeSpan ReturnRamp)
{
    public TimeSpan TotalDuration => HeatRamp + HotDwell + CoolRamp + ColdDwell + ReturnRamp;

    public void Validate()
    {
        if (HeatRamp <= TimeSpan.Zero || HotDwell < TimeSpan.Zero ||
            CoolRamp <= TimeSpan.Zero || ColdDwell < TimeSpan.Zero || ReturnRamp <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(HeatRamp), "Ramp times must be positive; dwell times cannot be negative.");
        if (!double.IsFinite(Start.DegreesCelsius) ||
            !double.IsFinite(Hot.DegreesCelsius) || !double.IsFinite(Cold.DegreesCelsius) ||
            Hot.DegreesCelsius <= Start.DegreesCelsius ||
            Cold.DegreesCelsius >= Start.DegreesCelsius)
            throw new ArgumentException("Expected finite hot > start > cold temperatures.");
    }

    public static ThermalCycleSettings Example() => new(
        Temperature.FromDegreesCelsius(30),
        Temperature.FromDegreesCelsius(70),
        Temperature.FromDegreesCelsius(-30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(2),
        TimeSpan.FromHours(1), TimeSpan.FromHours(2),
        TimeSpan.FromHours(1));
}

/// <summary>Chamber temperature and named phase at a specified logical time.</summary>
public sealed record ThermalChamberSample(Temperature Temperature, string Phase, TimeSpan Elapsed);

/// <summary>
/// Idealized chamber setpoint model; no real thermal lag, overshoot, or sensor noise.
/// Evaluated using the shared simulation clock, not wall-clock sleeps.
/// </summary>
public sealed class ThermalChamberModel
{
    public ThermalCycleSettings Settings { get; }
    public ThermalChamberModel(ThermalCycleSettings settings)
    {
        settings.Validate();
        Settings = settings;
    }

    public ThermalChamberSample At(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        var s = Settings;
        double t = elapsed.TotalSeconds;
        double heat = s.HeatRamp.TotalSeconds, hot = s.HotDwell.TotalSeconds;
        double cool = s.CoolRamp.TotalSeconds, cold = s.ColdDwell.TotalSeconds;
        double ret = s.ReturnRamp.TotalSeconds;
        double start = s.Start.DegreesCelsius, high = s.Hot.DegreesCelsius;
        double low = s.Cold.DegreesCelsius;
        if (t < heat) return Sample(start + (high - start) * t / heat, "Heat ramp");
        t -= heat;
        if (t < hot) return Sample(high, "Hot dwell");
        t -= hot;
        if (t < cool) return Sample(high + (low - high) * t / cool, "Cool ramp");
        t -= cool;
        if (t < cold) return Sample(low, "Cold dwell");
        t -= cold;
        if (t < ret) return Sample(low + (start - low) * t / ret, "Return ramp");
        return Sample(start, "Complete");

        ThermalChamberSample Sample(double celsius, string phase) =>
            new(Temperature.FromDegreesCelsius(celsius), phase, elapsed);
    }
}

/// <summary>
/// Injected power-supply load behavior linked to a virtual chamber.
/// At or below baseline temperature, current stays nominal; at the hot point,
/// it rises by the configured fraction. This is an illustrative model only.
/// </summary>
public sealed class TemperatureDependentLoadBehavior(
    ThermalChamberModel chamber, ISimulationClock clock,
    ElectricCurrent nominalCurrent, Temperature baseline,
    Temperature hot, double hotIncreaseFraction) : ILoadBehavior
{
    public ElectricCurrent CurrentAt(ElectricPotential voltage, TimeSpan elapsed)
    {
        if (clock.Elapsed != elapsed)
            throw new InvalidOperationException("The chamber and supply must share the same logical time.");
        if (!double.IsFinite(nominalCurrent.Amperes) || nominalCurrent.Amperes < 0 ||
            !double.IsFinite(hotIncreaseFraction) || hotIncreaseFraction < 0 ||
            hot.DegreesCelsius <= baseline.DegreesCelsius)
            throw new ArgumentException("Invalid temperature/current model.");
        double temp = chamber.At(elapsed).Temperature.DegreesCelsius;
        double fraction = Math.Clamp(
            (temp - baseline.DegreesCelsius) /
            (hot.DegreesCelsius - baseline.DegreesCelsius), 0, 1);
        return ElectricCurrent.FromAmperes(
            nominalCurrent.Amperes * (1 + hotIncreaseFraction * fraction));
    }
}
