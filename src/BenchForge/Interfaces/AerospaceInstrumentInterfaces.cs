using BenchForge.Contracts;
using BenchForge.Outcomes;
using BenchForge.Simulation;
using UnitsNet;

namespace BenchForge.Interfaces;

/// <summary>
/// Optional interface implemented by time-driven simulated instruments and digital-twin models
/// that react deterministically when <see cref="AdjustableSimulationClock"/> advances.
/// </summary>
public interface ISimulationTimeAware
{
    /// <summary>Advance internal simulated physics or timers to <paramref name="elapsed"/> by step <paramref name="delta"/>.</summary>
    /// <param name="elapsed">Current monotonic logical run time.</param>
    /// <param name="delta">Non-negative elapsed increment since the previous step.</param>
    void OnSimulationTimeAdvanced(TimeSpan elapsed, TimeSpan delta);
}

/// <summary>Single frequency point of a Vector Network Analyzer S-parameter trace.</summary>
/// <param name="FrequencyHz">Stimulus frequency in hertz.</param>
/// <param name="MagnitudeDb">Measured scattering parameter magnitude in decibels (dB).</param>
/// <param name="PhaseDegrees">Measured scattering parameter phase in degrees.</param>
public sealed record SParameterPoint(double FrequencyHz, double MagnitudeDb, double PhaseDegrees);

/// <summary>Canonical typed async Vector Network Analyzer (VNA) interface for RF S-parameter sweeps.</summary>
public interface IBenchVectorNetworkAnalyzer : IBenchInstrument
{
    /// <summary>Configure sweep start and stop frequencies.</summary>
    /// <param name="start">Sweep start frequency.</param>
    /// <param name="stop">Sweep stop frequency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance.</returns>
    Task<BenchResult> SetFrequencyRangeAsync(Frequency start, Frequency stop, CancellationToken cancellationToken = default);

    /// <summary>Configure VNA stimulus source power.</summary>
    /// <param name="power">RF stimulus power.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance.</returns>
    Task<BenchResult> SetSourcePowerAsync(Power power, CancellationToken cancellationToken = default);

    /// <summary>Measure an S-parameter sweep trace (e.g., <c>S11</c>, <c>S21</c>, <c>S12</c>, <c>S22</c>).</summary>
    /// <param name="parameter">Scattering parameter name.</param>
    /// <param name="points">Number of sweep points (2 to 4096).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing the ordered frequency sweep trace.</returns>
    Task<BenchResult<IReadOnlyList<SParameterPoint>>> MeasureSParameterSweepAsync(string parameter = "S21", int points = 101, CancellationToken cancellationToken = default);
}

/// <summary>Single channel reading acquired from a multiplexed Data Acquisition (DAQ) unit.</summary>
/// <param name="Channel">One-based DAQ channel index.</param>
/// <param name="Value">Measured numeric value in <paramref name="Unit"/>.</param>
/// <param name="Unit">Physical unit symbol (for example, <c>V</c> or <c>degC</c>).</param>
/// <param name="SimulationElapsed">Logical simulation time (or zero on unclocked reads).</param>
public sealed record DaqChannelSample(int Channel, double Value, string Unit, TimeSpan SimulationElapsed);

/// <summary>Canonical typed async multiplexed Data Acquisition (DAQ) / data logger interface.</summary>
public interface IBenchDataAcquisition : IBenchInstrument
{
    /// <summary>Perform an ordered multiplexed scan across the specified channels.</summary>
    /// <param name="channels">One-based channel list to scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing one <see cref="DaqChannelSample"/> per requested channel.</returns>
    Task<BenchResult<IReadOnlyList<DaqChannelSample>>> ScanChannelsAsync(IReadOnlyList<int> channels, CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async Digital I/O interface for discrete EGSE command and telemetry lines.</summary>
public interface IBenchDigitalIo : IBenchInstrument
{
    /// <summary>Drive a discrete digital output pin high or low.</summary>
    /// <param name="pin">Discrete pin name or index identifier (for example, <c>ARM_CMD</c> or <c>DO_01</c>).</param>
    /// <param name="high">True for logic high; false for logic low.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming pin state update.</returns>
    Task<BenchResult> WritePinAsync(string pin, bool high, CancellationToken cancellationToken = default);

    /// <summary>Read the current logic state of a discrete digital pin.</summary>
    /// <param name="pin">Discrete pin name or index identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing true for logic high or false for logic low.</returns>
    Task<BenchResult<bool>> ReadPinAsync(string pin, CancellationToken cancellationToken = default);

    /// <summary>Read a snapshot of all declared discrete pin states.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing pin-name to logic-state map.</returns>
    Task<BenchResult<IReadOnlyDictionary<string, bool>>> ReadAllPinsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Coherent status snapshot of a hardware/simulated safety interlock and watchdog loop.</summary>
/// <param name="IsArmed">True when the safety loop is closed and energized actuation is permitted.</param>
/// <param name="IsTripped">True when an E-Stop, watchdog timeout, or external fault has tripped the loop.</param>
/// <param name="TripReason">Optional explanation of the active fault trip.</param>
/// <param name="TimeSinceLastWatchdogKick">Elapsed simulation time since the last watchdog heartbeat kick.</param>
/// <param name="WatchdogTimeout">Configured maximum allowed interval between watchdog kicks.</param>
public sealed record SafetyInterlockStatus(
    bool IsArmed,
    bool IsTripped,
    string? TripReason,
    TimeSpan TimeSinceLastWatchdogKick,
    TimeSpan WatchdogTimeout);

/// <summary>
/// Canonical typed async safety interlock / hardware watchdog interface modeling independent E-Stop loops
/// and dead-man watchdog heartbeats (<c>BD-RES-002</c>).
/// </summary>
public interface IBenchSafetyInterlock : IBenchInstrument
{
    /// <summary>Query the live status of the interlock loop and watchdog timer.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing <see cref="SafetyInterlockStatus"/>.</returns>
    Task<BenchResult<SafetyInterlockStatus>> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Refresh the watchdog heartbeat timer to prove host liveness.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming the watchdog was kicked (fails if already tripped).</returns>
    Task<BenchResult> KickWatchdogAsync(CancellationToken cancellationToken = default);

    /// <summary>Trip the safety interlock immediately into latched fault state.</summary>
    /// <param name="reason">Audit reason for tripping the interlock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming the trip.</returns>
    Task<BenchResult> TripAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>Explicitly clear a latched fault and re-arm the safety interlock after operator inspection.</summary>
    /// <param name="operatorNote">Audit note confirming safe physical verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming the interlock is re-armed.</returns>
    Task<BenchResult> ResetAndArmAsync(string operatorNote, CancellationToken cancellationToken = default);
}

/// <summary>Stateful mock Vector Network Analyzer modeling realistic S11 return loss and S21 passband response.</summary>
public sealed class StatefulMockVectorNetworkAnalyzer : IBenchVectorNetworkAnalyzer, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _startHz = 1e9;
    private double _stopHz = 3e9;
    private decimal _powerW = 0.001m;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock VNA.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockVectorNetworkAnalyzer(string role, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "StatefulMockVna", $"MOCK-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "VNA", Identity, Backend, ["SetFrequencyRange", "SetSourcePower", "MeasureSParameterSweep", "SafeState"], Calibration);
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) _powerW = 0.0001m;
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyRangeAsync(Frequency start, Frequency stop, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } fault) return Task.FromResult(BenchResult.Fail(fault));
            if (start.Hertz <= 0 || stop.Hertz <= start.Hertz)
                return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-VNA-RANGE", "Start frequency must be positive and strictly less than stop frequency.", Role)));
            _startHz = start.Hertz;
            _stopHz = stop.Hertz;
            return Task.FromResult(BenchResult.Ok());
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetSourcePowerAsync(Power power, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } fault) return Task.FromResult(BenchResult.Fail(fault));
            if (power.Watts < 0m)
                return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-VNA-POWER", "VNA source power must be non-negative.", Role)));
            _powerW = power.Watts;
            return Task.FromResult(BenchResult.Ok());
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<IReadOnlyList<SParameterPoint>>> MeasureSParameterSweepAsync(
        string parameter = "S21",
        int points = 101,
        CancellationToken cancellationToken = default)
    {
        if (points < 2 || points > 4096)
            return Task.FromResult(BenchResult<IReadOnlyList<SParameterPoint>>.Fail(
                BenchError.Validation("BF-VNA-POINTS", "Sweep points must lie in [2, 4096].", Role)));

        double start, stop;
        using (_gate.EnterScope())
        {
            if (ConsumeFault() is { } fault) return Task.FromResult(BenchResult<IReadOnlyList<SParameterPoint>>.Fail(fault));
            start = _startHz;
            stop = _stopHz;
        }

        string p = parameter.Trim().ToUpperInvariant();
        double center = 0.5 * (start + stop);
        double halfSpan = Math.Max(1.0, 0.5 * (stop - start));
        var trace = new SParameterPoint[points];
        for (int i = 0; i < points; i++)
        {
            double frac = (double)i / (points - 1);
            double f = start + frac * (stop - start);
            double detune = (f - center) / halfSpan;
            double magDb = p is "S11" or "S22"
                ? -22.0 + 14.0 * detune * detune // well-matched near center
                : -1.2 - 18.0 * detune * detune; // bandpass insertion loss
            double phaseDeg = -180.0 * frac;
            trace[i] = new SParameterPoint(f, magDb, phaseDeg);
        }
        return Task.FromResult(BenchResult<IReadOnlyList<SParameterPoint>>.Ok(trace));
    }

    private BenchError? ConsumeFault() { var f = _nextFault; _nextFault = null; return f; }
}

/// <summary>Stateful time-stamped mock multiplexed DAQ / data logger.</summary>
public sealed class StatefulMockDataAcquisition : IBenchDataAcquisition, IBenchMockControl, ISimulationTimeAware
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, (double Value, string Unit)> _channels = new();
    private TimeSpan _elapsed;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock DAQ pre-populated with default thermocouple/voltage channels.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="channelCount">Initial number of simulated channels.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockDataAcquisition(string role, int channelCount = 8, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "StatefulMockDaq", $"MOCK-{role}", "1.0");
        for (int ch = 1; ch <= Math.Max(1, channelCount); ch++)
            _channels[ch] = (25.0 + 0.1 * ch, "degC");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "DAQ", Identity, Backend, ["ScanChannels", "SafeState"], Calibration);
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public void OnSimulationTimeAdvanced(TimeSpan elapsed, TimeSpan delta) { using (_gate.EnterScope()) _elapsed = elapsed; }
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());

    /// <summary>Configure a simulated reading for a specific DAQ channel.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="value">Simulated numeric reading.</param>
    /// <param name="unit">Unit symbol (e.g., <c>V</c>, <c>degC</c>).</param>
    public void SetChannelValue(int channel, double value, string unit = "V")
    {
        using (_gate.EnterScope()) _channels[channel] = (value, unit);
    }

    /// <inheritdoc/>
    public Task<BenchResult<IReadOnlyList<DaqChannelSample>>> ScanChannelsAsync(
        IReadOnlyList<int> channels,
        CancellationToken cancellationToken = default)
    {
        if (channels is null || channels.Count == 0)
            return Task.FromResult(BenchResult<IReadOnlyList<DaqChannelSample>>.Fail(
                BenchError.Validation("BF-DAQ-EMPTY", "DAQ scan list must contain at least one channel.", Role)));

        using (_gate.EnterScope())
        {
            if (_nextFault is { } fault) { _nextFault = null; return Task.FromResult(BenchResult<IReadOnlyList<DaqChannelSample>>.Fail(fault)); }
            var results = new List<DaqChannelSample>(channels.Count);
            foreach (int ch in channels)
            {
                if (!_channels.TryGetValue(ch, out var reading))
                    return Task.FromResult(BenchResult<IReadOnlyList<DaqChannelSample>>.Fail(
                        BenchError.Validation("BF-DAQ-CH", $"DAQ channel {ch} is not configured on '{Role}'.", Role)));
                results.Add(new DaqChannelSample(ch, reading.Value, reading.Unit, _elapsed));
            }
            return Task.FromResult(BenchResult<IReadOnlyList<DaqChannelSample>>.Ok(results));
        }
    }
}

/// <summary>Stateful mock discrete Digital I/O controller for EGSE command/status pins.</summary>
public sealed class StatefulMockDigitalIo : IBenchDigitalIo, IBenchMockControl
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, bool> _pins = new(StringComparer.OrdinalIgnoreCase);
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock Digital I/O controller.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockDigitalIo(string role, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "StatefulMockDigitalIo", $"MOCK-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "DIO", Identity, Backend, ["WritePin", "ReadPin", "ReadAllPins", "SafeState"], Calibration);
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            foreach (var key in _pins.Keys.ToArray())
                _pins[key] = false;
        }
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult> WritePinAsync(string pin, bool high, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pin))
            return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-DIO-PIN", "Pin identifier must be non-empty.", Role)));
        using (_gate.EnterScope())
        {
            if (_nextFault is { } fault) { _nextFault = null; return Task.FromResult(BenchResult.Fail(fault)); }
            _pins[pin] = high;
            return Task.FromResult(BenchResult.Ok());
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<bool>> ReadPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pin))
            return Task.FromResult(BenchResult<bool>.Fail(BenchError.Validation("BF-DIO-PIN", "Pin identifier must be non-empty.", Role)));
        using (_gate.EnterScope())
        {
            if (_nextFault is { } fault) { _nextFault = null; return Task.FromResult(BenchResult<bool>.Fail(fault)); }
            return Task.FromResult(BenchResult<bool>.Ok(_pins.TryGetValue(pin, out bool val) && val));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<IReadOnlyDictionary<string, bool>>> ReadAllPinsAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
            return Task.FromResult(BenchResult<IReadOnlyDictionary<string, bool>>.Ok(
                new Dictionary<string, bool>(_pins, StringComparer.OrdinalIgnoreCase)));
    }
}

/// <summary>
/// Time-aware stateful mock safety interlock and dead-man watchdog loop driven by <see cref="ISimulationClock"/>.
/// Automatically trips into a latched safe-fault state whenever simulation time exceeds <c>WatchdogTimeout</c>
/// without a heartbeat kick.
/// </summary>
public sealed class StatefulMockSafetyInterlock : IBenchSafetyInterlock, IBenchMockControl, ISimulationTimeAware
{
    private readonly Lock _gate = new();
    private readonly TimeSpan _watchdogTimeout;
    private TimeSpan _elapsed;
    private TimeSpan _lastKick;
    private bool _armed = true;
    private bool _tripped;
    private string? _tripReason;
    private BenchError? _nextFault;

    /// <summary>Create a time-aware mock safety interlock with configurable watchdog deadline.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="watchdogTimeout">Maximum logical time allowed between watchdog kicks before auto-trip.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockSafetyInterlock(string role, TimeSpan? watchdogTimeout = null, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _watchdogTimeout = watchdogTimeout ?? TimeSpan.FromSeconds(5);
        if (_watchdogTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(watchdogTimeout), "WatchdogTimeout must be positive.");
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "StatefulMockSafetyInterlock", $"MOCK-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "INTERLOCK", Identity, Backend, ["GetStatus", "KickWatchdog", "Trip", "ResetAndArm", "SafeState"], Calibration);
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }

    /// <inheritdoc/>
    public void OnSimulationTimeAdvanced(TimeSpan elapsed, TimeSpan delta)
    {
        using (_gate.EnterScope())
        {
            _elapsed = elapsed;
            if (_armed && !_tripped && (_elapsed - _lastKick) > _watchdogTimeout)
            {
                _tripped = true;
                _armed = false;
                _tripReason = $"Watchdog expired after {(_elapsed - _lastKick).TotalSeconds:F2}s (limit {_watchdogTimeout.TotalSeconds:F2}s).";
            }
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) _armed = false;
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult<SafetyInterlockStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            if (_nextFault is { } fault) { _nextFault = null; return Task.FromResult(BenchResult<SafetyInterlockStatus>.Fail(fault)); }
            return Task.FromResult(BenchResult<SafetyInterlockStatus>.Ok(
                new SafetyInterlockStatus(_armed, _tripped, _tripReason, _elapsed - _lastKick, _watchdogTimeout)));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult> KickWatchdogAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            if (_nextFault is { } fault) { _nextFault = null; return Task.FromResult(BenchResult.Fail(fault)); }
            if (_tripped)
            {
                return Task.FromResult(BenchResult.Fail(new BenchError(
                    "BF-INTERLOCK-TRIPPED",
                    BenchErrorKind.RecoveryRequired,
                    $"Safety interlock '{Role}' is tripped ({_tripReason}) and cannot accept watchdog kicks until explicitly reset.",
                    Role,
                    RequiresRecovery: true,
                    Remedy: "Verify physical safe state and call ResetAndArmAsync(operatorNote).")));
            }
            _lastKick = _elapsed;
            return Task.FromResult(BenchResult.Ok());
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult> TripAsync(string reason, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            _tripped = true;
            _armed = false;
            _tripReason = string.IsNullOrWhiteSpace(reason) ? "Manual trip." : reason;
        }
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult> ResetAndArmAsync(string operatorNote, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operatorNote))
            return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-INTERLOCK-NOTE", "Operator verification note is required to re-arm safety interlock.", Role)));
        using (_gate.EnterScope())
        {
            _tripped = false;
            _armed = true;
            _tripReason = null;
            _lastKick = _elapsed;
        }
        return Task.FromResult(BenchResult.Ok());
    }
}
