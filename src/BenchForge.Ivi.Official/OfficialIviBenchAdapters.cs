using BenchForge.Contracts;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using UnitsNet;

namespace BenchForge.Ivi.Official;

/// <summary>
/// Catalog of all nine official IVI Foundation .NET class-driver interfaces loaded into
/// <see cref="BenchForge.Ivi.Official"/> for vendor driver interoperability on .NET 11.
/// </summary>
public static class OfficialIviClassCatalog
{
    /// <summary>Official <c>Ivi.Dmm.IIviDmm</c> interface type.</summary>
    public static Type DmmInterface => typeof(global::Ivi.Dmm.IIviDmm);

    /// <summary>Official <c>Ivi.DCPwr.IIviDCPwr</c> interface type.</summary>
    public static Type DcPwrInterface => typeof(global::Ivi.DCPwr.IIviDCPwr);

    /// <summary>Official <c>Ivi.Fgen.IIviFgen</c> interface type.</summary>
    public static Type FgenInterface => typeof(global::Ivi.Fgen.IIviFgen);

    /// <summary>Official <c>Ivi.Scope.IIviScope</c> interface type.</summary>
    public static Type ScopeInterface => typeof(global::Ivi.Scope.IIviScope);

    /// <summary>Official <c>Ivi.Swtch.IIviSwtch</c> interface type.</summary>
    public static Type SwitchInterface => typeof(global::Ivi.Swtch.IIviSwtch);

    /// <summary>Official <c>Ivi.RFSigGen.IIviRFSigGen</c> interface type.</summary>
    public static Type RfSigGenInterface => typeof(global::Ivi.RFSigGen.IIviRFSigGen);

    /// <summary>Official <c>Ivi.SpecAn.IIviSpecAn</c> interface type.</summary>
    public static Type SpecAnInterface => typeof(global::Ivi.SpecAn.IIviSpecAn);

    /// <summary>Official <c>Ivi.PwrMeter.IIviPwrMeter</c> interface type.</summary>
    public static Type PowerMeterInterface => typeof(global::Ivi.PwrMeter.IIviPwrMeter);

    /// <summary>Official <c>Ivi.Counter.IIviCounter</c> interface type.</summary>
    public static Type CounterInterface => typeof(global::Ivi.Counter.IIviCounter);

    /// <summary>Read-only map from canonical BenchForge class tag to the official IVI.NET interface <see cref="Type"/>.</summary>
    public static IReadOnlyDictionary<string, Type> AllInterfaces { get; } = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        ["DMM"] = DmmInterface,
        ["PSU"] = DcPwrInterface,
        ["FGEN"] = FgenInterface,
        ["SCOPE"] = ScopeInterface,
        ["SWITCH"] = SwitchInterface,
        ["RFSIGGEN"] = RfSigGenInterface,
        ["SPECAN"] = SpecAnInterface,
        ["PWRMETER"] = PowerMeterInterface,
        ["COUNTER"] = CounterInterface,
    };

    /// <summary>Inspect public members exposed by each loaded official IVI Foundation interface.</summary>
    /// <returns>Map from canonical class tag to sorted member signatures.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DescribeAll() =>
        AllInterfaces.ToDictionary(
            kv => kv.Key,
            kv => OfficialIviCompatibility.Describe(kv.Value),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Validate that a supplied vendor driver implements <typeparamref name="TIvi"/>.</summary>
    /// <typeparam name="TIvi">Expected official IVI interface type.</typeparam>
    /// <param name="driver">Candidate vendor driver object.</param>
    /// <returns>Typed reference to <typeparamref name="TIvi"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="driver"/> does not implement <typeparamref name="TIvi"/>.</exception>
    public static TIvi RequireOfficial<TIvi>(object driver) where TIvi : class =>
        driver as TIvi ?? throw new ArgumentException(
            $"Driver does not implement the official {typeof(TIvi).FullName} interface.", nameof(driver));
}

internal static class OfficialIviRailroad
{
    public static Task<BenchResult<T>> ExecuteQueryAsync<T>(
        string role,
        string operation,
        Func<T> action,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(BenchResult<T>.Fail(new BenchError("BF-IVI-CANCEL", BenchErrorKind.CancelledBeforeDispatch, $"{role}.{operation} was cancelled before dispatch.", role)));
        try
        {
            return Task.FromResult(BenchResult<T>.Ok(action()));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Task.FromResult(BenchResult<T>.Fail(BenchError.Validation("BF-IVI-RANGE", ex.Message, role, $"Check parameter bounds for {role}.{operation}.")));
        }
        catch (TimeoutException ex)
        {
            return Task.FromResult(BenchResult<T>.Fail(new BenchError("BF-IVI-TIMEOUT", BenchErrorKind.Timeout, ex.Message, role, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, Remedy: $"Verify IVI driver communication for {role}.{operation}.")));
        }
        catch (Exception ex)
        {
            return Task.FromResult(BenchResult<T>.Fail(new BenchError("BF-IVI-FAULT", BenchErrorKind.TransportFault, ex.Message, role, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, Remedy: $"Inspect vendor IVI error queue on {role} before continuing.")));
        }
    }

    public static Task<BenchResult> ExecuteWriteAsync(
        string role,
        string operation,
        Action action,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(BenchResult.Fail(new BenchError("BF-IVI-CANCEL", BenchErrorKind.CancelledBeforeDispatch, $"{role}.{operation} was cancelled before dispatch.", role)));
        try
        {
            action();
            return Task.FromResult(BenchResult.Ok());
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-IVI-RANGE", ex.Message, role, $"Check parameter bounds for {role}.{operation}.")));
        }
        catch (TimeoutException ex)
        {
            return Task.FromResult(BenchResult.Fail(new BenchError("BF-IVI-TIMEOUT", BenchErrorKind.Timeout, ex.Message, role, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, RequiresRecovery: true, Remedy: $"Do not blindly retry {role}.{operation}; verify physical instrument state first.")));
        }
        catch (Exception ex)
        {
            return Task.FromResult(BenchResult.Fail(new BenchError("BF-IVI-FAULT", BenchErrorKind.TransportFault, ex.Message, role, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, RequiresRecovery: true, Remedy: $"Inspect vendor IVI error queue and physical state on {role} before reconnecting.")));
        }
    }
}

/// <summary>
/// Railroad-oriented <see cref="IBenchDigitalMultimeter"/> adapter wrapping a caller-owned official
/// <see cref="global::Ivi.Dmm.IIviDmm"/> vendor driver for 1:1 interchangeability with BenchForge mocks.
/// </summary>
public sealed class OfficialIviDmmBenchAdapter : IBenchDigitalMultimeter
{
    private readonly global::Ivi.Dmm.IIviDmm _driver;
    private readonly Func<global::Ivi.Dmm.IIviDmm, double> _measureDcVoltageVolts;
    private readonly Func<global::Ivi.Dmm.IIviDmm, double> _measureDcCurrentAmps;
    private readonly Func<global::Ivi.Dmm.IIviDmm, double> _measureResistanceOhms;
    private readonly Action<global::Ivi.Dmm.IIviDmm>? _safeState;

    /// <summary>Create an official IVI DMM adapter with explicit vendor operation delegates.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="driver">Caller-owned official <see cref="global::Ivi.Dmm.IIviDmm"/> driver.</param>
    /// <param name="identity">Declared instrument identity.</param>
    /// <param name="measureDcVoltageVolts">Delegate reading DC voltage in volts.</param>
    /// <param name="measureDcCurrentAmps">Delegate reading DC current in amperes.</param>
    /// <param name="measureResistanceOhms">Delegate reading resistance in ohms.</param>
    /// <param name="safeState">Optional safe-state delegate (defaults to no-op for passive meters).</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public OfficialIviDmmBenchAdapter(
        string role,
        global::Ivi.Dmm.IIviDmm driver,
        InstrumentIdentity identity,
        Func<global::Ivi.Dmm.IIviDmm, double> measureDcVoltageVolts,
        Func<global::Ivi.Dmm.IIviDmm, double> measureDcCurrentAmps,
        Func<global::Ivi.Dmm.IIviDmm, double> measureResistanceOhms,
        Action<global::Ivi.Dmm.IIviDmm>? safeState = null,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _measureDcVoltageVolts = measureDcVoltageVolts ?? throw new ArgumentNullException(nameof(measureDcVoltageVolts));
        _measureDcCurrentAmps = measureDcCurrentAmps ?? throw new ArgumentNullException(nameof(measureDcCurrentAmps));
        _measureResistanceOhms = measureResistanceOhms ?? throw new ArgumentNullException(nameof(measureResistanceOhms));
        _safeState = safeState;
        Calibration = calibration;
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Physical;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "DMM", Identity, Backend, ["MeasureDcVoltage", "MeasureDcCurrent", "MeasureResistance", "SafeState", "IVI.Dmm"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SafeState", () => _safeState?.Invoke(_driver), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureDcVoltageAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "MeasureDcVoltage", () => ElectricPotential.FromVolts(_measureDcVoltageVolts(_driver)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureDcCurrentAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "MeasureDcCurrent", () => ElectricCurrent.FromAmperes(_measureDcCurrentAmps(_driver)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "MeasureResistance", () => ElectricResistance.FromOhms(_measureResistanceOhms(_driver)), cancellationToken);
}

/// <summary>
/// Railroad-oriented <see cref="IBenchPowerSupply"/> adapter wrapping a caller-owned official
/// <see cref="global::Ivi.DCPwr.IIviDCPwr"/> vendor driver.
/// </summary>
public sealed class OfficialIviDcPwrBenchAdapter : IBenchPowerSupply
{
    private readonly global::Ivi.DCPwr.IIviDCPwr _driver;
    private readonly Action<global::Ivi.DCPwr.IIviDCPwr, int, double> _setVoltageVolts;
    private readonly Func<global::Ivi.DCPwr.IIviDCPwr, int, double> _getVoltageVolts;
    private readonly Action<global::Ivi.DCPwr.IIviDCPwr, int, double> _setCurrentLimitAmps;
    private readonly Func<global::Ivi.DCPwr.IIviDCPwr, int, double> _getCurrentLimitAmps;
    private readonly Func<global::Ivi.DCPwr.IIviDCPwr, int, double> _measureVoltageVolts;
    private readonly Func<global::Ivi.DCPwr.IIviDCPwr, int, double> _measureCurrentAmps;
    private readonly Action<global::Ivi.DCPwr.IIviDCPwr, int, bool> _setOutputEnabled;
    private readonly Func<global::Ivi.DCPwr.IIviDCPwr, int, bool> _getOutputEnabled;

    /// <summary>Create an official IVI DC power supply adapter with explicit vendor operation delegates.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="driver">Caller-owned official <see cref="global::Ivi.DCPwr.IIviDCPwr"/> driver.</param>
    /// <param name="identity">Declared instrument identity.</param>
    /// <param name="setVoltageVolts">Delegate programming output voltage in volts.</param>
    /// <param name="getVoltageVolts">Delegate querying voltage setpoint in volts.</param>
    /// <param name="setCurrentLimitAmps">Delegate programming current limit in amperes.</param>
    /// <param name="getCurrentLimitAmps">Delegate querying current limit in amperes.</param>
    /// <param name="measureVoltageVolts">Delegate measuring terminal voltage in volts.</param>
    /// <param name="measureCurrentAmps">Delegate measuring terminal current in amperes.</param>
    /// <param name="setOutputEnabled">Delegate enabling or disabling channel output.</param>
    /// <param name="getOutputEnabled">Delegate querying channel output state.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public OfficialIviDcPwrBenchAdapter(
        string role,
        global::Ivi.DCPwr.IIviDCPwr driver,
        InstrumentIdentity identity,
        Action<global::Ivi.DCPwr.IIviDCPwr, int, double> setVoltageVolts,
        Func<global::Ivi.DCPwr.IIviDCPwr, int, double> getVoltageVolts,
        Action<global::Ivi.DCPwr.IIviDCPwr, int, double> setCurrentLimitAmps,
        Func<global::Ivi.DCPwr.IIviDCPwr, int, double> getCurrentLimitAmps,
        Func<global::Ivi.DCPwr.IIviDCPwr, int, double> measureVoltageVolts,
        Func<global::Ivi.DCPwr.IIviDCPwr, int, double> measureCurrentAmps,
        Action<global::Ivi.DCPwr.IIviDCPwr, int, bool> setOutputEnabled,
        Func<global::Ivi.DCPwr.IIviDCPwr, int, bool> getOutputEnabled,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _setVoltageVolts = setVoltageVolts ?? throw new ArgumentNullException(nameof(setVoltageVolts));
        _getVoltageVolts = getVoltageVolts ?? throw new ArgumentNullException(nameof(getVoltageVolts));
        _setCurrentLimitAmps = setCurrentLimitAmps ?? throw new ArgumentNullException(nameof(setCurrentLimitAmps));
        _getCurrentLimitAmps = getCurrentLimitAmps ?? throw new ArgumentNullException(nameof(getCurrentLimitAmps));
        _measureVoltageVolts = measureVoltageVolts ?? throw new ArgumentNullException(nameof(measureVoltageVolts));
        _measureCurrentAmps = measureCurrentAmps ?? throw new ArgumentNullException(nameof(measureCurrentAmps));
        _setOutputEnabled = setOutputEnabled ?? throw new ArgumentNullException(nameof(setOutputEnabled));
        _getOutputEnabled = getOutputEnabled ?? throw new ArgumentNullException(nameof(getOutputEnabled));
        Calibration = calibration;
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Physical;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "PSU", Identity, Backend, ["SetVoltage", "GetVoltage", "SetCurrentLimit", "GetCurrentLimit", "MeasureVoltage", "MeasureCurrent", "SetOutputEnabled", "SafeState", "IVI.DCPwr"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SafeState", () => { _setOutputEnabled(_driver, 1, false); _setVoltageVolts(_driver, 1, 0.0); }, cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetVoltageAsync(ElectricPotential voltage, int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetVoltage", () => _setVoltageVolts(_driver, channel, voltage.Volts), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "GetVoltage", () => ElectricPotential.FromVolts(_getVoltageVolts(_driver, channel)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetCurrentLimitAsync(ElectricCurrent currentLimit, int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetCurrentLimit", () => _setCurrentLimitAmps(_driver, channel, currentLimit.Amperes), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> GetCurrentLimitAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "GetCurrentLimit", () => ElectricCurrent.FromAmperes(_getCurrentLimitAmps(_driver, channel)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "MeasureVoltage", () => ElectricPotential.FromVolts(_measureVoltageVolts(_driver, channel)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "MeasureCurrent", () => ElectricCurrent.FromAmperes(_measureCurrentAmps(_driver, channel)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetOutputEnabled", () => _setOutputEnabled(_driver, channel, enabled), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<bool>> GetOutputEnabledAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "GetOutputEnabled", () => _getOutputEnabled(_driver, channel), cancellationToken);
}

/// <summary>
/// Railroad-oriented <see cref="IBenchFunctionGenerator"/> adapter wrapping a caller-owned official
/// <see cref="global::Ivi.Fgen.IIviFgen"/> vendor driver.
/// </summary>
public sealed class OfficialIviFgenBenchAdapter : IBenchFunctionGenerator
{
    private readonly global::Ivi.Fgen.IIviFgen _driver;
    private readonly Action<global::Ivi.Fgen.IIviFgen, int, double> _setFrequencyHz;
    private readonly Func<global::Ivi.Fgen.IIviFgen, int, double> _getFrequencyHz;
    private readonly Action<global::Ivi.Fgen.IIviFgen, int, double> _setAmplitudeVpp;
    private readonly Func<global::Ivi.Fgen.IIviFgen, int, double> _getAmplitudeVpp;
    private readonly Action<global::Ivi.Fgen.IIviFgen, int, bool> _setOutputEnabled;

    /// <summary>Create an official IVI function generator adapter with explicit vendor delegates.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="driver">Caller-owned official <see cref="global::Ivi.Fgen.IIviFgen"/> driver.</param>
    /// <param name="identity">Declared instrument identity.</param>
    /// <param name="setFrequencyHz">Delegate programming output frequency in hertz.</param>
    /// <param name="getFrequencyHz">Delegate querying output frequency in hertz.</param>
    /// <param name="setAmplitudeVpp">Delegate programming peak-to-peak amplitude in volts.</param>
    /// <param name="getAmplitudeVpp">Delegate querying peak-to-peak amplitude in volts.</param>
    /// <param name="setOutputEnabled">Delegate enabling or disabling channel output.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public OfficialIviFgenBenchAdapter(
        string role,
        global::Ivi.Fgen.IIviFgen driver,
        InstrumentIdentity identity,
        Action<global::Ivi.Fgen.IIviFgen, int, double> setFrequencyHz,
        Func<global::Ivi.Fgen.IIviFgen, int, double> getFrequencyHz,
        Action<global::Ivi.Fgen.IIviFgen, int, double> setAmplitudeVpp,
        Func<global::Ivi.Fgen.IIviFgen, int, double> getAmplitudeVpp,
        Action<global::Ivi.Fgen.IIviFgen, int, bool> setOutputEnabled,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _setFrequencyHz = setFrequencyHz ?? throw new ArgumentNullException(nameof(setFrequencyHz));
        _getFrequencyHz = getFrequencyHz ?? throw new ArgumentNullException(nameof(getFrequencyHz));
        _setAmplitudeVpp = setAmplitudeVpp ?? throw new ArgumentNullException(nameof(setAmplitudeVpp));
        _getAmplitudeVpp = getAmplitudeVpp ?? throw new ArgumentNullException(nameof(getAmplitudeVpp));
        _setOutputEnabled = setOutputEnabled ?? throw new ArgumentNullException(nameof(setOutputEnabled));
        Calibration = calibration;
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Physical;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "FGEN", Identity, Backend, ["SetFrequency", "GetFrequency", "SetAmplitude", "GetAmplitude", "SetOutputEnabled", "SafeState", "IVI.Fgen"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) =>
        SetOutputEnabledAsync(false, 1, cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyAsync(Frequency frequency, int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetFrequency", () => _setFrequencyHz(_driver, channel, frequency.Hertz), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetFrequencyAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "GetFrequency", () => Frequency.FromHertz(_getFrequencyHz(_driver, channel)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetAmplitudeAsync(ElectricPotential amplitude, int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetAmplitude", () => _setAmplitudeVpp(_driver, channel, amplitude.Volts), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetAmplitudeAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "GetAmplitude", () => ElectricPotential.FromVolts(_getAmplitudeVpp(_driver, channel)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetOutputEnabled", () => _setOutputEnabled(_driver, channel, enabled), cancellationToken);
}

/// <summary>
/// Railroad-oriented <see cref="IBenchOscilloscope"/> adapter wrapping a caller-owned official
/// <see cref="global::Ivi.Scope.IIviScope"/> vendor driver.
/// </summary>
public sealed class OfficialIviScopeBenchAdapter : IBenchOscilloscope
{
    private readonly global::Ivi.Scope.IIviScope _driver;
    private readonly Action<global::Ivi.Scope.IIviScope, int, double> _setVerticalScaleVolts;
    private readonly Action<global::Ivi.Scope.IIviScope, int, double> _setTriggerLevelVolts;
    private readonly Func<global::Ivi.Scope.IIviScope, int, double[]> _acquireWaveformVolts;

    /// <summary>Create an official IVI oscilloscope adapter with explicit vendor delegates.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="driver">Caller-owned official <see cref="global::Ivi.Scope.IIviScope"/> driver.</param>
    /// <param name="identity">Declared instrument identity.</param>
    /// <param name="setVerticalScaleVolts">Delegate setting volts-per-division scale.</param>
    /// <param name="setTriggerLevelVolts">Delegate setting edge trigger voltage threshold.</param>
    /// <param name="acquireWaveformVolts">Delegate acquiring decoded waveform voltage samples.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public OfficialIviScopeBenchAdapter(
        string role,
        global::Ivi.Scope.IIviScope driver,
        InstrumentIdentity identity,
        Action<global::Ivi.Scope.IIviScope, int, double> setVerticalScaleVolts,
        Action<global::Ivi.Scope.IIviScope, int, double> setTriggerLevelVolts,
        Func<global::Ivi.Scope.IIviScope, int, double[]> acquireWaveformVolts,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _setVerticalScaleVolts = setVerticalScaleVolts ?? throw new ArgumentNullException(nameof(setVerticalScaleVolts));
        _setTriggerLevelVolts = setTriggerLevelVolts ?? throw new ArgumentNullException(nameof(setTriggerLevelVolts));
        _acquireWaveformVolts = acquireWaveformVolts ?? throw new ArgumentNullException(nameof(acquireWaveformVolts));
        Calibration = calibration;
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Physical;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SCOPE", Identity, Backend, ["SetVerticalScale", "SetTriggerLevel", "AcquireWaveformVolts", "SafeState", "IVI.Scope"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetVerticalScaleAsync(ElectricPotential voltsPerDivision, int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetVerticalScale", () => _setVerticalScaleVolts(_driver, channel, voltsPerDivision.Volts), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetTriggerLevelAsync(ElectricPotential level, int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetTriggerLevel", () => _setTriggerLevelVolts(_driver, channel, level.Volts), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<double[]>> AcquireWaveformVoltsAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "AcquireWaveformVolts", () => _acquireWaveformVolts(_driver, channel), cancellationToken);
}

/// <summary>
/// Railroad-oriented <see cref="IBenchSwitchMatrix"/> adapter wrapping a caller-owned official
/// <see cref="global::Ivi.Swtch.IIviSwtch"/> vendor driver.
/// </summary>
public sealed class OfficialIviSwitchBenchAdapter : IBenchSwitchMatrix
{
    private readonly global::Ivi.Swtch.IIviSwtch _driver;
    private readonly Action<global::Ivi.Swtch.IIviSwtch, string, string> _connect;
    private readonly Action<global::Ivi.Swtch.IIviSwtch, string, string> _disconnect;
    private readonly Action<global::Ivi.Swtch.IIviSwtch> _disconnectAll;
    private readonly Func<global::Ivi.Swtch.IIviSwtch, IReadOnlyList<string>> _getClosedRoutes;

    /// <summary>Create an official IVI switch matrix adapter with explicit vendor delegates.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="driver">Caller-owned official <see cref="global::Ivi.Swtch.IIviSwtch"/> driver.</param>
    /// <param name="identity">Declared instrument identity.</param>
    /// <param name="connect">Delegate closing a relay path between two channels.</param>
    /// <param name="disconnect">Delegate opening a relay path between two channels.</param>
    /// <param name="disconnectAll">Delegate opening all closed relay paths.</param>
    /// <param name="getClosedRoutes">Delegate listing currently closed relay routes.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public OfficialIviSwitchBenchAdapter(
        string role,
        global::Ivi.Swtch.IIviSwtch driver,
        InstrumentIdentity identity,
        Action<global::Ivi.Swtch.IIviSwtch, string, string> connect,
        Action<global::Ivi.Swtch.IIviSwtch, string, string> disconnect,
        Action<global::Ivi.Swtch.IIviSwtch> disconnectAll,
        Func<global::Ivi.Swtch.IIviSwtch, IReadOnlyList<string>> getClosedRoutes,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _disconnect = disconnect ?? throw new ArgumentNullException(nameof(disconnect));
        _disconnectAll = disconnectAll ?? throw new ArgumentNullException(nameof(disconnectAll));
        _getClosedRoutes = getClosedRoutes ?? throw new ArgumentNullException(nameof(getClosedRoutes));
        Calibration = calibration;
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Physical;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SWITCH", Identity, Backend, ["Connect", "Disconnect", "DisconnectAll", "GetClosedRoutes", "SafeState", "IVI.Swtch"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => DisconnectAllAsync(cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> ConnectAsync(string channel1, string channel2, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "Connect", () => _connect(_driver, channel1, channel2), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> DisconnectAsync(string channel1, string channel2, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "Disconnect", () => _disconnect(_driver, channel1, channel2), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> DisconnectAllAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "DisconnectAll", () => _disconnectAll(_driver), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<IReadOnlyList<string>>> GetClosedRoutesAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "GetClosedRoutes", () => _getClosedRoutes(_driver), cancellationToken);
}

/// <summary>
/// Railroad-oriented <see cref="IBenchRfSignalGenerator"/> adapter wrapping a caller-owned official
/// <see cref="global::Ivi.RFSigGen.IIviRFSigGen"/> vendor driver.
/// </summary>
public sealed class OfficialIviRfSigGenBenchAdapter : IBenchRfSignalGenerator
{
    private readonly global::Ivi.RFSigGen.IIviRFSigGen _driver;
    private readonly Action<global::Ivi.RFSigGen.IIviRFSigGen, double> _setFrequencyHz;
    private readonly Func<global::Ivi.RFSigGen.IIviRFSigGen, double> _getFrequencyHz;
    private readonly Action<global::Ivi.RFSigGen.IIviRFSigGen, double> _setPowerWatts;
    private readonly Func<global::Ivi.RFSigGen.IIviRFSigGen, double> _getPowerWatts;
    private readonly Action<global::Ivi.RFSigGen.IIviRFSigGen, bool> _setRfEnabled;

    /// <summary>Create an official IVI RF signal generator adapter with explicit vendor delegates.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="driver">Caller-owned official <see cref="global::Ivi.RFSigGen.IIviRFSigGen"/> driver.</param>
    /// <param name="identity">Declared instrument identity.</param>
    /// <param name="setFrequencyHz">Delegate programming RF carrier frequency in hertz.</param>
    /// <param name="getFrequencyHz">Delegate querying RF carrier frequency in hertz.</param>
    /// <param name="setPowerWatts">Delegate programming RF output power in watts.</param>
    /// <param name="getPowerWatts">Delegate querying RF output power in watts.</param>
    /// <param name="setRfEnabled">Delegate enabling or muting RF carrier output.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public OfficialIviRfSigGenBenchAdapter(
        string role,
        global::Ivi.RFSigGen.IIviRFSigGen driver,
        InstrumentIdentity identity,
        Action<global::Ivi.RFSigGen.IIviRFSigGen, double> setFrequencyHz,
        Func<global::Ivi.RFSigGen.IIviRFSigGen, double> getFrequencyHz,
        Action<global::Ivi.RFSigGen.IIviRFSigGen, double> setPowerWatts,
        Func<global::Ivi.RFSigGen.IIviRFSigGen, double> getPowerWatts,
        Action<global::Ivi.RFSigGen.IIviRFSigGen, bool> setRfEnabled,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _setFrequencyHz = setFrequencyHz ?? throw new ArgumentNullException(nameof(setFrequencyHz));
        _getFrequencyHz = getFrequencyHz ?? throw new ArgumentNullException(nameof(getFrequencyHz));
        _setPowerWatts = setPowerWatts ?? throw new ArgumentNullException(nameof(setPowerWatts));
        _getPowerWatts = getPowerWatts ?? throw new ArgumentNullException(nameof(getPowerWatts));
        _setRfEnabled = setRfEnabled ?? throw new ArgumentNullException(nameof(setRfEnabled));
        Calibration = calibration;
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Physical;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "RFSIGGEN", Identity, Backend, ["SetFrequency", "GetFrequency", "SetOutputPower", "GetOutputPower", "SetRfOutputEnabled", "SafeState", "IVI.RFSigGen"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => SetRfOutputEnabledAsync(false, cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetFrequency", () => _setFrequencyHz(_driver, frequency.Hertz), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetFrequencyAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "GetFrequency", () => Frequency.FromHertz(_getFrequencyHz(_driver)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetOutputPowerAsync(Power power, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetOutputPower", () => _setPowerWatts(_driver, (double)power.Watts), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<Power>> GetOutputPowerAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "GetOutputPower", () => Power.FromWatts((decimal)_getPowerWatts(_driver)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetRfOutputEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetRfOutputEnabled", () => _setRfEnabled(_driver, enabled), cancellationToken);
}

/// <summary>
/// Railroad-oriented <see cref="IBenchSpectrumAnalyzer"/> adapter wrapping a caller-owned official
/// <see cref="global::Ivi.SpecAn.IIviSpecAn"/> vendor driver.
/// </summary>
public sealed class OfficialIviSpecAnBenchAdapter : IBenchSpectrumAnalyzer
{
    private readonly global::Ivi.SpecAn.IIviSpecAn _driver;
    private readonly Action<global::Ivi.SpecAn.IIviSpecAn, double> _setCenterHz;
    private readonly Func<global::Ivi.SpecAn.IIviSpecAn, double> _getCenterHz;
    private readonly Action<global::Ivi.SpecAn.IIviSpecAn, double> _setSpanHz;
    private readonly Action<global::Ivi.SpecAn.IIviSpecAn, double> _setRbwHz;

    /// <summary>Create an official IVI spectrum analyzer adapter with explicit vendor delegates.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="driver">Caller-owned official <see cref="global::Ivi.SpecAn.IIviSpecAn"/> driver.</param>
    /// <param name="identity">Declared instrument identity.</param>
    /// <param name="setCenterHz">Delegate programming center frequency in hertz.</param>
    /// <param name="getCenterHz">Delegate querying center frequency in hertz.</param>
    /// <param name="setSpanHz">Delegate programming span in hertz.</param>
    /// <param name="setRbwHz">Delegate programming resolution bandwidth in hertz.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public OfficialIviSpecAnBenchAdapter(
        string role,
        global::Ivi.SpecAn.IIviSpecAn driver,
        InstrumentIdentity identity,
        Action<global::Ivi.SpecAn.IIviSpecAn, double> setCenterHz,
        Func<global::Ivi.SpecAn.IIviSpecAn, double> getCenterHz,
        Action<global::Ivi.SpecAn.IIviSpecAn, double> setSpanHz,
        Action<global::Ivi.SpecAn.IIviSpecAn, double> setRbwHz,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _setCenterHz = setCenterHz ?? throw new ArgumentNullException(nameof(setCenterHz));
        _getCenterHz = getCenterHz ?? throw new ArgumentNullException(nameof(getCenterHz));
        _setSpanHz = setSpanHz ?? throw new ArgumentNullException(nameof(setSpanHz));
        _setRbwHz = setRbwHz ?? throw new ArgumentNullException(nameof(setRbwHz));
        Calibration = calibration;
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Physical;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SPECAN", Identity, Backend, ["SetCenterFrequency", "SetSpan", "SetResolutionBandwidth", "GetCenterFrequency", "SafeState", "IVI.SpecAn"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetCenterFrequencyAsync(Frequency center, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetCenterFrequency", () => _setCenterHz(_driver, center.Hertz), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetSpanAsync(Frequency span, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetSpan", () => _setSpanHz(_driver, span.Hertz), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetResolutionBandwidthAsync(Frequency rbw, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetResolutionBandwidth", () => _setRbwHz(_driver, rbw.Hertz), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetCenterFrequencyAsync(CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "GetCenterFrequency", () => Frequency.FromHertz(_getCenterHz(_driver)), cancellationToken);
}

/// <summary>
/// Railroad-oriented <see cref="IBenchRfPowerMeter"/> adapter wrapping a caller-owned official
/// <see cref="global::Ivi.PwrMeter.IIviPwrMeter"/> vendor driver.
/// </summary>
public sealed class OfficialIviRfPowerMeterBenchAdapter : IBenchRfPowerMeter
{
    private readonly global::Ivi.PwrMeter.IIviPwrMeter _driver;
    private readonly Action<global::Ivi.PwrMeter.IIviPwrMeter, int, double> _setFrequencyHz;
    private readonly Func<global::Ivi.PwrMeter.IIviPwrMeter, int, double> _measurePowerWatts;

    /// <summary>Create an official IVI RF power meter adapter with explicit vendor delegates.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="driver">Caller-owned official <see cref="global::Ivi.PwrMeter.IIviPwrMeter"/> driver.</param>
    /// <param name="identity">Declared instrument identity.</param>
    /// <param name="setFrequencyHz">Delegate configuring sensor calibration frequency in hertz.</param>
    /// <param name="measurePowerWatts">Delegate measuring RF power in watts.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public OfficialIviRfPowerMeterBenchAdapter(
        string role,
        global::Ivi.PwrMeter.IIviPwrMeter driver,
        InstrumentIdentity identity,
        Action<global::Ivi.PwrMeter.IIviPwrMeter, int, double> setFrequencyHz,
        Func<global::Ivi.PwrMeter.IIviPwrMeter, int, double> measurePowerWatts,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _setFrequencyHz = setFrequencyHz ?? throw new ArgumentNullException(nameof(setFrequencyHz));
        _measurePowerWatts = measurePowerWatts ?? throw new ArgumentNullException(nameof(measurePowerWatts));
        Calibration = calibration;
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Physical;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "PWRMETER", Identity, Backend, ["SetFrequency", "MeasurePower", "SafeState", "IVI.PwrMeter"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyAsync(Frequency frequency, int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteWriteAsync(Role, "SetFrequency", () => _setFrequencyHz(_driver, channel, frequency.Hertz), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<Power>> MeasurePowerAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "MeasurePower", () => Power.FromWatts((decimal)_measurePowerWatts(_driver, channel)), cancellationToken);
}

/// <summary>
/// Railroad-oriented <see cref="IBenchFrequencyCounter"/> adapter wrapping a caller-owned official
/// <see cref="global::Ivi.Counter.IIviCounter"/> vendor driver.
/// </summary>
public sealed class OfficialIviCounterBenchAdapter : IBenchFrequencyCounter
{
    private readonly global::Ivi.Counter.IIviCounter _driver;
    private readonly Func<global::Ivi.Counter.IIviCounter, int, double> _measureFrequencyHz;
    private readonly Func<global::Ivi.Counter.IIviCounter, int, double> _measurePeriodSeconds;

    /// <summary>Create an official IVI frequency counter adapter with explicit vendor delegates.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="driver">Caller-owned official <see cref="global::Ivi.Counter.IIviCounter"/> driver.</param>
    /// <param name="identity">Declared instrument identity.</param>
    /// <param name="measureFrequencyHz">Delegate measuring signal frequency in hertz.</param>
    /// <param name="measurePeriodSeconds">Delegate measuring signal period in seconds.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public OfficialIviCounterBenchAdapter(
        string role,
        global::Ivi.Counter.IIviCounter driver,
        InstrumentIdentity identity,
        Func<global::Ivi.Counter.IIviCounter, int, double> measureFrequencyHz,
        Func<global::Ivi.Counter.IIviCounter, int, double> measurePeriodSeconds,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _measureFrequencyHz = measureFrequencyHz ?? throw new ArgumentNullException(nameof(measureFrequencyHz));
        _measurePeriodSeconds = measurePeriodSeconds ?? throw new ArgumentNullException(nameof(measurePeriodSeconds));
        Calibration = calibration;
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Physical;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "COUNTER", Identity, Backend, ["MeasureFrequency", "MeasurePeriod", "SafeState", "IVI.Counter"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> MeasureFrequencyAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "MeasureFrequency", () => Frequency.FromHertz(_measureFrequencyHz(_driver, channel)), cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<Duration>> MeasurePeriodAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        OfficialIviRailroad.ExecuteQueryAsync(Role, "MeasurePeriod", () => Duration.FromSeconds(_measurePeriodSeconds(_driver, channel)), cancellationToken);
}
