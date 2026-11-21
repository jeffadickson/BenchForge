using System.Globalization;
using BenchForge.Async;
using BenchForge.Contracts;
using BenchForge.Outcomes;
using UnitsNet;

namespace BenchForge.Interfaces;

/// <summary>Calibration record attached to an instrument role for flight-hardware qualification traceability.</summary>
/// <param name="CertificateId">Calibration certificate or asset tracking identifier.</param>
/// <param name="CalibratedUtc">UTC date of last calibration.</param>
/// <param name="DueUtc">UTC expiration date after which flight runs must fail preflight.</param>
public sealed record InstrumentCalibrationInfo(
    string CertificateId,
    DateTimeOffset CalibratedUtc,
    DateTimeOffset DueUtc)
{
    /// <summary>Return true when calibration is valid at <paramref name="utcNow"/>.</summary>
    /// <param name="utcNow">Current UTC timestamp.</param>
    /// <returns>True when <paramref name="utcNow"/> is strictly before <see cref="DueUtc"/>.</returns>
    public bool IsCurrent(DateTimeOffset utcNow) => utcNow < DueUtc;
}

/// <summary>Machine-readable descriptor of a bench instrument role for human inspection and AI-agent discovery.</summary>
/// <param name="Role">Logical bench role identifier (for example, <c>SMU1</c>, <c>DMM_RAIL</c>).</param>
/// <param name="InstrumentClass">Canonical interface class (<c>DMM</c>, <c>PSU</c>, <c>SMU</c>, <c>FGEN</c>, <c>THERMAL</c>).</param>
/// <param name="Identity">Instrument manufacturer, model, serial, and firmware.</param>
/// <param name="Backend">Explicit provenance (<see cref="InstrumentBackend.Physical"/>, <see cref="InstrumentBackend.Simulated"/>, or <see cref="InstrumentBackend.Mock"/>).</param>
/// <param name="Capabilities">Supported typed operations.</param>
/// <param name="Calibration">Optional calibration certificate and due date.</param>
public sealed record BenchInstrumentDescriptor(
    string Role,
    string InstrumentClass,
    InstrumentIdentity Identity,
    InstrumentBackend Backend,
    IReadOnlyList<string> Capabilities,
    InstrumentCalibrationInfo? Calibration = null);

/// <summary>
/// Canonical base interface shared symmetrically by physical drivers, generated drivers, remote proxies, and stateful mocks.
/// </summary>
public interface IBenchInstrument
{
    /// <summary>Logical bench role name bound to this instrument.</summary>
    string Role { get; }

    /// <summary>Instrument manufacturer, model, serial, and firmware identity.</summary>
    InstrumentIdentity Identity { get; }

    /// <summary>Explicit backend provenance so mocks never masquerade as physical hardware.</summary>
    InstrumentBackend Backend { get; }

    /// <summary>Optional calibration metadata tracked for flight-hardware evidence bundles.</summary>
    InstrumentCalibrationInfo? Calibration { get; }

    /// <summary>Return a machine-readable descriptor of this instrument's capabilities.</summary>
    /// <returns>Immutable instrument descriptor.</returns>
    BenchInstrumentDescriptor Describe();

    /// <summary>Transition the instrument to a safe idle state (outputs disabled where applicable).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result confirming safe-state transition or detailing required recovery.</returns>
    Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async digital multimeter interface shared by real drivers and stateful mocks.</summary>
public interface IBenchDigitalMultimeter : IBenchInstrument
{
    /// <summary>Measure DC voltage in canonical <see cref="ElectricPotential"/> units.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing measured DC voltage or a structured <see cref="BenchError"/>.</returns>
    Task<BenchResult<ElectricPotential>> MeasureDcVoltageAsync(CancellationToken cancellationToken = default);

    /// <summary>Measure DC current in canonical <see cref="ElectricCurrent"/> units.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing measured DC current or a structured <see cref="BenchError"/>.</returns>
    Task<BenchResult<ElectricCurrent>> MeasureDcCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Measure resistance in canonical <see cref="ElectricResistance"/> units.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing measured resistance or a structured <see cref="BenchError"/>.</returns>
    Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async DC power supply interface shared by real drivers and stateful mocks.</summary>
public interface IBenchPowerSupply : IBenchInstrument
{
    /// <summary>Program output voltage setpoint.</summary>
    /// <param name="voltage">Requested voltage setpoint.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result indicating transport acceptance or violation.</returns>
    Task<BenchResult> SetVoltageAsync(ElectricPotential voltage, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Query the programmed output voltage setpoint.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing programmed voltage.</returns>
    Task<BenchResult<ElectricPotential>> GetVoltageAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Program current limit (compliance ceiling).</summary>
    /// <param name="currentLimit">Maximum allowed output current.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result indicating acceptance or violation.</returns>
    Task<BenchResult> SetCurrentLimitAsync(ElectricCurrent currentLimit, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Query the programmed current limit.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing programmed current limit.</returns>
    Task<BenchResult<ElectricCurrent>> GetCurrentLimitAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Measure actual output voltage at the terminals.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing measured terminal voltage.</returns>
    Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Measure actual output current delivered to the load.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing measured load current.</returns>
    Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Enable or disable power output on the specified channel.</summary>
    /// <param name="enabled">True to energize output; false to de-energize.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result indicating state transition or interlock denial.</returns>
    Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Query whether output is currently enabled.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing output enabled state.</returns>
    Task<BenchResult<bool>> GetOutputEnabledAsync(int channel = 1, CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async Source-Measure Unit (SMU) interface extending <see cref="IBenchPowerSupply"/>.</summary>
public interface IBenchSourceMeasureUnit : IBenchPowerSupply
{
    /// <summary>Query whether the SMU has tripped its current or voltage compliance limit.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result true when compliance is actively clamped.</returns>
    Task<BenchResult<bool>> IsComplianceTrippedAsync(int channel = 1, CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async thermal chamber / temperature controller interface shared by real drivers and stateful mocks.</summary>
public interface IBenchThermalController : IBenchInstrument
{
    /// <summary>Program target temperature setpoint.</summary>
    /// <param name="target">Target temperature.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result confirming setpoint acceptance.</returns>
    Task<BenchResult> SetSetpointAsync(Temperature target, CancellationToken cancellationToken = default);

    /// <summary>Measure current chamber or sensor temperature.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing measured temperature.</returns>
    Task<BenchResult<Temperature>> MeasureTemperatureAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Test-only fault-injection and physics-control interface implemented by stateful mocks so qualification suites
/// can simulate link drops, compliance trips, and sensor drift without leaking test hooks onto <see cref="IBenchInstrument"/>.
/// </summary>
public interface IBenchMockControl
{
    /// <summary>Inject a one-shot structured fault returned on the next instrument operation.</summary>
    /// <param name="fault">Structured fault to return, or null to clear.</param>
    void InjectNextFault(BenchError? fault);

    /// <summary>Clear any injected faults and restore default mock physics.</summary>
    void ClearFaults();
}

/// <summary>
/// High-fidelity stateful mock digital multimeter implementing <see cref="IBenchDigitalMultimeter"/> and <see cref="IBenchMockControl"/>.
/// </summary>
public sealed class StatefulMockDigitalMultimeter : IBenchDigitalMultimeter, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _dcVoltageVolts;
    private double _dcCurrentAmperes;
    private double _resistanceOhms;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock DMM with optional nominal readings and calibration metadata.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="dcVoltageVolts">Initial DC voltage reading in volts.</param>
    /// <param name="dcCurrentAmperes">Initial DC current reading in amperes.</param>
    /// <param name="resistanceOhms">Initial resistance reading in ohms.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public StatefulMockDigitalMultimeter(
        string role,
        double dcVoltageVolts = 0.0,
        double dcCurrentAmperes = 0.0,
        double resistanceOhms = 100.0,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _dcVoltageVolts = dcVoltageVolts;
        _dcCurrentAmperes = dcCurrentAmperes;
        _resistanceOhms = resistanceOhms;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockDMM-6.5", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }

    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }

    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;

    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }

    /// <summary>Update the simulated DC voltage reading presented on subsequent queries.</summary>
    /// <param name="voltage">Simulated voltage.</param>
    public void SetSimulatedVoltage(ElectricPotential voltage) { using (_gate.EnterScope()) _dcVoltageVolts = voltage.Volts; }

    /// <summary>Update the simulated DC current reading presented on subsequent queries.</summary>
    /// <param name="current">Simulated current.</param>
    public void SetSimulatedCurrent(ElectricCurrent current) { using (_gate.EnterScope()) _dcCurrentAmperes = current.Amperes; }

    /// <summary>Update the simulated resistance reading presented on subsequent queries.</summary>
    /// <param name="resistance">Simulated resistance.</param>
    public void SetSimulatedResistance(ElectricResistance resistance) { using (_gate.EnterScope()) _resistanceOhms = resistance.Ohms; }

    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }

    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }

    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "DMM", Identity, Backend, ["MeasureDcVoltage", "MeasureDcCurrent", "MeasureResistance", "SafeState"], Calibration);

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureDcVoltageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<ElectricPotential>.Fail(err));
            return Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(_dcVoltageVolts)));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureDcCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<ElectricCurrent>.Fail(err));
            return Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(_dcCurrentAmperes)));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<ElectricResistance>.Fail(err));
            return Task.FromResult(BenchResult<ElectricResistance>.Ok(ElectricResistance.FromOhms(_resistanceOhms)));
        }
    }

    private BenchError? ConsumeFault()
    {
        var fault = _nextFault;
        _nextFault = null;
        return fault;
    }
}

/// <summary>
/// High-fidelity stateful mock SMU modeling Ohm's law load current, current-compliance clamping, and fault injection.
/// </summary>
public sealed class StatefulMockSourceMeasureUnit : IBenchSourceMeasureUnit, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _loadResistanceOhms;
    private double _voltageSetpointVolts;
    private double _currentLimitAmperes;
    private bool _outputEnabled;
    private bool _complianceConfigured;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock SMU connected to a simulated resistive load.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="loadResistanceOhms">Simulated load resistance in ohms (must be positive).</param>
    /// <param name="defaultCurrentLimitAmperes">Initial compliance current ceiling in amperes.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockSourceMeasureUnit(
        string role,
        double loadResistanceOhms = 10.0,
        double defaultCurrentLimitAmperes = 0.1,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (!double.IsFinite(loadResistanceOhms) || loadResistanceOhms <= 0)
            throw new ArgumentOutOfRangeException(nameof(loadResistanceOhms), "Load resistance must be positive.");
        if (!double.IsFinite(defaultCurrentLimitAmperes) || defaultCurrentLimitAmperes <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultCurrentLimitAmperes), "Current limit must be positive.");

        Role = role;
        _loadResistanceOhms = loadResistanceOhms;
        _currentLimitAmperes = defaultCurrentLimitAmperes;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockSMU-2450", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }

    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }

    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;

    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }

    /// <summary>True once <see cref="SetCurrentLimitAsync"/> has been explicitly called.</summary>
    public bool ComplianceConfigured { get { using (_gate.EnterScope()) return _complianceConfigured; } }

    /// <summary>Dynamically alter the simulated DUT load resistance (for example, to inject a short-circuit fault).</summary>
    /// <param name="ohms">New positive load resistance in ohms.</param>
    public void SetSimulatedLoadResistance(double ohms)
    {
        if (!double.IsFinite(ohms) || ohms <= 0)
            throw new ArgumentOutOfRangeException(nameof(ohms), "Load resistance must be positive.");
        using (_gate.EnterScope()) _loadResistanceOhms = ohms;
    }

    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }

    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }

    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SMU", Identity, Backend,
            ["SetVoltage", "GetVoltage", "SetCurrentLimit", "GetCurrentLimit", "MeasureVoltage", "MeasureCurrent", "SetOutputEnabled", "IsComplianceTripped", "SafeState"],
            Calibration);

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.EnterScope())
        {
            _outputEnabled = false;
            _voltageSetpointVolts = 0.0;
        }
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetVoltageAsync(ElectricPotential voltage, int channel = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != 1) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-CHAN", $"Unsupported channel {channel}.", Role)));
        if (!double.IsFinite(voltage.Volts)) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-NAN", "Voltage must be finite.", Role)));
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult.Fail(err));
            _voltageSetpointVolts = voltage.Volts;
            return Task.FromResult(BenchResult.Ok());
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetVoltageAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != 1) return Task.FromResult(BenchResult<ElectricPotential>.Fail(BenchError.Validation("BF-CHAN", $"Unsupported channel {channel}.", Role)));
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<ElectricPotential>.Fail(err));
            return Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(_voltageSetpointVolts)));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetCurrentLimitAsync(ElectricCurrent currentLimit, int channel = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != 1) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-CHAN", $"Unsupported channel {channel}.", Role)));
        if (!double.IsFinite(currentLimit.Amperes) || currentLimit.Amperes <= 0)
            return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-LIMIT", "Current limit must be strictly positive.", Role)));
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult.Fail(err));
            _currentLimitAmperes = currentLimit.Amperes;
            _complianceConfigured = true;
            return Task.FromResult(BenchResult.Ok());
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> GetCurrentLimitAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != 1) return Task.FromResult(BenchResult<ElectricCurrent>.Fail(BenchError.Validation("BF-CHAN", $"Unsupported channel {channel}.", Role)));
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<ElectricCurrent>.Fail(err));
            return Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(_currentLimitAmperes)));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != 1) return Task.FromResult(BenchResult<ElectricPotential>.Fail(BenchError.Validation("BF-CHAN", $"Unsupported channel {channel}.", Role)));
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<ElectricPotential>.Fail(err));
            var (_, actualV, _) = ComputeTerminalPhysics();
            return Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(actualV)));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != 1) return Task.FromResult(BenchResult<ElectricCurrent>.Fail(BenchError.Validation("BF-CHAN", $"Unsupported channel {channel}.", Role)));
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<ElectricCurrent>.Fail(err));
            var (actualI, _, _) = ComputeTerminalPhysics();
            return Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(actualI)));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != 1) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-CHAN", $"Unsupported channel {channel}.", Role)));
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult.Fail(err));
            _outputEnabled = enabled;
            return Task.FromResult(BenchResult.Ok());
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<bool>> GetOutputEnabledAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != 1) return Task.FromResult(BenchResult<bool>.Fail(BenchError.Validation("BF-CHAN", $"Unsupported channel {channel}.", Role)));
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<bool>.Fail(err));
            return Task.FromResult(BenchResult<bool>.Ok(_outputEnabled));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<bool>> IsComplianceTrippedAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != 1) return Task.FromResult(BenchResult<bool>.Fail(BenchError.Validation("BF-CHAN", $"Unsupported channel {channel}.", Role)));
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<bool>.Fail(err));
            var (_, _, tripped) = ComputeTerminalPhysics();
            return Task.FromResult(BenchResult<bool>.Ok(tripped));
        }
    }

    private (double CurrentA, double VoltageV, bool ComplianceTripped) ComputeTerminalPhysics()
    {
        if (!_outputEnabled)
            return (0.0, 0.0, false);

        double idealI = _voltageSetpointVolts / _loadResistanceOhms;
        if (Math.Abs(idealI) > _currentLimitAmperes)
        {
            double clampedI = Math.Sign(idealI) * _currentLimitAmperes;
            return (clampedI, clampedI * _loadResistanceOhms, true);
        }
        return (idealI, _voltageSetpointVolts, false);
    }

    private BenchError? ConsumeFault()
    {
        var fault = _nextFault;
        _nextFault = null;
        return fault;
    }
}

/// <summary>Stateful mock thermal chamber controller implementing <see cref="IBenchThermalController"/> and <see cref="IBenchMockControl"/>.</summary>
public sealed class StatefulMockThermalController : IBenchThermalController, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _temperatureCelsius;
    private double _setpointCelsius;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock thermal chamber controller.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="initialTempCelsius">Initial chamber temperature in degrees Celsius.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    public StatefulMockThermalController(
        string role,
        double initialTempCelsius = 25.0,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _temperatureCelsius = initialTempCelsius;
        _setpointCelsius = initialTempCelsius;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockThermal-336", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }

    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }

    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;

    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }

    /// <summary>Step the simulated chamber temperature toward the active setpoint.</summary>
    /// <param name="fraction">Fraction in (0, 1] of the remaining delta to close.</param>
    public void AdvanceThermalResponse(double fraction = 1.0)
    {
        if (!double.IsFinite(fraction) || fraction <= 0 || fraction > 1.0)
            throw new ArgumentOutOfRangeException(nameof(fraction), "Fraction must be in (0, 1].");
        using (_gate.EnterScope())
            _temperatureCelsius += (_setpointCelsius - _temperatureCelsius) * fraction;
    }

    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }

    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }

    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "THERMAL", Identity, Backend, ["SetSetpoint", "MeasureTemperature", "SafeState"], Calibration);

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.EnterScope()) _setpointCelsius = 25.0;
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetSetpointAsync(Temperature target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult.Fail(err));
            _setpointCelsius = target.DegreesCelsius;
            return Task.FromResult(BenchResult.Ok());
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<Temperature>> MeasureTemperatureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } err) return Task.FromResult(BenchResult<Temperature>.Fail(err));
            return Task.FromResult(BenchResult<Temperature>.Ok(Temperature.FromDegreesCelsius(_temperatureCelsius)));
        }
    }

    private BenchError? ConsumeFault()
    {
        var fault = _nextFault;
        _nextFault = null;
        return fault;
    }
}

/// <summary>
/// Symmetric SCPI-backed <see cref="IBenchDigitalMultimeter"/> adapter that talks over any <see cref="IAsyncScpiSession"/>
/// (pure-C# TCP, guarded session, or mock) and returns railroad <see cref="BenchResult{T}"/> outcomes.
/// </summary>
public sealed class ScpiDigitalMultimeterClient : IBenchDigitalMultimeter
{
    private readonly IAsyncScpiSession _session;

    /// <summary>Create a railroad-oriented SCPI DMM client over a caller-owned <see cref="IAsyncScpiSession"/>.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="session">Caller-owned async SCPI session.</param>
    /// <param name="identity">Instrument identity metadata.</param>
    /// <param name="backend">Explicit backend provenance.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public ScpiDigitalMultimeterClient(
        string role,
        IAsyncScpiSession session,
        InstrumentIdentity identity,
        InstrumentBackend backend = InstrumentBackend.Physical,
        InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Backend = backend;
        Calibration = calibration;
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
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(BenchResult.Ok());

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureDcVoltageAsync(CancellationToken cancellationToken = default) =>
        QueryQuantityAsync("MEAS:VOLT:DC?", v => ElectricPotential.FromVolts(v), cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureDcCurrentAsync(CancellationToken cancellationToken = default) =>
        QueryQuantityAsync("MEAS:CURR:DC?", v => ElectricCurrent.FromAmperes(v), cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default) =>
        QueryQuantityAsync("MEAS:RES?", v => ElectricResistance.FromOhms(v), cancellationToken);

    private async Task<BenchResult<T>> QueryQuantityAsync<T>(string scpi, Func<double, T> convert, CancellationToken ct)
    {
        try
        {
            var raw = await _session.QueryAsync(scpi, ct).ConfigureAwait(false);
            if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var val) || !double.IsFinite(val))
            {
                return BenchResult<T>.Fail(new BenchError(
                    "BF-SCPI-PARSE",
                    BenchErrorKind.ProtocolFault,
                    $"DMM '{Role}' returned a non-numeric response to '{scpi}'.",
                    Role,
                    Confidence: PhysicalStateConfidence.Unchanged,
                    Remedy: "Inspect instrument measurement mode and SCPI error queue."));
            }
            return BenchResult<T>.Ok(convert(val));
        }
        catch (OperationGuardDeniedException ex)
        {
            return BenchResult<T>.Fail(new BenchError(ex.Decision.RuleId, BenchErrorKind.GuardDenied, ex.Message, Role, Remedy: "Satisfy the active operation guard."));
        }
        catch (TimeoutException ex)
        {
            return BenchResult<T>.Fail(new BenchError("BF-SCPI-TIMEOUT", BenchErrorKind.Timeout, ex.Message, Role, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, Remedy: "Verify instrument connectivity and trigger configuration."));
        }
        catch (OperationCanceledException)
        {
            return BenchResult<T>.Fail(new BenchError("BF-SCPI-CANCEL", BenchErrorKind.CancelledBeforeDispatch, $"Query '{scpi}' on '{Role}' was cancelled.", Role));
        }
        catch (Exception ex)
        {
            return BenchResult<T>.Fail(new BenchError("BF-SCPI-IO", BenchErrorKind.TransportFault, ex.Message, Role, Confidence: PhysicalStateConfidence.UnknownRequiresRecovery, Remedy: "Check physical bus/TCP connection and reconnect explicitly."));
        }
    }
}
