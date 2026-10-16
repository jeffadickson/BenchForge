using System.Reflection;
using BenchDouble.Ivi;

namespace BenchDouble.Ivi.Official;

/// <summary>
/// Boundary to the official IVI Foundation class assemblies. This module does not
/// reimplement IIviDmm / IIviFgen and does not claim driver conformance.
/// </summary>
public static class OfficialIviCompatibility
{
    public const string DmmInterfaceName = "global::Ivi.Dmm.IIviDmm";
    public const string FgenInterfaceName = "global::Ivi.Fgen.IIviFgen";

    public static Type DmmInterface =>
        typeof(global::Ivi.Dmm.IIviDmm);

    public static Type FgenInterface =>
        typeof(global::Ivi.Fgen.IIviFgen);

    public static bool IsOfficialDmm(object driver) =>
        DmmInterface.IsInstanceOfType(driver);

    public static bool IsOfficialFunctionGenerator(object driver) =>
        FgenInterface.IsInstanceOfType(driver);

    /// <summary>
    /// Reports the public contract of the actual IVI assemblies, without assuming
    /// IVI 2014 members are also Generation 2026 core requirements.
    /// </summary>
    public static IReadOnlyList<string> Describe(Type instrumentClass) =>
        instrumentClass.GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => $"{member.MemberType}: {member.Name}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Checks that a supplied vendor driver truly implements the official DMM interface.
    /// This is more meaningful than adopting IVI-like method names.
    /// </summary>
    public static global::Ivi.Dmm.IIviDmm RequireOfficialDmm(object driver) =>
        driver as global::Ivi.Dmm.IIviDmm ??
        throw new ArgumentException("Driver does not implement the official global::Ivi.Dmm.IIviDmm interface.", nameof(driver));

    public static global::Ivi.Fgen.IIviFgen RequireOfficialFunctionGenerator(object driver) =>
        driver as global::Ivi.Fgen.IIviFgen ??
        throw new ArgumentException("Driver does not implement the official global::Ivi.Fgen.IIviFgen interface.", nameof(driver));
}

/// <summary>
/// Explicit bridge for user-provided, typed vendor-driver operations.
/// Delegates are chosen by the integrating application because specific IVI
/// drivers can expose different supported extensions/capabilities.
/// </summary>
public sealed class OfficialDmmBridge : IIviDigitalMultimeter
{
    private readonly global::Ivi.Dmm.IIviDmm _driver;
    private readonly Action<global::Ivi.Dmm.IIviDmm, IviMeasurementFunction, double?, double?> _configure;
    private readonly Func<global::Ivi.Dmm.IIviDmm, double> _measure;
    private readonly Action<global::Ivi.Dmm.IIviDmm> _reset;
    private readonly Func<global::Ivi.Dmm.IIviDmm, string> _identity;

    public OfficialDmmBridge(
        global::Ivi.Dmm.IIviDmm driver,
        Action<global::Ivi.Dmm.IIviDmm, IviMeasurementFunction, double?, double?> configure,
        Func<global::Ivi.Dmm.IIviDmm, double> measure,
        Action<global::Ivi.Dmm.IIviDmm> reset,
        Func<global::Ivi.Dmm.IIviDmm, string> identity)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
        _measure = measure ?? throw new ArgumentNullException(nameof(measure));
        _reset = reset ?? throw new ArgumentNullException(nameof(reset));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public string Identity => _identity(_driver);
    public bool SimulationEnabled => false; // Vendor driver's internal simulation option is not inferred.
    public void ConfigureMeasurement(IviMeasurementFunction function, double? range = null, double? resolution = null) =>
        _configure(_driver, function, range, resolution);
    public double Measure() => _measure(_driver);
    public void Reset() => _reset(_driver);
    public void Dispose() { } // Vendor-driver lifetime belongs to the caller.
}


/// <summary>
/// Adapts an actual IVI Foundation function-generator driver to the portable
/// BenchDouble contract. The host supplies explicit operations so that IVI
/// driver capability and repeated-channel semantics are not assumed.
/// </summary>
public sealed class OfficialFgenBridge : IIviFunctionGenerator
{
    private readonly global::Ivi.Fgen.IIviFgen _driver;
    private readonly Action<global::Ivi.Fgen.IIviFgen, int, IviWaveform, double, double> _configure;
    private readonly Action<global::Ivi.Fgen.IIviFgen, int, bool> _output;
    private readonly Action<global::Ivi.Fgen.IIviFgen> _reset;
    private readonly Func<global::Ivi.Fgen.IIviFgen, string> _identity;

    public OfficialFgenBridge(
        global::Ivi.Fgen.IIviFgen driver,
        Action<global::Ivi.Fgen.IIviFgen, int, IviWaveform, double, double> configure,
        Action<global::Ivi.Fgen.IIviFgen, int, bool> output,
        Action<global::Ivi.Fgen.IIviFgen> reset,
        Func<global::Ivi.Fgen.IIviFgen, string> identity)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _reset = reset ?? throw new ArgumentNullException(nameof(reset));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public string Identity => _identity(_driver);
    public bool SimulationEnabled => false; // Do not infer an IVI driver option.
    public void ConfigureWaveform(int channel, IviWaveform shape, double frequencyHz, double amplitudeVpp)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channel);
        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        if (!double.IsFinite(amplitudeVpp) || amplitudeVpp <= 0) throw new ArgumentOutOfRangeException(nameof(amplitudeVpp));
        _configure(_driver, channel, shape, frequencyHz, amplitudeVpp);
    }
    public void SetOutputEnabled(int channel, bool enabled)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channel);
        _output(_driver, channel, enabled);
    }
    public void Reset() => _reset(_driver);
    public void Dispose() { } // Caller owns the vendor driver.
}
