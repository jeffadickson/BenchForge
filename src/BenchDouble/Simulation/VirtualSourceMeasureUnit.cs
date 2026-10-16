using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>Simulation-only source selection.</summary>
public enum VirtualSourceMode
{
    /// <summary>Voltage source with current compliance.</summary>
    Voltage,
    /// <summary>Current source with voltage compliance.</summary>
    Current
}

/// <summary>One coherent typed source measurement.</summary>
/// <param name="Voltage">Terminal voltage.</param>
/// <param name="Current">Resistive load current.</param>
/// <param name="ComplianceActive">True when output is clipped by compliance.</param>
/// <param name="Mode">Active source mode.</param>
/// <param name="Revision">Configuration revision.</param>
/// <param name="Elapsed">Virtual acquisition time.</param>
public sealed record VirtualSmuMeasurement(ElectricPotential Voltage, ElectricCurrent Current,
    bool ComplianceActive, VirtualSourceMode Mode, long Revision, TimeSpan Elapsed);

/// <summary>Immutable virtual source configuration.</summary>
/// <param name="Mode">Selected source mode.</param>
/// <param name="Enabled">Output enable state.</param>
/// <param name="VoltageSetpoint">Voltage target.</param>
/// <param name="CurrentSetpoint">Current target.</param>
/// <param name="CurrentLimit">Voltage-mode current compliance.</param>
/// <param name="VoltageLimit">Current-mode voltage compliance.</param>
/// <param name="Load">Resistive load.</param>
/// <param name="Revision">Monotonic configuration revision.</param>
public sealed record VirtualSmuConfiguration(VirtualSourceMode Mode, bool Enabled,
    ElectricPotential VoltageSetpoint, ElectricCurrent CurrentSetpoint,
    ElectricCurrent CurrentLimit, ElectricPotential VoltageLimit, ElectricResistance Load, long Revision);

/// <summary>Run-scoped source-measure simulation with exclusive source ownership.</summary>
/// <remarks>Ideal resistive physics only; no hardware safety or calibration.</remarks>
public sealed class VirtualSourceMeasureUnit
{
    private readonly object _gate = new();
    private readonly VirtualSignalBus _bus;
    private readonly ISimulationClock _clock;
    private readonly ILogger<VirtualSourceMeasureUnit> _logger;
    private VirtualSourceMode _mode;
    private ElectricPotential _voltage = ElectricPotential.FromVolts(0);
    private ElectricCurrent _current = ElectricCurrent.FromAmperes(0);
    private ElectricCurrent _currentLimit = ElectricCurrent.FromAmperes(0.01);
    private ElectricPotential _voltageLimit = ElectricPotential.FromVolts(20);
    private ElectricResistance _load = ElectricResistance.FromOhms(1000);
    private bool _enabled;
    private long _revision;
    private readonly Queue<Exception> _faults = new();

    /// <summary>Inject the shared virtual source, clock and logger.</summary>
    /// <param name="bus">Run-scoped virtual signal bus.</param>
    /// <param name="clock">Run-scoped simulation clock.</param>
    /// <param name="logger">Optional structured logger.</param>
    public VirtualSourceMeasureUnit(VirtualSignalBus bus, ISimulationClock clock,
        ILogger<VirtualSourceMeasureUnit>? logger = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<VirtualSourceMeasureUnit>.Instance;
        ApplySource();
    }

    /// <summary>Configure signed voltage with a positive current limit.</summary>
    /// <param name="setpoint">Finite voltage target.</param>
    /// <param name="currentLimit">Positive current compliance.</param>
    public void SourceVoltage(ElectricPotential setpoint, ElectricCurrent currentLimit)
    {
        if (!double.IsFinite(setpoint.Volts) || !Positive(currentLimit.Amperes))
            throw new ArgumentOutOfRangeException(nameof(setpoint));
        lock (_gate)
        {
            _mode = VirtualSourceMode.Voltage;
            _voltage = setpoint;
            _currentLimit = currentLimit;
            ApplySource();
            ++_revision;
            _logger.LogInformation("Virtual SMU voltage {VoltageV} current limit {CurrentA} revision {Revision}",
                setpoint.Volts, currentLimit.Amperes, _revision);
        }
    }

    /// <summary>Configure signed current with positive voltage compliance.</summary>
    /// <param name="setpoint">Finite current target.</param>
    /// <param name="voltageLimit">Positive voltage limit.</param>
    public void SourceCurrent(ElectricCurrent setpoint, ElectricPotential voltageLimit)
    {
        if (!double.IsFinite(setpoint.Amperes) || !Positive(voltageLimit.Volts))
            throw new ArgumentOutOfRangeException(nameof(setpoint));
        lock (_gate)
        {
            ValidateDerived(setpoint, voltageLimit, _load);
            _mode = VirtualSourceMode.Current;
            _current = setpoint;
            _voltageLimit = voltageLimit;
            ApplySource();
            ++_revision;
        }
    }

    /// <summary>Set a positive resistive load and recompute compliance.</summary>
    /// <param name="load">Positive finite resistance.</param>
    public void SetLoad(ElectricResistance load)
    {
        if (!Positive(load.Ohms)) throw new ArgumentOutOfRangeException(nameof(load));
        lock (_gate)
        {
            if (_mode == VirtualSourceMode.Current) ValidateDerived(_current, _voltageLimit, load);
            _load = load;
            ApplySource();
            ++_revision;
        }
    }

    /// <summary>Control the shared simulated output.</summary>
    /// <param name="enabled">Requested output state.</param>
    public void SetOutput(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            _bus.SetOutput(enabled);
            ++_revision;
        }
    }

    /// <summary>Queue a one-shot fault for the next valid acquisition.</summary>
    /// <param name="failure">Exception to raise once.</param>
    public void QueueMeasurementFault(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        lock (_gate) _faults.Enqueue(failure);
    }

    /// <summary>Read terminal voltage and current from one source state.</summary>
    /// <returns>Typed measurement with virtual time and revision.</returns>
    public VirtualSmuMeasurement Measure()
    {
        lock (_gate)
        {
            ThrowFault();
            return MeasureState(_bus.Snapshot(), _clock.Elapsed, _revision);
        }
    }

    /// <summary>Acquire source, meters and waveform from one immutable snapshot.</summary>
    /// <param name="channel">One-based scope channel.</param>
    /// <param name="sampleCount">Number of waveform samples.</param>
    /// <param name="interval">Positive sample interval.</param>
    /// <param name="voltsPerCode">Positive ADC scale.</param>
    /// <param name="acquisitionId">Optional shared correlation ID.</param>
    /// <returns>Coherent source and waveform result.</returns>
    public VirtualCoherentAcquisition AcquireCoherently(int channel, int sampleCount,
        Duration interval, double voltsPerCode = 0.001, Guid? acquisitionId = null)
    {
        VirtualBench.ValidateCapture(channel, sampleCount, interval, voltsPerCode);
        if (acquisitionId == Guid.Empty) throw new ArgumentException("Empty acquisition ID.", nameof(acquisitionId));
        VirtualSignalState state;
        VirtualSmuMeasurement reading;
        lock (_gate)
        {
            ThrowFault();
            state = _bus.Snapshot();
            reading = MeasureState(state, _clock.Elapsed, _revision);
        }
        var id = acquisitionId ?? Guid.NewGuid();
        var waveform = VirtualBench.CaptureSnapshot(state, channel, sampleCount,
            interval, voltsPerCode, id, Duration.FromSeconds(reading.Elapsed.TotalSeconds));
        _logger.LogDebug("Virtual acquisition {AcquisitionId} revision {Revision}", id, reading.Revision);
        return new VirtualCoherentAcquisition(id, reading, state, waveform);
    }

    /// <summary>Copy source configuration without exposing mutable state.</summary>
    /// <returns>Immutable source settings.</returns>
    public VirtualSmuConfiguration Snapshot()
    {
        lock (_gate) return new(_mode, _enabled, _voltage, _current,
            _currentLimit, _voltageLimit, _load, _revision);
    }

    /// <summary>Restore output-off defaults and discard pending faults.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _mode = VirtualSourceMode.Voltage;
            _voltage = ElectricPotential.FromVolts(0);
            _current = ElectricCurrent.FromAmperes(0);
            _currentLimit = ElectricCurrent.FromAmperes(0.01);
            _voltageLimit = ElectricPotential.FromVolts(20);
            _load = ElectricResistance.FromOhms(1000);
            _enabled = false;
            _faults.Clear();
            ApplySource();
            ++_revision;
            _logger.LogInformation("Virtual SMU reset revision {Revision}", _revision);
        }
    }

    private void ThrowFault()
    {
        if (_faults.Count == 0) return;
        var failure = _faults.Dequeue();
        _logger.LogWarning(failure, "Virtual SMU injected fault revision {Revision}", _revision);
        throw failure;
    }

    private VirtualSmuMeasurement MeasureState(VirtualSignalState state, TimeSpan elapsed, long revision)
    {
        var at = Duration.FromSeconds(elapsed.TotalSeconds);
        var volts = state.VoltageAt(at);
        var amps = state.CurrentAt(at);
        double requested = _mode == VirtualSourceMode.Voltage ? _voltage.Volts : _current.Amperes * _load.Ohms;
        bool clipped = _enabled && Math.Abs(requested - volts.Volts) > 1e-10 * Math.Max(1, Math.Abs(requested));
        return new(volts, amps, clipped, _mode, revision, elapsed);
    }

    private void ApplySource()
    {
        double target = _mode == VirtualSourceMode.Voltage ? _voltage.Volts : _current.Amperes * _load.Ohms;
        double limit = _mode == VirtualSourceMode.Voltage ? _currentLimit.Amperes : _voltageLimit.Volts / _load.Ohms;
        if (!double.IsFinite(target) || !Positive(limit))
            throw new InvalidOperationException("Derived source values are not finite.");
        _bus.Configure(Frequency.FromHertz(1), ElectricPotential.FromVolts(0),
            ElectricPotential.FromVolts(target), _load, ElectricCurrent.FromAmperes(limit));
        _bus.SetOutput(_enabled);
    }

    private static void ValidateDerived(ElectricCurrent current, ElectricPotential limit, ElectricResistance load)
    {
        if (!double.IsFinite(current.Amperes * load.Ohms) || !Positive(limit.Volts / load.Ohms))
            throw new ArgumentOutOfRangeException(nameof(load));
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}

/// <summary>Programmable power-supply facade over the shared source controller.</summary>
/// <param name="source">Run-scoped source-measure controller.</param>
public sealed class VirtualProgrammablePowerSupply(VirtualSourceMeasureUnit source)
{
    private readonly VirtualSourceMeasureUnit _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Configure signed voltage and positive current compliance.</summary>
    /// <param name="voltage">Voltage target.</param>
    /// <param name="currentLimit">Current limit.</param>
    public void Configure(ElectricPotential voltage, ElectricCurrent currentLimit) =>
        _source.SourceVoltage(voltage, currentLimit);

    /// <summary>Enable or disable the common output.</summary>
    /// <param name="enabled">Output state.</param>
    public void SetOutput(bool enabled) => _source.SetOutput(enabled);

    /// <summary>Read shared terminal voltage and current.</summary>
    /// <returns>Coherent terminal measurement.</returns>
    public VirtualSmuMeasurement Measure() => _source.Measure();
}
