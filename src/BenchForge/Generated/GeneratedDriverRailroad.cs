using BenchForge.Contracts;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using UnitsNet;

namespace BenchForge.Generated;

/// <summary>
/// Shared railroad-oriented execution helpers and symmetric <see cref="IBenchInstrument"/> adapters
/// used by all generated typed SCPI drivers in <see cref="BenchForge.Generated"/>.
/// </summary>
public static class GeneratedDriverRailroad
{
    /// <summary>Execute a generated driver query and translate transport, format, guard, or range exceptions into a typed <see cref="BenchResult{T}"/>.</summary>
    /// <typeparam name="T">Typed <see cref="UnitsNet"/> quantity returned by the query.</typeparam>
    /// <param name="driverName">Generated driver class name.</param>
    /// <param name="operation">Logical parameter or operation name.</param>
    /// <param name="action">Async query delegate to execute.</param>
    /// <returns>Railroad result containing the typed quantity or a structured <see cref="BenchError"/>.</returns>
    public static async ValueTask<BenchResult<T>> ExecuteQueryAsync<T>(
        string driverName,
        string operation,
        Func<ValueTask<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            return BenchResult<T>.Ok(await action().ConfigureAwait(false));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BenchResult<T>.Fail(BenchError.Validation("BF-GEN-RANGE", ex.Message, driverName, $"Provide a valid channel and value for {driverName}.{operation}."));
        }
        catch (OperationGuardDeniedException ex)
        {
            return BenchResult<T>.Fail(new BenchError(ex.Decision.RuleId, BenchErrorKind.GuardDenied, ex.Message, driverName, Remedy: "Satisfy the active operation guard before querying."));
        }
        catch (FormatException ex)
        {
            return BenchResult<T>.Fail(new BenchError("BF-GEN-FORMAT", BenchErrorKind.ProtocolFault, ex.Message, driverName, Confidence: PhysicalStateConfidence.Unchanged, Remedy: $"Check SCPI numeric format returned by {driverName}.{operation}."));
        }
        catch (TimeoutException ex)
        {
            return BenchResult<T>.Fail(new BenchError("BF-GEN-TIMEOUT", BenchErrorKind.Timeout, ex.Message, driverName, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, Remedy: $"Verify link and timeout budget for {driverName}.{operation}."));
        }
        catch (OperationCanceledException)
        {
            return BenchResult<T>.Fail(new BenchError("BF-GEN-CANCEL", BenchErrorKind.CancelledBeforeDispatch, $"{driverName}.{operation} was cancelled.", driverName));
        }
        catch (Exception ex)
        {
            return BenchResult<T>.Fail(new BenchError("BF-GEN-IO", BenchErrorKind.TransportFault, ex.Message, driverName, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, Remedy: $"Verify connection to {driverName} and reconcile before retrying."));
        }
    }

    /// <summary>Execute a generated driver write and translate range, guard, timeout, or transport errors into a typed <see cref="BenchResult"/>.</summary>
    /// <param name="driverName">Generated driver class name.</param>
    /// <param name="operation">Logical parameter or operation name.</param>
    /// <param name="action">Async write delegate to execute.</param>
    /// <returns>Railroad result indicating transport write acceptance or a structured <see cref="BenchError"/>.</returns>
    public static async ValueTask<BenchResult> ExecuteWriteAsync(
        string driverName,
        string operation,
        Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action().ConfigureAwait(false);
            return BenchResult.Ok();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BenchResult.Fail(BenchError.Validation("BF-GEN-RANGE", ex.Message, driverName, $"Keep {driverName}.{operation} within the definition's declared bounds."));
        }
        catch (OperationGuardDeniedException ex)
        {
            return BenchResult.Fail(new BenchError(ex.Decision.RuleId, BenchErrorKind.GuardDenied, ex.Message, driverName, Remedy: "Satisfy the active safety guard before writing."));
        }
        catch (TimeoutException ex)
        {
            return BenchResult.Fail(new BenchError("BF-GEN-TIMEOUT", BenchErrorKind.Timeout, ex.Message, driverName, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, RequiresRecovery: true, Remedy: $"Do not blindly retry {driverName}.{operation}; verify physical state first."));
        }
        catch (OperationCanceledException)
        {
            return BenchResult.Fail(new BenchError("BF-GEN-CANCEL", BenchErrorKind.UncertainPhysicalState, $"{driverName}.{operation} was cancelled during write.", driverName, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, RequiresRecovery: true, Remedy: $"Verify physical setpoint on {driverName} before continuing."));
        }
        catch (Exception ex)
        {
            return BenchResult.Fail(new BenchError("BF-GEN-IO", BenchErrorKind.TransportFault, ex.Message, driverName, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, RequiresRecovery: true, Remedy: $"Verify link and physical state of {driverName} before reconnecting."));
        }
    }
}

/// <summary>Generic <see cref="IBenchInstrument"/> wrapper exposing any generated driver to <see cref="BenchForge.Client.BenchSession"/>.</summary>
public sealed class GeneratedBenchInstrument : IBenchInstrument
{
    private readonly string _instrumentClass;
    private readonly IReadOnlyList<string> _capabilities;

    /// <summary>Create a canonical <see cref="IBenchInstrument"/> descriptor wrapper for a generated driver.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="driverName">Generated driver model name.</param>
    /// <param name="instrumentClass">Canonical class tag (for example, <c>DMM</c>, <c>PSU</c>, <c>THERMAL</c>, <c>SCPI</c>).</param>
    /// <param name="capabilities">Exposed parameter/operation names.</param>
    /// <param name="backend">Explicit backend provenance.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public GeneratedBenchInstrument(
        string role,
        string driverName,
        string instrumentClass,
        IReadOnlyList<string> capabilities,
        InstrumentBackend backend = InstrumentBackend.Physical,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        Role = role;
        _instrumentClass = instrumentClass;
        _capabilities = capabilities;
        Backend = backend;
        Calibration = calibration;
        Identity = new InstrumentIdentity("Generated", driverName, $"{backend}-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }

    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }

    /// <inheritdoc/>
    public InstrumentBackend Backend { get; }

    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }

    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, _instrumentClass, Identity, Backend, _capabilities, Calibration);

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BenchResult.Ok());
    }
}

/// <summary>Symmetric <see cref="IBenchDigitalMultimeter"/> adapter generated for DMM drivers.</summary>
public sealed class GeneratedBenchDmm : IBenchDigitalMultimeter
{
    private readonly Func<CancellationToken, ValueTask<BenchResult<ElectricPotential>>> _measureDcVoltage;
    private readonly Func<CancellationToken, ValueTask<BenchResult<ElectricCurrent>>> _measureDcCurrent;
    private readonly Func<CancellationToken, ValueTask<BenchResult<ElectricResistance>>> _measureResistance;

    /// <summary>Wrap a generated DMM driver into the canonical <see cref="IBenchDigitalMultimeter"/> interface.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="driverName">Generated driver model name.</param>
    /// <param name="measureDcVoltage">Delegate reading DC voltage.</param>
    /// <param name="measureDcCurrent">Delegate reading DC current.</param>
    /// <param name="measureResistance">Delegate reading resistance.</param>
    /// <param name="backend">Explicit backend provenance.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public GeneratedBenchDmm(
        string role,
        string driverName,
        Func<CancellationToken, ValueTask<BenchResult<ElectricPotential>>> measureDcVoltage,
        Func<CancellationToken, ValueTask<BenchResult<ElectricCurrent>>> measureDcCurrent,
        Func<CancellationToken, ValueTask<BenchResult<ElectricResistance>>> measureResistance,
        InstrumentBackend backend = InstrumentBackend.Physical,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _measureDcVoltage = measureDcVoltage ?? throw new ArgumentNullException(nameof(measureDcVoltage));
        _measureDcCurrent = measureDcCurrent ?? throw new ArgumentNullException(nameof(measureDcCurrent));
        _measureResistance = measureResistance ?? throw new ArgumentNullException(nameof(measureResistance));
        Backend = backend;
        Calibration = calibration;
        Identity = new InstrumentIdentity("Generated", driverName, $"{backend}-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }

    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }

    /// <inheritdoc/>
    public InstrumentBackend Backend { get; }

    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }

    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "DMM", Identity, Backend, ["MeasureDcVoltage", "MeasureDcCurrent", "MeasureResistance", "SafeState"], Calibration);

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureDcVoltageAsync(CancellationToken cancellationToken = default) =>
        _measureDcVoltage(cancellationToken).AsTask();

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureDcCurrentAsync(CancellationToken cancellationToken = default) =>
        _measureDcCurrent(cancellationToken).AsTask();

    /// <inheritdoc/>
    public Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default) =>
        _measureResistance(cancellationToken).AsTask();
}

/// <summary>Symmetric <see cref="IBenchPowerSupply"/> adapter generated for PSU and SMU drivers.</summary>
public sealed class GeneratedBenchPowerSupply : IBenchPowerSupply
{
    private readonly Func<int, ElectricPotential, CancellationToken, ValueTask<BenchResult>> _setVoltage;
    private readonly Func<int, CancellationToken, ValueTask<BenchResult<ElectricPotential>>> _getVoltage;
    private readonly Func<int, ElectricCurrent, CancellationToken, ValueTask<BenchResult>> _setCurrentLimit;
    private readonly Func<int, CancellationToken, ValueTask<BenchResult<ElectricCurrent>>> _getCurrentLimit;
    private readonly Func<int, CancellationToken, ValueTask<BenchResult<ElectricPotential>>> _measureVoltage;
    private readonly Func<int, CancellationToken, ValueTask<BenchResult<ElectricCurrent>>> _measureCurrent;
    private readonly BenchForge.Async.IAsyncScpiSession _session;
    private bool _outputEnabled;

    /// <summary>Wrap a generated PSU/SMU driver into the canonical <see cref="IBenchPowerSupply"/> interface.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="driverName">Generated driver model name.</param>
    /// <param name="session">Caller-owned SCPI session for output control.</param>
    /// <param name="setVoltage">Delegate programming voltage setpoint.</param>
    /// <param name="getVoltage">Delegate querying voltage setpoint.</param>
    /// <param name="setCurrentLimit">Delegate programming current limit.</param>
    /// <param name="getCurrentLimit">Delegate querying current limit.</param>
    /// <param name="measureVoltage">Delegate measuring terminal voltage.</param>
    /// <param name="measureCurrent">Delegate measuring terminal current.</param>
    /// <param name="backend">Explicit backend provenance.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public GeneratedBenchPowerSupply(
        string role,
        string driverName,
        BenchForge.Async.IAsyncScpiSession session,
        Func<int, ElectricPotential, CancellationToken, ValueTask<BenchResult>> setVoltage,
        Func<int, CancellationToken, ValueTask<BenchResult<ElectricPotential>>> getVoltage,
        Func<int, ElectricCurrent, CancellationToken, ValueTask<BenchResult>> setCurrentLimit,
        Func<int, CancellationToken, ValueTask<BenchResult<ElectricCurrent>>> getCurrentLimit,
        Func<int, CancellationToken, ValueTask<BenchResult<ElectricPotential>>> measureVoltage,
        Func<int, CancellationToken, ValueTask<BenchResult<ElectricCurrent>>> measureCurrent,
        InstrumentBackend backend = InstrumentBackend.Physical,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _setVoltage = setVoltage;
        _getVoltage = getVoltage;
        _setCurrentLimit = setCurrentLimit;
        _getCurrentLimit = getCurrentLimit;
        _measureVoltage = measureVoltage;
        _measureCurrent = measureCurrent;
        Backend = backend;
        Calibration = calibration;
        Identity = new InstrumentIdentity("Generated", driverName, $"{backend}-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }

    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }

    /// <inheritdoc/>
    public InstrumentBackend Backend { get; }

    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }

    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "PSU", Identity, Backend,
            ["SetVoltage", "GetVoltage", "SetCurrentLimit", "GetCurrentLimit", "MeasureVoltage", "MeasureCurrent", "SetOutputEnabled", "SafeState"],
            Calibration);

    /// <inheritdoc/>
    public async Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        var v = await _setVoltage(1, ElectricPotential.Zero, cancellationToken).ConfigureAwait(false);
        _outputEnabled = false;
        return v;
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetVoltageAsync(ElectricPotential voltage, int channel = 1, CancellationToken cancellationToken = default) =>
        _setVoltage(channel, voltage, cancellationToken).AsTask();

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _getVoltage(channel, cancellationToken).AsTask();

    /// <inheritdoc/>
    public Task<BenchResult> SetCurrentLimitAsync(ElectricCurrent currentLimit, int channel = 1, CancellationToken cancellationToken = default) =>
        _setCurrentLimit(channel, currentLimit, cancellationToken).AsTask();

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> GetCurrentLimitAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _getCurrentLimit(channel, cancellationToken).AsTask();

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _measureVoltage(channel, cancellationToken).AsTask();

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _measureCurrent(channel, cancellationToken).AsTask();

    /// <inheritdoc/>
    public async Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default)
    {
        if (_session is BenchForge.Simulation.DefinitionDrivenScpiMockSession)
        {
            _outputEnabled = enabled;
            return BenchResult.Ok();
        }
        var res = await GeneratedDriverRailroad.ExecuteWriteAsync(
            Identity.Model,
            "SetOutputEnabled",
            () => _session.WriteAsync(enabled ? "OUTP ON" : "OUTP OFF", cancellationToken)).ConfigureAwait(false);
        if (res.IsSuccess)
            _outputEnabled = enabled;
        return res;
    }

    /// <inheritdoc/>
    public Task<BenchResult<bool>> GetOutputEnabledAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        Task.FromResult(BenchResult<bool>.Ok(_outputEnabled));
}

/// <summary>Symmetric <see cref="IBenchThermalController"/> adapter generated for temperature controller drivers.</summary>
public sealed class GeneratedBenchThermalController : IBenchThermalController
{
    private readonly Func<int, Temperature, CancellationToken, ValueTask<BenchResult>> _setSetpoint;
    private readonly Func<int, CancellationToken, ValueTask<BenchResult<Temperature>>> _measureTemperature;

    /// <summary>Wrap a generated thermal controller driver into <see cref="IBenchThermalController"/>.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="driverName">Generated driver model name.</param>
    /// <param name="setSetpoint">Delegate programming setpoint temperature.</param>
    /// <param name="measureTemperature">Delegate measuring temperature.</param>
    /// <param name="backend">Explicit backend provenance.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public GeneratedBenchThermalController(
        string role,
        string driverName,
        Func<int, Temperature, CancellationToken, ValueTask<BenchResult>> setSetpoint,
        Func<int, CancellationToken, ValueTask<BenchResult<Temperature>>> measureTemperature,
        InstrumentBackend backend = InstrumentBackend.Physical,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _setSetpoint = setSetpoint;
        _measureTemperature = measureTemperature;
        Backend = backend;
        Calibration = calibration;
        Identity = new InstrumentIdentity("Generated", driverName, $"{backend}-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }

    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }

    /// <inheritdoc/>
    public InstrumentBackend Backend { get; }

    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }

    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "THERMAL", Identity, Backend, ["SetSetpoint", "MeasureTemperature", "SafeState"], Calibration);

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) =>
        _setSetpoint(1, Temperature.FromDegreesCelsius(25.0), cancellationToken).AsTask();

    /// <inheritdoc/>
    public Task<BenchResult> SetSetpointAsync(Temperature target, CancellationToken cancellationToken = default) =>
        _setSetpoint(1, target, cancellationToken).AsTask();

    /// <inheritdoc/>
    public Task<BenchResult<Temperature>> MeasureTemperatureAsync(CancellationToken cancellationToken = default) =>
        _measureTemperature(1, cancellationToken).AsTask();
}

/// <summary>Symmetric <see cref="IBenchFunctionGenerator"/> adapter generated for FGEN/AWG drivers.</summary>
/// <param name="role">Logical bench role name.</param>
/// <param name="driverName">Generated driver model name.</param>
/// <param name="session">Caller-owned SCPI session for output control.</param>
/// <param name="setFrequency">Delegate programming output frequency.</param>
/// <param name="getFrequency">Delegate querying output frequency.</param>
/// <param name="setAmplitude">Delegate programming output peak-to-peak amplitude.</param>
/// <param name="getAmplitude">Delegate querying output peak-to-peak amplitude.</param>
/// <param name="backend">Explicit backend provenance.</param>
/// <param name="calibration">Optional calibration certificate metadata.</param>
public sealed class GeneratedBenchFunctionGenerator(
    string role,
    string driverName,
    BenchForge.Async.IAsyncScpiSession session,
    Func<int, Frequency, CancellationToken, ValueTask<BenchResult>> setFrequency,
    Func<int, CancellationToken, ValueTask<BenchResult<Frequency>>> getFrequency,
    Func<int, ElectricPotential, CancellationToken, ValueTask<BenchResult>> setAmplitude,
    Func<int, CancellationToken, ValueTask<BenchResult<ElectricPotential>>> getAmplitude,
    InstrumentBackend backend = InstrumentBackend.Physical,
    InstrumentCalibrationInfo? calibration = null) : IBenchFunctionGenerator
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("Generated", driverName, $"{backend}-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend { get; } = backend;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() => new(Role, "FGEN", Identity, Backend, ["SetFrequency", "GetFrequency", "SetAmplitude", "GetAmplitude", "SetOutputEnabled", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => SetOutputEnabledAsync(false, 1, cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyAsync(Frequency frequency, int channel = 1, CancellationToken cancellationToken = default) => setFrequency(channel, frequency, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetFrequencyAsync(int channel = 1, CancellationToken cancellationToken = default) => getFrequency(channel, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult> SetAmplitudeAsync(ElectricPotential amplitude, int channel = 1, CancellationToken cancellationToken = default) => setAmplitude(channel, amplitude, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetAmplitudeAsync(int channel = 1, CancellationToken cancellationToken = default) => getAmplitude(channel, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default) =>
        session is BenchForge.Simulation.DefinitionDrivenScpiMockSession
            ? Task.FromResult(BenchResult.Ok())
            : GeneratedDriverRailroad.ExecuteWriteAsync(Identity.Model, "SetOutputEnabled", () => session.WriteAsync(enabled ? $"OUTP{channel} ON" : $"OUTP{channel} OFF", cancellationToken)).AsTask();
}

/// <summary>Symmetric <see cref="IBenchRfSignalGenerator"/> adapter generated for RF signal generator drivers.</summary>
/// <param name="role">Logical bench role name.</param>
/// <param name="driverName">Generated driver model name.</param>
/// <param name="session">Caller-owned SCPI session for RF output control.</param>
/// <param name="setFrequency">Delegate programming CW carrier frequency.</param>
/// <param name="getFrequency">Delegate querying CW carrier frequency.</param>
/// <param name="setPower">Delegate programming RF output power.</param>
/// <param name="getPower">Delegate querying RF output power.</param>
/// <param name="backend">Explicit backend provenance.</param>
/// <param name="calibration">Optional calibration certificate metadata.</param>
public sealed class GeneratedBenchRfSignalGenerator(
    string role,
    string driverName,
    BenchForge.Async.IAsyncScpiSession session,
    Func<int, Frequency, CancellationToken, ValueTask<BenchResult>> setFrequency,
    Func<int, CancellationToken, ValueTask<BenchResult<Frequency>>> getFrequency,
    Func<int, Power, CancellationToken, ValueTask<BenchResult>> setPower,
    Func<int, CancellationToken, ValueTask<BenchResult<Power>>> getPower,
    InstrumentBackend backend = InstrumentBackend.Physical,
    InstrumentCalibrationInfo? calibration = null) : IBenchRfSignalGenerator
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("Generated", driverName, $"{backend}-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend { get; } = backend;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() => new(Role, "RFSIGGEN", Identity, Backend, ["SetFrequency", "GetFrequency", "SetOutputPower", "GetOutputPower", "SetRfOutputEnabled", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => SetRfOutputEnabledAsync(false, cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default) => setFrequency(1, frequency, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetFrequencyAsync(CancellationToken cancellationToken = default) => getFrequency(1, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult> SetOutputPowerAsync(Power power, CancellationToken cancellationToken = default) => setPower(1, power, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<Power>> GetOutputPowerAsync(CancellationToken cancellationToken = default) => getPower(1, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult> SetRfOutputEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        session is BenchForge.Simulation.DefinitionDrivenScpiMockSession
            ? Task.FromResult(BenchResult.Ok())
            : GeneratedDriverRailroad.ExecuteWriteAsync(Identity.Model, "SetRfOutputEnabled", () => session.WriteAsync(enabled ? "OUTP ON" : "OUTP OFF", cancellationToken)).AsTask();
}

/// <summary>Symmetric <see cref="IBenchSpectrumAnalyzer"/> adapter generated for spectrum analyzer drivers.</summary>
/// <param name="role">Logical bench role name.</param>
/// <param name="driverName">Generated driver model name.</param>
/// <param name="setCenter">Delegate programming center frequency.</param>
/// <param name="getCenter">Delegate querying center frequency.</param>
/// <param name="setSpan">Delegate programming frequency span.</param>
/// <param name="setRbw">Delegate programming resolution bandwidth.</param>
/// <param name="backend">Explicit backend provenance.</param>
/// <param name="calibration">Optional calibration certificate metadata.</param>
public sealed class GeneratedBenchSpectrumAnalyzer(
    string role,
    string driverName,
    Func<int, Frequency, CancellationToken, ValueTask<BenchResult>> setCenter,
    Func<int, CancellationToken, ValueTask<BenchResult<Frequency>>> getCenter,
    Func<int, Frequency, CancellationToken, ValueTask<BenchResult>> setSpan,
    Func<int, Frequency, CancellationToken, ValueTask<BenchResult>> setRbw,
    InstrumentBackend backend = InstrumentBackend.Physical,
    InstrumentCalibrationInfo? calibration = null) : IBenchSpectrumAnalyzer
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("Generated", driverName, $"{backend}-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend { get; } = backend;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() => new(Role, "SPECAN", Identity, Backend, ["SetCenterFrequency", "SetSpan", "SetResolutionBandwidth", "GetCenterFrequency", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetCenterFrequencyAsync(Frequency center, CancellationToken cancellationToken = default) => setCenter(1, center, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult> SetSpanAsync(Frequency span, CancellationToken cancellationToken = default) => setSpan(1, span, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult> SetResolutionBandwidthAsync(Frequency rbw, CancellationToken cancellationToken = default) => setRbw(1, rbw, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetCenterFrequencyAsync(CancellationToken cancellationToken = default) => getCenter(1, cancellationToken).AsTask();
}

/// <summary>Symmetric <see cref="IBenchElectronicLoad"/> adapter generated for programmable DC electronic load drivers.</summary>
/// <param name="role">Logical bench role name.</param>
/// <param name="driverName">Generated driver model name.</param>
/// <param name="session">Caller-owned SCPI session for load input enable control.</param>
/// <param name="setCurrentSink">Delegate programming constant-current sink setpoint.</param>
/// <param name="setResistanceLoad">Delegate programming constant-resistance load setpoint.</param>
/// <param name="measureVoltage">Delegate measuring input voltage.</param>
/// <param name="measureCurrent">Delegate measuring input current.</param>
/// <param name="measurePower">Delegate measuring absorbed DC power.</param>
/// <param name="backend">Explicit backend provenance.</param>
/// <param name="calibration">Optional calibration certificate metadata.</param>
public sealed class GeneratedBenchElectronicLoad(
    string role,
    string driverName,
    BenchForge.Async.IAsyncScpiSession session,
    Func<int, ElectricCurrent, CancellationToken, ValueTask<BenchResult>> setCurrentSink,
    Func<int, ElectricResistance, CancellationToken, ValueTask<BenchResult>> setResistanceLoad,
    Func<int, CancellationToken, ValueTask<BenchResult<ElectricPotential>>> measureVoltage,
    Func<int, CancellationToken, ValueTask<BenchResult<ElectricCurrent>>> measureCurrent,
    Func<int, CancellationToken, ValueTask<BenchResult<Power>>> measurePower,
    InstrumentBackend backend = InstrumentBackend.Physical,
    InstrumentCalibrationInfo? calibration = null) : IBenchElectronicLoad
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("Generated", driverName, $"{backend}-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend { get; } = backend;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() => new(Role, "ELOAD", Identity, Backend, ["SetCurrentSink", "SetResistanceLoad", "MeasureVoltage", "MeasureCurrent", "MeasurePower", "SetInputEnabled", "SafeState"], Calibration);
    /// <inheritdoc/>
    public async Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        var zero = await setCurrentSink(1, ElectricCurrent.Zero, cancellationToken).ConfigureAwait(false);
        var off = await SetInputEnabledAsync(false, 1, cancellationToken).ConfigureAwait(false);
        return zero.IsFailure ? zero : off;
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetCurrentSinkAsync(ElectricCurrent current, int channel = 1, CancellationToken cancellationToken = default) => setCurrentSink(channel, current, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult> SetResistanceLoadAsync(ElectricResistance resistance, int channel = 1, CancellationToken cancellationToken = default) => setResistanceLoad(channel, resistance, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) => measureVoltage(channel, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default) => measureCurrent(channel, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<Power>> MeasurePowerAsync(int channel = 1, CancellationToken cancellationToken = default) => measurePower(channel, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult> SetInputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default) =>
        session is BenchForge.Simulation.DefinitionDrivenScpiMockSession
            ? Task.FromResult(BenchResult.Ok())
            : GeneratedDriverRailroad.ExecuteWriteAsync(Identity.Model, "SetInputEnabled", () => session.WriteAsync(enabled ? "INP ON" : "INP OFF", cancellationToken)).AsTask();
}

/// <summary>Symmetric <see cref="IBenchRfPowerMeter"/> adapter generated for RF power meter drivers.</summary>
/// <param name="role">Logical bench role name.</param>
/// <param name="driverName">Generated driver model name.</param>
/// <param name="setFrequency">Delegate programming sensor calibration frequency.</param>
/// <param name="measurePower">Delegate measuring RF power.</param>
/// <param name="backend">Explicit backend provenance.</param>
/// <param name="calibration">Optional calibration certificate metadata.</param>
public sealed class GeneratedBenchRfPowerMeter(
    string role,
    string driverName,
    Func<int, Frequency, CancellationToken, ValueTask<BenchResult>> setFrequency,
    Func<int, CancellationToken, ValueTask<BenchResult<Power>>> measurePower,
    InstrumentBackend backend = InstrumentBackend.Physical,
    InstrumentCalibrationInfo? calibration = null) : IBenchRfPowerMeter
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("Generated", driverName, $"{backend}-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend { get; } = backend;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() => new(Role, "PWRMETER", Identity, Backend, ["SetFrequency", "MeasurePower", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyAsync(Frequency frequency, int channel = 1, CancellationToken cancellationToken = default) => setFrequency(channel, frequency, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<Power>> MeasurePowerAsync(int channel = 1, CancellationToken cancellationToken = default) => measurePower(channel, cancellationToken).AsTask();
}

/// <summary>Symmetric <see cref="IBenchFrequencyCounter"/> adapter generated for universal frequency counter drivers.</summary>
/// <param name="role">Logical bench role name.</param>
/// <param name="driverName">Generated driver model name.</param>
/// <param name="measureFrequency">Delegate measuring input signal frequency.</param>
/// <param name="measurePeriod">Delegate measuring input signal period.</param>
/// <param name="backend">Explicit backend provenance.</param>
/// <param name="calibration">Optional calibration certificate metadata.</param>
public sealed class GeneratedBenchFrequencyCounter(
    string role,
    string driverName,
    Func<int, CancellationToken, ValueTask<BenchResult<Frequency>>> measureFrequency,
    Func<int, CancellationToken, ValueTask<BenchResult<Duration>>> measurePeriod,
    InstrumentBackend backend = InstrumentBackend.Physical,
    InstrumentCalibrationInfo? calibration = null) : IBenchFrequencyCounter
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("Generated", driverName, $"{backend}-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend { get; } = backend;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() => new(Role, "COUNTER", Identity, Backend, ["MeasureFrequency", "MeasurePeriod", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> MeasureFrequencyAsync(int channel = 1, CancellationToken cancellationToken = default) => measureFrequency(channel, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<Duration>> MeasurePeriodAsync(int channel = 1, CancellationToken cancellationToken = default) => measurePeriod(channel, cancellationToken).AsTask();
}

/// <summary>Symmetric <see cref="IBenchLcrMeter"/> adapter generated for LCR / impedance meter drivers.</summary>
/// <param name="role">Logical bench role name.</param>
/// <param name="driverName">Generated driver model name.</param>
/// <param name="setTestFreq">Delegate programming AC test stimulus frequency.</param>
/// <param name="setTestVolt">Delegate programming AC test stimulus voltage.</param>
/// <param name="measureCap">Delegate measuring capacitance.</param>
/// <param name="measureRes">Delegate measuring equivalent series resistance.</param>
/// <param name="backend">Explicit backend provenance.</param>
/// <param name="calibration">Optional calibration certificate metadata.</param>
public sealed class GeneratedBenchLcrMeter(
    string role,
    string driverName,
    Func<int, Frequency, CancellationToken, ValueTask<BenchResult>> setTestFreq,
    Func<int, ElectricPotential, CancellationToken, ValueTask<BenchResult>> setTestVolt,
    Func<int, CancellationToken, ValueTask<BenchResult<ElectricCapacitance>>> measureCap,
    Func<int, CancellationToken, ValueTask<BenchResult<ElectricResistance>>> measureRes,
    InstrumentBackend backend = InstrumentBackend.Physical,
    InstrumentCalibrationInfo? calibration = null) : IBenchLcrMeter
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("Generated", driverName, $"{backend}-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend { get; } = backend;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() => new(Role, "LCR", Identity, Backend, ["SetTestFrequency", "SetTestVoltage", "MeasureCapacitance", "MeasureResistance", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetTestFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default) => setTestFreq(1, frequency, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult> SetTestVoltageAsync(ElectricPotential voltage, CancellationToken cancellationToken = default) => setTestVolt(1, voltage, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCapacitance>> MeasureCapacitanceAsync(CancellationToken cancellationToken = default) => measureCap(1, cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default) => measureRes(1, cancellationToken).AsTask();
}
