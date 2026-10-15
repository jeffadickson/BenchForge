using System.Globalization;
using BenchDouble.Async;
using UnitsNet;

namespace BenchDouble.Instruments.Keithley;

/// <summary>236/237/238 use Keithley's legacy IEEE-488 language, not SCPI.</summary>
public enum SourceMeasureMode { SourceVoltageMeasureCurrent, SourceCurrentMeasureVoltage }
public enum IntegrationSpeed { Fast, Medium, LineCycle60Hz, LineCycle50Hz }

/// <summary>
/// A bus adapter must support the IEEE-488 TALK phase separately from a command WRITE.
/// Implementations handle EOI, bus timeouts, addressing, and command termination.
/// </summary>
public interface IKeithley236Bus
{
    ValueTask WriteAsync(string command, CancellationToken ct = default);
    ValueTask<string> ReadAsync(CancellationToken ct = default);
}

/// <summary>
/// Initial DC source/measure implementation based on the Keithley 236/237/238
/// quick reference, pp. 56–69. No output is enabled by construction.
/// Hardware must be configured by an authorized test operator before enabling.
/// </summary>
public sealed class Keithley236
{
    private readonly IKeithley236Bus _bus;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SourceMeasureMode? _mode;
    private bool _operating;
    private double _compliance;

    public Keithley236(IKeithley236Bus bus) =>
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));

    public SourceMeasureMode? ConfiguredMode => _mode;
    public bool OperateRequested => _operating;

    /// <summary>Configure DC sourcing mode; selecting mode requests standby first.</summary>
    public async ValueTask ConfigureAsync(SourceMeasureMode mode, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Mode changes can affect the output; always request standby first.
            await _bus.WriteAsync("N0X", ct).ConfigureAwait(false);
            _operating = false;
            await _bus.WriteAsync(mode == SourceMeasureMode.SourceVoltageMeasureCurrent ? "F0,0X" : "F1,0X", ct).ConfigureAwait(false);
            _mode = mode;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Set DC bias; auto range (range=0), delay in milliseconds.</summary>
    public async ValueTask SetBiasAsync(ElectricPotential voltage, TimeSpan delay, CancellationToken ct = default)
    {
        if (_mode != SourceMeasureMode.SourceVoltageMeasureCurrent) throw new InvalidOperationException("Configure voltage sourcing first.");
        await SetBiasCoreAsync(voltage.Volts, 110, delay, ct).ConfigureAwait(false);
    }

    /// <summary>Set DC current bias; auto range (range=0), delay in milliseconds.</summary>
    public async ValueTask SetBiasAsync(ElectricCurrent current, TimeSpan delay, CancellationToken ct = default)
    {
        if (_mode != SourceMeasureMode.SourceCurrentMeasureVoltage) throw new InvalidOperationException("Configure current sourcing first.");
        await SetBiasCoreAsync(current.Amperes, 0.1, delay, ct).ConfigureAwait(false);
    }

    private async ValueTask SetBiasCoreAsync(double level, double limit, TimeSpan delay, CancellationToken ct)
    {
        if (!double.IsFinite(level) || Math.Abs(level) > limit) throw new ArgumentOutOfRangeException(nameof(level));
        if (delay < TimeSpan.Zero || delay.TotalMilliseconds > 65000) throw new ArgumentOutOfRangeException(nameof(delay));
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // B(level),(range),(delay), range 0=auto; execute with X.
            await _bus.WriteAsync($"B{level.ToString("G17", CultureInfo.InvariantCulture)},0,{delay.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)}X", ct).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Configure measurement compliance (auto range). Always call before Operate.</summary>
    public async ValueTask SetComplianceAsync(ElectricCurrent current, CancellationToken ct = default)
    {
        if (_mode != SourceMeasureMode.SourceVoltageMeasureCurrent) throw new InvalidOperationException("Current compliance requires voltage source mode.");
        await SetComplianceCoreAsync(current.Amperes, 0.1, ct).ConfigureAwait(false);
    }

    public async ValueTask SetComplianceAsync(ElectricPotential voltage, CancellationToken ct = default)
    {
        if (_mode != SourceMeasureMode.SourceCurrentMeasureVoltage) throw new InvalidOperationException("Voltage compliance requires current source mode.");
        await SetComplianceCoreAsync(voltage.Volts, 110, ct).ConfigureAwait(false);
    }

    private async ValueTask SetComplianceCoreAsync(double level, double max, CancellationToken ct)
    {
        if (!double.IsFinite(level) || level <= 0 || level > max) throw new ArgumentOutOfRangeException(nameof(level));
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _bus.WriteAsync($"L{level.ToString("G17", CultureInfo.InvariantCulture)},0X", ct).ConfigureAwait(false);
            _compliance = level;
        }
        finally { _lock.Release(); }
    }

    public async ValueTask SetIntegrationAsync(IntegrationSpeed speed, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(speed)) throw new ArgumentOutOfRangeException(nameof(speed));
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { await _bus.WriteAsync($"S{(int)speed}X", ct).ConfigureAwait(false); }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Requests OPERATE. This may energize hazardous terminals; use only with
    /// verified interlocks, physical fixture, authorization and safe source limits.
    /// </summary>
    public async ValueTask SetOperateAsync(bool enabled, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (enabled && (_mode is null || _compliance <= 0))
                throw new InvalidOperationException("Configure source mode and compliance before operate.");
            await _bus.WriteAsync(enabled ? "N1X" : "N0X", ct).ConfigureAwait(false);
            _operating = enabled;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Request measurement-only ASCII output, then read in the IEEE-488 TALK phase.
    /// This is NOT a SCPI query. The bus provider must implement the talk handshake.
    /// </summary>
    public async ValueTask<ElectricCurrent> MeasureCurrentAsync(CancellationToken ct = default)
    {
        if (_mode != SourceMeasureMode.SourceVoltageMeasureCurrent) throw new InvalidOperationException("Current measurement requires voltage sourcing.");
        return ElectricCurrent.FromAmperes(await MeasureScalarAsync(ct).ConfigureAwait(false));
    }

    public async ValueTask<ElectricPotential> MeasureVoltageAsync(CancellationToken ct = default)
    {
        if (_mode != SourceMeasureMode.SourceCurrentMeasureVoltage) throw new InvalidOperationException("Voltage measurement requires current sourcing.");
        return ElectricPotential.FromVolts(await MeasureScalarAsync(ct).ConfigureAwait(false));
    }

    private async ValueTask<double> MeasureScalarAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // G(items=4 measurement, format=2 bare ASCII, lines=0 DC)
            await _bus.WriteAsync("G4,2,0X", ct).ConfigureAwait(false);
            string raw = (await _bus.ReadAsync(ct).ConfigureAwait(false)).Trim();
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                throw new FormatException($"Unexpected Keithley 236 measurement response: '{raw}'");
            return value;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Best-effort standby; caller must confirm hardware state separately.</summary>
    public ValueTask StandbyAsync(CancellationToken ct = default) => SetOperateAsync(false, ct);
}
