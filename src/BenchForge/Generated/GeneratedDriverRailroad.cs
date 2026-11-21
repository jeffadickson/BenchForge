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
