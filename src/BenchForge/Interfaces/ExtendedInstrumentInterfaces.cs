using BenchForge.Contracts;
using BenchForge.Outcomes;
using UnitsNet;

namespace BenchForge.Interfaces;

/// <summary>Canonical typed async function/arbitrary waveform generator interface mirroring IVI Fgen.</summary>
public interface IBenchFunctionGenerator : IBenchInstrument
{
    /// <summary>Program output frequency on the specified channel.</summary>
    /// <param name="frequency">Output frequency.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance or structured fault.</returns>
    Task<BenchResult> SetFrequencyAsync(Frequency frequency, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Query the programmed output frequency.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing output frequency.</returns>
    Task<BenchResult<Frequency>> GetFrequencyAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Program peak-to-peak amplitude on the specified channel.</summary>
    /// <param name="amplitude">Peak-to-peak amplitude.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance or structured fault.</returns>
    Task<BenchResult> SetAmplitudeAsync(ElectricPotential amplitude, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Query the programmed peak-to-peak amplitude.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing peak-to-peak amplitude.</returns>
    Task<BenchResult<ElectricPotential>> GetAmplitudeAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Enable or disable generator output on the specified channel.</summary>
    /// <param name="enabled">True to enable output; false to disable.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming state transition.</returns>
    Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async oscilloscope interface mirroring IVI Scope with SIMD-ready waveform capture.</summary>
public interface IBenchOscilloscope : IBenchInstrument
{
    /// <summary>Program channel vertical scale in volts per division.</summary>
    /// <param name="voltsPerDivision">Vertical scale.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming scale acceptance.</returns>
    Task<BenchResult> SetVerticalScaleAsync(ElectricPotential voltsPerDivision, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Program trigger voltage threshold.</summary>
    /// <param name="level">Trigger voltage level.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming trigger acceptance.</returns>
    Task<BenchResult> SetTriggerLevelAsync(ElectricPotential level, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Acquire decoded voltage samples from the specified channel.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing decoded voltage samples in volts.</returns>
    Task<BenchResult<double[]>> AcquireWaveformVoltsAsync(int channel = 1, CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async switch matrix / relay router interface mirroring IVI Swtch for EGSE harness routing.</summary>
public interface IBenchSwitchMatrix : IBenchInstrument
{
    /// <summary>Close a relay path connecting <paramref name="channel1"/> to <paramref name="channel2"/>.</summary>
    /// <param name="channel1">Source terminal or channel identifier.</param>
    /// <param name="channel2">Destination terminal or channel identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming path closure or conflict denial.</returns>
    Task<BenchResult> ConnectAsync(string channel1, string channel2, CancellationToken cancellationToken = default);

    /// <summary>Open an existing relay path between <paramref name="channel1"/> and <paramref name="channel2"/>.</summary>
    /// <param name="channel1">Source terminal or channel identifier.</param>
    /// <param name="channel2">Destination terminal or channel identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming path opening.</returns>
    Task<BenchResult> DisconnectAsync(string channel1, string channel2, CancellationToken cancellationToken = default);

    /// <summary>Open all closed relays across the switch matrix (safe disconnected state).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming all relays opened.</returns>
    Task<BenchResult> DisconnectAllAsync(CancellationToken cancellationToken = default);

    /// <summary>List all currently closed relay routes.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing ordered closed route pairs (<c>ch1->ch2</c>).</returns>
    Task<BenchResult<IReadOnlyList<string>>> GetClosedRoutesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async programmable DC electronic load interface for EPS/battery/power-bus testing.</summary>
public interface IBenchElectronicLoad : IBenchInstrument
{
    /// <summary>Program constant-current sink setpoint.</summary>
    /// <param name="current">Target sink current.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming setpoint acceptance.</returns>
    Task<BenchResult> SetCurrentSinkAsync(ElectricCurrent current, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Program constant-resistance load setpoint.</summary>
    /// <param name="resistance">Target simulated load resistance.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming setpoint acceptance.</returns>
    Task<BenchResult> SetResistanceLoadAsync(ElectricResistance resistance, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Measure terminal voltage presented to the electronic load.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing measured voltage.</returns>
    Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Measure actual sink current drawn by the electronic load.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing measured current.</returns>
    Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Measure power dissipated by the electronic load.</summary>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing measured power.</returns>
    Task<BenchResult<Power>> MeasurePowerAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Enable or disable the electronic load input.</summary>
    /// <param name="enabled">True to engage load sink; false to disconnect.</param>
    /// <param name="channel">One-based channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming input state change.</returns>
    Task<BenchResult> SetInputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async RF signal generator interface mirroring IVI RFSigGen.</summary>
public interface IBenchRfSignalGenerator : IBenchInstrument
{
    /// <summary>Program RF carrier frequency.</summary>
    /// <param name="frequency">Carrier frequency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming frequency acceptance.</returns>
    Task<BenchResult> SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default);

    /// <summary>Query programmed RF carrier frequency.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing carrier frequency.</returns>
    Task<BenchResult<Frequency>> GetFrequencyAsync(CancellationToken cancellationToken = default);

    /// <summary>Program RF output power in watts.</summary>
    /// <param name="power">Output power.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming power acceptance.</returns>
    Task<BenchResult> SetOutputPowerAsync(Power power, CancellationToken cancellationToken = default);

    /// <summary>Query programmed RF output power.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing output power.</returns>
    Task<BenchResult<Power>> GetOutputPowerAsync(CancellationToken cancellationToken = default);

    /// <summary>Enable or disable RF carrier output.</summary>
    /// <param name="enabled">True to energize RF output; false to mute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming RF state change.</returns>
    Task<BenchResult> SetRfOutputEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async spectrum analyzer interface mirroring IVI SpecAn.</summary>
public interface IBenchSpectrumAnalyzer : IBenchInstrument
{
    /// <summary>Program center frequency.</summary>
    /// <param name="center">Center frequency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance.</returns>
    Task<BenchResult> SetCenterFrequencyAsync(Frequency center, CancellationToken cancellationToken = default);

    /// <summary>Program frequency span.</summary>
    /// <param name="span">Frequency span.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance.</returns>
    Task<BenchResult> SetSpanAsync(Frequency span, CancellationToken cancellationToken = default);

    /// <summary>Program resolution bandwidth (RBW).</summary>
    /// <param name="rbw">Resolution bandwidth.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance.</returns>
    Task<BenchResult> SetResolutionBandwidthAsync(Frequency rbw, CancellationToken cancellationToken = default);

    /// <summary>Query configured center frequency.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing center frequency.</returns>
    Task<BenchResult<Frequency>> GetCenterFrequencyAsync(CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async RF power meter interface mirroring IVI PwrMeter.</summary>
public interface IBenchRfPowerMeter : IBenchInstrument
{
    /// <summary>Configure calibration frequency for the RF power sensor.</summary>
    /// <param name="frequency">Calibration frequency.</param>
    /// <param name="channel">One-based sensor channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance.</returns>
    Task<BenchResult> SetFrequencyAsync(Frequency frequency, int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Measure RF power on the specified sensor channel.</summary>
    /// <param name="channel">One-based sensor channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing measured RF power.</returns>
    Task<BenchResult<Power>> MeasurePowerAsync(int channel = 1, CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async universal frequency counter interface mirroring IVI Counter.</summary>
public interface IBenchFrequencyCounter : IBenchInstrument
{
    /// <summary>Measure signal frequency on the specified input channel.</summary>
    /// <param name="channel">One-based input channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing measured frequency.</returns>
    Task<BenchResult<Frequency>> MeasureFrequencyAsync(int channel = 1, CancellationToken cancellationToken = default);

    /// <summary>Measure signal period on the specified input channel.</summary>
    /// <param name="channel">One-based input channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing measured period.</returns>
    Task<BenchResult<Duration>> MeasurePeriodAsync(int channel = 1, CancellationToken cancellationToken = default);
}

/// <summary>Canonical typed async LCR / impedance meter interface.</summary>
public interface IBenchLcrMeter : IBenchInstrument
{
    /// <summary>Program AC stimulus test frequency.</summary>
    /// <param name="frequency">Stimulus frequency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance.</returns>
    Task<BenchResult> SetTestFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default);

    /// <summary>Program AC stimulus test voltage.</summary>
    /// <param name="voltage">Stimulus RMS voltage.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome confirming acceptance.</returns>
    Task<BenchResult> SetTestVoltageAsync(ElectricPotential voltage, CancellationToken cancellationToken = default);

    /// <summary>Measure capacitance of the device under test.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing measured capacitance.</returns>
    Task<BenchResult<ElectricCapacitance>> MeasureCapacitanceAsync(CancellationToken cancellationToken = default);

    /// <summary>Measure resistance / ESR of the device under test.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing measured resistance.</returns>
    Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default);
}

/// <summary>Stateful mock function generator implementing <see cref="IBenchFunctionGenerator"/> and <see cref="IBenchMockControl"/>.</summary>
public sealed class StatefulMockFunctionGenerator : IBenchFunctionGenerator, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _frequencyHz = 1000.0;
    private double _amplitudeVpp = 1.0;
    private bool _outputEnabled;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock function generator.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockFunctionGenerator(string role, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockFGen-33522B", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "FGEN", Identity, Backend, ["SetFrequency", "GetFrequency", "SetAmplitude", "GetAmplitude", "SetOutputEnabled", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) { using (_gate.EnterScope()) _outputEnabled = false; return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyAsync(Frequency frequency, int channel = 1, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(frequency.Hertz) || frequency.Hertz <= 0) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-FGEN-FREQ", "Frequency must be positive.", Role)));
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _frequencyHz = frequency.Hertz; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetFrequencyAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<Frequency>.Fail(e)); return Task.FromResult(BenchResult<Frequency>.Ok(Frequency.FromHertz(_frequencyHz))); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetAmplitudeAsync(ElectricPotential amplitude, int channel = 1, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(amplitude.Volts) || amplitude.Volts < 0) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-FGEN-AMP", "Amplitude must be non-negative.", Role)));
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _amplitudeVpp = amplitude.Volts; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetAmplitudeAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<ElectricPotential>.Fail(e)); return Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(_amplitudeVpp))); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _outputEnabled = enabled; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <summary>Inspect current simulated output enable state.</summary>
    public bool IsOutputEnabled { get { using (_gate.EnterScope()) return _outputEnabled; } }
    private BenchError? Consume() { var e = _nextFault; _nextFault = null; return e; }
}

/// <summary>Stateful mock oscilloscope synthesizing deterministic waveforms ready for SIMD <see cref="BenchForge.Oscilloscopes.WaveformAnalyzer"/>.</summary>
public sealed class StatefulMockOscilloscope : IBenchOscilloscope, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _dcOffsetVolts;
    private double _rippleAmplitudeVolts;
    private int _sampleCount;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock oscilloscope synthesizing a DC + sinusoidal ripple waveform.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="dcOffsetVolts">Simulated DC rail offset in volts.</param>
    /// <param name="rippleAmplitudeVolts">Simulated AC sinusoidal ripple amplitude in volts.</param>
    /// <param name="sampleCount">Number of samples per waveform acquisition.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockOscilloscope(string role, double dcOffsetVolts = 3.3, double rippleAmplitudeVolts = 0.008, int sampleCount = 1024, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (sampleCount < 8) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        Role = role;
        _dcOffsetVolts = dcOffsetVolts;
        _rippleAmplitudeVolts = rippleAmplitudeVolts;
        _sampleCount = sampleCount;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockScope-MSO54", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <summary>Configure the synthesized DC offset and AC ripple amplitude for subsequent captures.</summary>
    /// <param name="dcOffsetVolts">DC mean voltage in volts.</param>
    /// <param name="rippleAmplitudeVolts">AC sinusoidal amplitude in volts.</param>
    public void ConfigureSignal(double dcOffsetVolts, double rippleAmplitudeVolts) { using (_gate.EnterScope()) { _dcOffsetVolts = dcOffsetVolts; _rippleAmplitudeVolts = rippleAmplitudeVolts; } }
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SCOPE", Identity, Backend, ["SetVerticalScale", "SetTriggerLevel", "AcquireWaveformVolts", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetVerticalScaleAsync(ElectricPotential voltsPerDivision, int channel = 1, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(voltsPerDivision.Volts) || voltsPerDivision.Volts <= 0) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-SCOPE-SCALE", "Vertical scale must be positive.", Role)));
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetTriggerLevelAsync(ElectricPotential level, int channel = 1, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(level.Volts)) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-SCOPE-TRIG", "Trigger level must be finite.", Role)));
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<double[]>> AcquireWaveformVoltsAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            if (Consume() is { } e) return Task.FromResult(BenchResult<double[]>.Fail(e));
            var samples = new double[_sampleCount];
            for (int i = 0; i < _sampleCount; i++)
                samples[i] = _dcOffsetVolts + _rippleAmplitudeVolts * Math.Sin(2.0 * Math.PI * i / 64.0);
            return Task.FromResult(BenchResult<double[]>.Ok(samples));
        }
    }
    private BenchError? Consume() { var e = _nextFault; _nextFault = null; return e; }
}

/// <summary>Stateful mock switch matrix enforcing exclusive terminal routing and safe all-open shutdown.</summary>
public sealed class StatefulMockSwitchMatrix : IBenchSwitchMatrix, IBenchMockControl
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _routes = new(StringComparer.OrdinalIgnoreCase);
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock switch matrix.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockSwitchMatrix(string role, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockSwitch-DAQ970A", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SWITCH", Identity, Backend, ["Connect", "Disconnect", "DisconnectAll", "GetClosedRoutes", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => DisconnectAllAsync(cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> ConnectAsync(string channel1, string channel2, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channel1) || string.IsNullOrWhiteSpace(channel2) || string.Equals(channel1, channel2, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-SW-ROUTE", "Switch connect requires two distinct non-empty terminals.", Role)));
        using (_gate.EnterScope())
        {
            if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e));
            _routes.Add(Canonical(channel1, channel2));
            return Task.FromResult(BenchResult.Ok());
        }
    }
    /// <inheritdoc/>
    public Task<BenchResult> DisconnectAsync(string channel1, string channel2, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e));
            _routes.Remove(Canonical(channel1, channel2));
            return Task.FromResult(BenchResult.Ok());
        }
    }
    /// <inheritdoc/>
    public Task<BenchResult> DisconnectAllAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e));
            _routes.Clear();
            return Task.FromResult(BenchResult.Ok());
        }
    }
    /// <inheritdoc/>
    public Task<BenchResult<IReadOnlyList<string>>> GetClosedRoutesAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            if (Consume() is { } e) return Task.FromResult(BenchResult<IReadOnlyList<string>>.Fail(e));
            return Task.FromResult(BenchResult<IReadOnlyList<string>>.Ok(_routes.OrderBy(r => r, StringComparer.Ordinal).ToArray()));
        }
    }
    private static string Canonical(string a, string b) =>
        string.Compare(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase) <= 0 ? $"{a.Trim()}->{b.Trim()}" : $"{b.Trim()}->{a.Trim()}";
    private BenchError? Consume() { var e = _nextFault; _nextFault = null; return e; }
}

/// <summary>Stateful mock programmable DC electronic load modeling constant-current and constant-resistance dissipation.</summary>
public sealed class StatefulMockElectronicLoad : IBenchElectronicLoad, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _sourceVoltageVolts;
    private double _currentSinkAmperes;
    private double _resistanceOhms = 10.0;
    private bool _isConstantResistance;
    private bool _inputEnabled;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock electronic load attached to a simulated power bus voltage.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="sourceVoltageVolts">Simulated bus voltage applied to load terminals in volts.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockElectronicLoad(string role, double sourceVoltageVolts = 28.0, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _sourceVoltageVolts = sourceVoltageVolts;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockELoad-N3300A", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "ELOAD", Identity, Backend, ["SetCurrentSink", "SetResistanceLoad", "MeasureVoltage", "MeasureCurrent", "MeasurePower", "SetInputEnabled", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) { using (_gate.EnterScope()) { _inputEnabled = false; _currentSinkAmperes = 0.0; } return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult> SetCurrentSinkAsync(ElectricCurrent current, int channel = 1, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(current.Amperes) || current.Amperes < 0) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-ELOAD-CURR", "Sink current must be non-negative.", Role)));
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _currentSinkAmperes = current.Amperes; _isConstantResistance = false; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetResistanceLoadAsync(ElectricResistance resistance, int channel = 1, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(resistance.Ohms) || resistance.Ohms <= 0) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-ELOAD-RES", "Load resistance must be positive.", Role)));
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _resistanceOhms = resistance.Ohms; _isConstantResistance = true; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<ElectricPotential>.Fail(e)); return Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(_sourceVoltageVolts))); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<ElectricCurrent>.Fail(e)); double i = _inputEnabled ? (_isConstantResistance ? _sourceVoltageVolts / _resistanceOhms : _currentSinkAmperes) : 0.0; return Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(i))); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<Power>> MeasurePowerAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<Power>.Fail(e)); double i = _inputEnabled ? (_isConstantResistance ? _sourceVoltageVolts / _resistanceOhms : _currentSinkAmperes) : 0.0; return Task.FromResult(BenchResult<Power>.Ok(Power.FromWatts((decimal)(_sourceVoltageVolts * i)))); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetInputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _inputEnabled = enabled; return Task.FromResult(BenchResult.Ok()); }
    }
    private BenchError? Consume() { var e = _nextFault; _nextFault = null; return e; }
}

/// <summary>Stateful mock RF signal generator implementing <see cref="IBenchRfSignalGenerator"/> and <see cref="IBenchMockControl"/>.</summary>
public sealed class StatefulMockRfSignalGenerator : IBenchRfSignalGenerator, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _frequencyHz = 1e9;
    private decimal _powerWatts = 0.001m;
    private bool _rfEnabled;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock RF signal generator.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockRfSignalGenerator(string role, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockRfGen-N5181B", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "RFSIGGEN", Identity, Backend, ["SetFrequency", "GetFrequency", "SetOutputPower", "GetOutputPower", "SetRfOutputEnabled", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) { using (_gate.EnterScope()) _rfEnabled = false; return Task.FromResult(BenchResult.Ok()); }
    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(frequency.Hertz) || frequency.Hertz <= 0) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-RF-FREQ", "Frequency must be positive.", Role)));
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _frequencyHz = frequency.Hertz; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetFrequencyAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<Frequency>.Fail(e)); return Task.FromResult(BenchResult<Frequency>.Ok(Frequency.FromHertz(_frequencyHz))); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetOutputPowerAsync(Power power, CancellationToken cancellationToken = default)
    {
        if (power.Watts < 0m) return Task.FromResult(BenchResult.Fail(BenchError.Validation("BF-RF-PWR", "Power in watts must be non-negative.", Role)));
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _powerWatts = power.Watts; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<Power>> GetOutputPowerAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<Power>.Fail(e)); return Task.FromResult(BenchResult<Power>.Ok(Power.FromWatts(_powerWatts))); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetRfOutputEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _rfEnabled = enabled; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <summary>Inspect current simulated RF output enable state.</summary>
    public bool IsRfOutputEnabled { get { using (_gate.EnterScope()) return _rfEnabled; } }
    private BenchError? Consume() { var e = _nextFault; _nextFault = null; return e; }
}

/// <summary>Stateful mock spectrum analyzer implementing <see cref="IBenchSpectrumAnalyzer"/> and <see cref="IBenchMockControl"/>.</summary>
public sealed class StatefulMockSpectrumAnalyzer : IBenchSpectrumAnalyzer, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _centerHz = 1e9;
    private double _spanHz = 10e6;
    private double _rbwHz = 10e3;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock spectrum analyzer.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockSpectrumAnalyzer(string role, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockSpecAn-N9020B", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SPECAN", Identity, Backend, ["SetCenterFrequency", "SetSpan", "SetResolutionBandwidth", "GetCenterFrequency", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetCenterFrequencyAsync(Frequency center, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _centerHz = center.Hertz; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetSpanAsync(Frequency span, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _spanHz = span.Hertz; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetResolutionBandwidthAsync(Frequency rbw, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _rbwHz = rbw.Hertz; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetCenterFrequencyAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<Frequency>.Fail(e)); return Task.FromResult(BenchResult<Frequency>.Ok(Frequency.FromHertz(_centerHz))); }
    }
    /// <summary>Inspect configured frequency span and resolution bandwidth.</summary>
    public (Frequency Span, Frequency Rbw) GetSettings() { using (_gate.EnterScope()) return (Frequency.FromHertz(_spanHz), Frequency.FromHertz(_rbwHz)); }
    private BenchError? Consume() { var e = _nextFault; _nextFault = null; return e; }
}

/// <summary>Stateful mock RF power meter implementing <see cref="IBenchRfPowerMeter"/> and <see cref="IBenchMockControl"/>.</summary>
public sealed class StatefulMockRfPowerMeter : IBenchRfPowerMeter, IBenchMockControl
{
    private readonly Lock _gate = new();
    private decimal _measuredWatts;
    private double _calFrequencyHz = 1e9;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock RF power meter.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="measuredWatts">Initial simulated RF power reading in watts.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockRfPowerMeter(string role, decimal measuredWatts = 0.001m, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _measuredWatts = measuredWatts;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockPwrMeter-N1912A", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "PWRMETER", Identity, Backend, ["SetFrequency", "MeasurePower", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetFrequencyAsync(Frequency frequency, int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _calFrequencyHz = frequency.Hertz; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<Power>> MeasurePowerAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<Power>.Fail(e)); return Task.FromResult(BenchResult<Power>.Ok(Power.FromWatts(_measuredWatts))); }
    }
    /// <summary>Inspect active sensor calibration frequency.</summary>
    public Frequency CalibrationFrequency { get { using (_gate.EnterScope()) return Frequency.FromHertz(_calFrequencyHz); } }
    private BenchError? Consume() { var e = _nextFault; _nextFault = null; return e; }
}

/// <summary>Stateful mock universal frequency counter implementing <see cref="IBenchFrequencyCounter"/> and <see cref="IBenchMockControl"/>.</summary>
public sealed class StatefulMockFrequencyCounter : IBenchFrequencyCounter, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _frequencyHz;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock frequency counter.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="frequencyHz">Initial simulated signal frequency in hertz.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockFrequencyCounter(string role, double frequencyHz = 10_000_000.0, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _frequencyHz = frequencyHz;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockCounter-53230A", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "COUNTER", Identity, Backend, ["MeasureFrequency", "MeasurePeriod", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> MeasureFrequencyAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<Frequency>.Fail(e)); return Task.FromResult(BenchResult<Frequency>.Ok(Frequency.FromHertz(_frequencyHz))); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<Duration>> MeasurePeriodAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<Duration>.Fail(e)); return Task.FromResult(BenchResult<Duration>.Ok(Duration.FromSeconds(1.0 / _frequencyHz))); }
    }
    private BenchError? Consume() { var e = _nextFault; _nextFault = null; return e; }
}

/// <summary>Stateful mock LCR / impedance meter implementing <see cref="IBenchLcrMeter"/> and <see cref="IBenchMockControl"/>.</summary>
public sealed class StatefulMockLcrMeter : IBenchLcrMeter, IBenchMockControl
{
    private readonly Lock _gate = new();
    private double _capacitanceFarads;
    private double _resistanceOhms;
    private double _testFreqHz = 1000.0;
    private double _testVoltageVolts = 1.0;
    private BenchError? _nextFault;

    /// <summary>Create a stateful mock LCR meter.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <param name="capacitanceFarads">Initial simulated capacitance in farads.</param>
    /// <param name="resistanceOhms">Initial simulated resistance/ESR in ohms.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    public StatefulMockLcrMeter(string role, double capacitanceFarads = 1e-6, double resistanceOhms = 0.05, InstrumentCalibrationInfo? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _capacitanceFarads = capacitanceFarads;
        _resistanceOhms = resistanceOhms;
        Calibration = calibration;
        Identity = new InstrumentIdentity("BenchForge", "MockLCR-E4980", $"SIM-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Simulated;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration { get; }
    /// <inheritdoc/>
    public void InjectNextFault(BenchError? fault) { using (_gate.EnterScope()) _nextFault = fault; }
    /// <inheritdoc/>
    public void ClearFaults() { using (_gate.EnterScope()) _nextFault = null; }
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "LCR", Identity, Backend, ["SetTestFrequency", "SetTestVoltage", "MeasureCapacitance", "MeasureResistance", "SafeState"], Calibration);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());
    /// <inheritdoc/>
    public Task<BenchResult> SetTestFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _testFreqHz = frequency.Hertz; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult> SetTestVoltageAsync(ElectricPotential voltage, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult.Fail(e)); _testVoltageVolts = voltage.Volts; return Task.FromResult(BenchResult.Ok()); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCapacitance>> MeasureCapacitanceAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<ElectricCapacitance>.Fail(e)); return Task.FromResult(BenchResult<ElectricCapacitance>.Ok(ElectricCapacitance.FromFarads(_capacitanceFarads))); }
    }
    /// <inheritdoc/>
    public Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) { if (Consume() is { } e) return Task.FromResult(BenchResult<ElectricResistance>.Fail(e)); return Task.FromResult(BenchResult<ElectricResistance>.Ok(ElectricResistance.FromOhms(_resistanceOhms))); }
    }
    /// <summary>Inspect configured LCR stimulus frequency and voltage.</summary>
    public (Frequency Frequency, ElectricPotential Voltage) GetStimulus() { using (_gate.EnterScope()) return (Frequency.FromHertz(_testFreqHz), ElectricPotential.FromVolts(_testVoltageVolts)); }
    private BenchError? Consume() { var e = _nextFault; _nextFault = null; return e; }
}
