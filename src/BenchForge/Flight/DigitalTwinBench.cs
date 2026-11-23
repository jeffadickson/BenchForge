using BenchForge.Contracts;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using UnitsNet;

namespace BenchForge.Flight;

/// <summary>
/// Physical parameters for a time-driven closed-loop spacecraft Unit-Under-Test (UUT) digital twin.
/// Couples power supply, electronic load, thermal chamber, switch matrix, DMM, and oscilloscope physics
/// on the shared canonical <see cref="BenchForge.Simulation.ISimulationClock"/>.
/// </summary>
/// <param name="BaseLoadResistanceOhms">Nominal UUT electrical resistance at 25 C in ohms.</param>
/// <param name="TempCoeffPerCelsius">Fractional resistance temperature coefficient per degree Celsius (e.g., <c>0.0039</c> for copper).</param>
/// <param name="StandbyCurrentAmperes">Constant quiescent current draw when bus voltage is above 3 V.</param>
/// <param name="BusSeriesResistanceOhms">Harness series resistance causing realistic IR voltage droop under load.</param>
/// <param name="SwitchingRippleVolts">Peak sinusoidal switching-regulator ripple on the bus when energized.</param>
/// <param name="InitialTemperatureCelsius">Initial chamber and UUT temperature in degrees Celsius.</param>
/// <param name="ThermalTimeConstantSeconds">First-order chamber thermal time constant tau in seconds.</param>
/// <param name="SelfHeatingCelsiusPerWatt">Steady-state UUT temperature rise per watt of dissipated power.</param>
public sealed record DigitalTwinParameters(
    double BaseLoadResistanceOhms = 14.0,
    double TempCoeffPerCelsius = 0.0035,
    double StandbyCurrentAmperes = 0.25,
    double BusSeriesResistanceOhms = 0.08,
    double SwitchingRippleVolts = 0.015,
    double InitialTemperatureCelsius = 25.0,
    double ThermalTimeConstantSeconds = 60.0,
    double SelfHeatingCelsiusPerWatt = 0.35);

/// <summary>
/// Shared closed-loop physics state coupling power, load, thermal, switch routing, DMM, and oscilloscope
/// instruments together under a single <see cref="BenchForge.Simulation.AdjustableSimulationClock"/>.
/// </summary>
public sealed class DigitalTwinPlant : ISimulationTimeAware
{
    private readonly Lock _gate = new();
    private readonly DigitalTwinParameters _p;
    private double _psuSetpointV;
    private double _psuCurrentLimitA = 1.0;
    private bool _psuOutputOn;
    private double _eloadSinkA;
    private double? _eloadResOhm;
    private bool _eloadInputOn;
    private double _chamberSetpointC;
    private double _currentTempC;
    private TimeSpan _elapsed;

    /// <summary>Create a closed-loop spacecraft UUT digital twin with the given physical parameters.</summary>
    /// <param name="parameters">Validated physical plant parameters.</param>
    public DigitalTwinPlant(DigitalTwinParameters? parameters = null)
    {
        _p = parameters ?? new DigitalTwinParameters();
        _chamberSetpointC = _p.InitialTemperatureCelsius;
        _currentTempC = _p.InitialTemperatureCelsius;
    }

    /// <summary>Current UUT temperature in degrees Celsius.</summary>
    public double CurrentTemperatureC { get { using (_gate.EnterScope()) return _currentTempC; } }

    /// <summary>Current logical simulation time observed by this plant.</summary>
    public TimeSpan Elapsed { get { using (_gate.EnterScope()) return _elapsed; } }

    /// <summary>Advance closed-loop thermal and electrical physics by <paramref name="delta"/> using exponential integration.</summary>
    /// <param name="elapsed">Current monotonic simulation elapsed time.</param>
    /// <param name="delta">Non-negative elapsed increment.</param>
    public void OnSimulationTimeAdvanced(TimeSpan elapsed, TimeSpan delta)
    {
        using (_gate.EnterScope())
        {
            _elapsed = elapsed;
            double dt = delta.TotalSeconds;
            if (dt <= 0) return;

            var (_, uutPowerW, _, _, _) = ComputeElectricalLocked();
            double equilibriumC = _chamberSetpointC + uutPowerW * _p.SelfHeatingCelsiusPerWatt;
            double tau = Math.Max(0.1, _p.ThermalTimeConstantSeconds);
            double alpha = 1.0 - Math.Exp(-dt / tau);
            _currentTempC += alpha * (equilibriumC - _currentTempC);
        }
    }

    internal void SetPsuVoltage(double volts) { using (_gate.EnterScope()) _psuSetpointV = volts; }
    internal double GetPsuVoltage() { using (_gate.EnterScope()) return _psuSetpointV; }
    internal void SetPsuCurrentLimit(double amps) { using (_gate.EnterScope()) _psuCurrentLimitA = amps; }
    internal double GetPsuCurrentLimit() { using (_gate.EnterScope()) return _psuCurrentLimitA; }
    internal void SetPsuOutput(bool enabled) { using (_gate.EnterScope()) _psuOutputOn = enabled; }
    internal bool GetPsuOutput() { using (_gate.EnterScope()) return _psuOutputOn; }
    internal void SetThermalSetpoint(double celsius) { using (_gate.EnterScope()) _chamberSetpointC = celsius; }
    internal void SetEloadCurrent(double amps) { using (_gate.EnterScope()) { _eloadSinkA = amps; _eloadResOhm = null; } }
    internal void SetEloadResistance(double ohms) { using (_gate.EnterScope()) { _eloadResOhm = ohms; _eloadSinkA = 0.0; } }
    internal void SetEloadInput(bool enabled) { using (_gate.EnterScope()) _eloadInputOn = enabled; }

    internal (double BusVolts, double UutPowerW, double TotalAmps, double EloadAmps, bool ComplianceTripped) SampleElectrical()
    {
        using (_gate.EnterScope()) return ComputeElectricalLocked();
    }

    internal double[] SampleBusWaveform(int sampleCount = 512)
    {
        using (_gate.EnterScope())
        {
            var (busV, _, totalA, _, _) = ComputeElectricalLocked();
            double ripple = _psuOutputOn ? _p.SwitchingRippleVolts * (1.0 + 0.1 * totalA) : 0.0005;
            double phaseOffset = _elapsed.TotalSeconds * 2.0 * Math.PI * 50.0;
            var samples = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                double theta = phaseOffset + (2.0 * Math.PI * 8.0 * i) / sampleCount;
                samples[i] = busV + ripple * Math.Sin(theta);
            }
            return samples;
        }
    }

    internal double EffectiveUutResistanceOhms()
    {
        using (_gate.EnterScope())
            return Math.Max(0.1, _p.BaseLoadResistanceOhms * (1.0 + _p.TempCoeffPerCelsius * (_currentTempC - 25.0)));
    }

    private (double BusVolts, double UutPowerW, double TotalAmps, double EloadAmps, bool ComplianceTripped) ComputeElectricalLocked()
    {
        if (!_psuOutputOn || _psuSetpointV <= 0.0)
            return (0.0, 0.0, 0.0, 0.0, false);

        double rUut = Math.Max(0.1, _p.BaseLoadResistanceOhms * (1.0 + _p.TempCoeffPerCelsius * (_currentTempC - 25.0)));
        double standby = _psuSetpointV >= 3.0 ? _p.StandbyCurrentAmperes : 0.0;
        double uutA = _psuSetpointV / rUut + standby;
        double eloadA = 0.0;
        if (_eloadInputOn)
            eloadA = _eloadResOhm is { } rLoad ? _psuSetpointV / Math.Max(0.01, rLoad) : _eloadSinkA;

        double requestedA = uutA + eloadA;
        bool tripped = requestedA > _psuCurrentLimitA;
        double actualA = tripped ? _psuCurrentLimitA : requestedA;
        double busV = Math.Max(0.0, (tripped ? _psuSetpointV * (actualA / Math.Max(1e-9, requestedA)) : _psuSetpointV) - actualA * _p.BusSeriesResistanceOhms);
        double uutShare = requestedA > 0 ? actualA * (uutA / requestedA) : 0.0;
        double eloadShare = requestedA > 0 ? actualA * (eloadA / requestedA) : 0.0;
        return (busV, busV * uutShare, actualA, eloadShare, tripped);
    }
}

/// <summary>Closed-loop coupled SMU/PSU backed by a shared <see cref="DigitalTwinPlant"/>.</summary>
/// <param name="role">Logical bench role identifier.</param>
/// <param name="plant">Shared digital-twin physics plant.</param>
/// <param name="calibration">Optional calibration metadata.</param>
public sealed class CoupledTwinPowerSupply(
    string role,
    DigitalTwinPlant plant,
    InstrumentCalibrationInfo? calibration = null) : IBenchSourceMeasureUnit, ISimulationTimeAware
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("BenchForge-Twin", "CoupledTwinPowerSupply", $"TWIN-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SMU", Identity, Backend, ["SetVoltage", "GetVoltage", "SetCurrentLimit", "GetCurrentLimit", "MeasureVoltage", "MeasureCurrent", "SetOutputEnabled", "IsComplianceTripped", "SafeState", "DigitalTwin"], Calibration);
    /// <inheritdoc/>
    public void OnSimulationTimeAdvanced(TimeSpan elapsed, TimeSpan delta) => plant.OnSimulationTimeAdvanced(elapsed, delta);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) { plant.SetPsuOutput(false); plant.SetPsuVoltage(0.0); return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult> SetVoltageAsync(ElectricPotential voltage, int channel = 1, CancellationToken cancellationToken = default) { plant.SetPsuVoltage(voltage.Volts); return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(plant.GetPsuVoltage())));
    /// <inheritdoc/>
    public Task<BenchResult> SetCurrentLimitAsync(ElectricCurrent currentLimit, int channel = 1, CancellationToken cancellationToken = default) { plant.SetPsuCurrentLimit(currentLimit.Amperes); return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> GetCurrentLimitAsync(int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(plant.GetPsuCurrentLimit())));
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(plant.SampleElectrical().BusVolts)));
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(plant.SampleElectrical().TotalAmps)));
    /// <inheritdoc/>
    public Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default) { plant.SetPsuOutput(enabled); return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult<bool>> GetOutputEnabledAsync(int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult<bool>.Ok(plant.GetPsuOutput()));
    /// <inheritdoc/>
    public Task<BenchResult<bool>> IsComplianceTrippedAsync(int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult<bool>.Ok(plant.SampleElectrical().ComplianceTripped));
}

/// <summary>Closed-loop coupled thermal chamber backed by a shared <see cref="DigitalTwinPlant"/>.</summary>
/// <param name="role">Logical bench role identifier.</param>
/// <param name="plant">Shared digital-twin physics plant.</param>
/// <param name="calibration">Optional calibration metadata.</param>
public sealed class CoupledTwinThermalController(
    string role,
    DigitalTwinPlant plant,
    InstrumentCalibrationInfo? calibration = null) : IBenchThermalController, ISimulationTimeAware
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("BenchForge-Twin", "CoupledTwinThermal", $"TWIN-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "THERMAL", Identity, Backend, ["SetSetpoint", "MeasureTemperature", "SafeState", "DigitalTwin"], Calibration);
    /// <inheritdoc/>
    public void OnSimulationTimeAdvanced(TimeSpan elapsed, TimeSpan delta) => plant.OnSimulationTimeAdvanced(elapsed, delta);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) { plant.SetThermalSetpoint(25.0); return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult> SetSetpointAsync(Temperature target, CancellationToken cancellationToken = default) { plant.SetThermalSetpoint(target.DegreesCelsius); return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult<Temperature>> MeasureTemperatureAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult<Temperature>.Ok(Temperature.FromDegreesCelsius(plant.CurrentTemperatureC)));
}

/// <summary>Closed-loop coupled electronic load adding sink current directly onto a shared <see cref="DigitalTwinPlant"/> bus.</summary>
/// <param name="role">Logical bench role identifier.</param>
/// <param name="plant">Shared digital-twin physics plant.</param>
/// <param name="calibration">Optional calibration metadata.</param>
public sealed class CoupledTwinElectronicLoad(
    string role,
    DigitalTwinPlant plant,
    InstrumentCalibrationInfo? calibration = null) : IBenchElectronicLoad
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("BenchForge-Twin", "CoupledTwinELoad", $"TWIN-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "ELOAD", Identity, Backend, ["SetCurrentSink", "SetResistanceLoad", "MeasureVoltage", "MeasureCurrent", "MeasurePower", "SetInputEnabled", "SafeState", "DigitalTwin"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) { plant.SetEloadInput(false); plant.SetEloadCurrent(0.0); return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult> SetCurrentSinkAsync(ElectricCurrent current, int channel = 1, CancellationToken cancellationToken = default) { plant.SetEloadCurrent(current.Amperes); return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult> SetResistanceLoadAsync(ElectricResistance resistance, int channel = 1, CancellationToken cancellationToken = default) { plant.SetEloadResistance(resistance.Ohms); return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(plant.SampleElectrical().BusVolts)));
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(plant.SampleElectrical().EloadAmps)));
    /// <inheritdoc/>
    public Task<BenchResult<Power>> MeasurePowerAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        var e = plant.SampleElectrical();
        return Task.FromResult(BenchResult<Power>.Ok(Power.FromWatts((decimal)(e.BusVolts * e.EloadAmps))));
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetInputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default) { plant.SetEloadInput(enabled); return Task.FromResult(BenchResult.Ok()); }
}

/// <summary>
/// Closed-loop coupled DMM measuring live <see cref="DigitalTwinPlant"/> bus state, optionally gated through
/// a required relay path on an <see cref="IBenchSwitchMatrix"/> (reading 0 V / open-circuit when relay is open!).
/// </summary>
/// <param name="role">Logical bench role identifier.</param>
/// <param name="plant">Shared digital-twin physics plant.</param>
/// <param name="switchMatrix">Optional switch matrix gating physical connection to the UUT bus.</param>
/// <param name="requiredRoute">Optional closed relay route required for continuity (for example, <c>BUS_A->UUT_V</c>).</param>
/// <param name="calibration">Optional calibration metadata.</param>
public sealed class CoupledTwinDmm(
    string role,
    DigitalTwinPlant plant,
    IBenchSwitchMatrix? switchMatrix = null,
    string? requiredRoute = null,
    InstrumentCalibrationInfo? calibration = null) : IBenchDigitalMultimeter
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("BenchForge-Twin", "CoupledTwinDmm", $"TWIN-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "DMM", Identity, Backend, ["MeasureDcVoltage", "MeasureDcCurrent", "MeasureResistance", "SafeState", "DigitalTwin"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());

    /// <inheritdoc/>
    public async Task<BenchResult<ElectricPotential>> MeasureDcVoltageAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsConnectedAsync(cancellationToken).ConfigureAwait(false))
            return BenchResult<ElectricPotential>.Ok(ElectricPotential.Zero);
        return BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(plant.SampleElectrical().BusVolts));
    }

    /// <inheritdoc/>
    public async Task<BenchResult<ElectricCurrent>> MeasureDcCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsConnectedAsync(cancellationToken).ConfigureAwait(false))
            return BenchResult<ElectricCurrent>.Ok(ElectricCurrent.Zero);
        return BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(plant.SampleElectrical().TotalAmps));
    }

    /// <inheritdoc/>
    public async Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsConnectedAsync(cancellationToken).ConfigureAwait(false))
            return BenchResult<ElectricResistance>.Ok(ElectricResistance.FromOhms(1e9));
        return BenchResult<ElectricResistance>.Ok(ElectricResistance.FromOhms(plant.EffectiveUutResistanceOhms()));
    }

    private async Task<bool> IsConnectedAsync(CancellationToken ct)
    {
        if (switchMatrix is null || string.IsNullOrWhiteSpace(requiredRoute)) return true;
        var routes = await switchMatrix.GetClosedRoutesAsync(ct).ConfigureAwait(false);
        return routes.IsSuccess && routes.Value.Any(r => string.Equals(r, requiredRoute, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Closed-loop coupled oscilloscope capturing live bus voltage + switching ripple from <see cref="DigitalTwinPlant"/>.</summary>
/// <param name="role">Logical bench role identifier.</param>
/// <param name="plant">Shared digital-twin physics plant.</param>
/// <param name="sampleCount">Samples captured per waveform acquisition.</param>
/// <param name="calibration">Optional calibration metadata.</param>
public sealed class CoupledTwinOscilloscope(
    string role,
    DigitalTwinPlant plant,
    int sampleCount = 512,
    InstrumentCalibrationInfo? calibration = null) : IBenchOscilloscope
{
    /// <inheritdoc/>
    public string Role { get; } = role;
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; } = new("BenchForge-Twin", "CoupledTwinScope", $"TWIN-{role}", "1.0");
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; } = calibration;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SCOPE", Identity, Backend, ["SetVerticalScale", "SetTriggerLevel", "AcquireWaveformVolts", "SafeState", "DigitalTwin"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetVerticalScaleAsync(ElectricPotential voltsPerDivision, int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetTriggerLevelAsync(ElectricPotential level, int channel = 1, CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult<double[]>> AcquireWaveformVoltsAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        Task.FromResult(BenchResult<double[]>.Ok(plant.SampleBusWaveform(sampleCount)));
}
