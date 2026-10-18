using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>
/// An instrument mock with explicit mode selection. The requested generic
/// quantity never silently changes its operating mode.
/// </summary>
public interface IModeAwareMockInstrument
{
    ValueTask ConfigureModeAsync(string mode, CancellationToken ct = default);
    ValueTask<TQuantity> MeasureAsync<TQuantity>(CancellationToken ct = default)
        where TQuantity : struct, IQuantity;
    string? SelectedMode { get; }
}

/// <summary>
/// Defines legal modes and mode-dependent measurement behaviors. This builder
/// will eventually be populated by reviewed Instrument Definer v2 metadata.
/// </summary>
public sealed class ModeAwareMockBuilder
{
    private readonly HashSet<string> _modes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Mode, Type Quantity), Func<TimeSpan, object>>
        _measurements = new();

    public ModeAwareMockBuilder Mode(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_modes.Add(name))
            throw new InvalidOperationException($"Duplicate instrument mode '{name}'.");
        return this;
    }

    /// <summary>
    /// Register a UnitsNet measurement in a declared mode; the behavior sees
    /// the shared experiment's virtual elapsed time.
    /// </summary>
    public ModeAwareMockBuilder Measurement<TQuantity>(
        string mode, Func<TimeSpan, TQuantity> behavior)
        where TQuantity : struct, IQuantity
    {
        ArgumentNullException.ThrowIfNull(behavior);
        if (!_modes.Contains(mode))
            throw new InvalidOperationException($"Declare mode '{mode}' first.");
        if (!_measurements.TryAdd((mode, typeof(TQuantity)), t => behavior(t)))
            throw new InvalidOperationException(
                $"Duplicate {typeof(TQuantity).Name} measurement in mode '{mode}'.");
        return this;
    }

    public ModeAwareMockInstrument Build(ISimulationClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (_modes.Count == 0)
            throw new InvalidOperationException("At least one mode is required.");
        return new ModeAwareMockInstrument(clock,
            new HashSet<string>(_modes, StringComparer.Ordinal),
            new Dictionary<(string, Type), Func<TimeSpan, object>>(_measurements));
    }
}

/// <summary>
/// A simulated-only, mode-safe UnitsNet measurement instrument. All mode
/// changes and reads are serialized; there is no implicit physical I/O.
/// </summary>
public sealed class ModeAwareMockInstrument : IModeAwareMockInstrument
{
    private readonly ISimulationClock _clock;
    private readonly HashSet<string> _modes;
    private readonly IReadOnlyDictionary<(string Mode, Type Quantity),
        Func<TimeSpan, object>> _measurements;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile string? _mode;

    internal ModeAwareMockInstrument(
        ISimulationClock clock, HashSet<string> modes,
        IReadOnlyDictionary<(string Mode, Type Quantity),
            Func<TimeSpan, object>> measurements)
    {
        _clock = clock;
        _modes = modes;
        _measurements = measurements;
    }

    public string? SelectedMode => _mode;

    public async ValueTask ConfigureModeAsync(string mode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_modes.Contains(mode))
                throw new NotSupportedException($"Unsupported instrument mode '{mode}'.");
            _mode = mode;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<TQuantity> MeasureAsync<TQuantity>(
        CancellationToken ct = default) where TQuantity : struct, IQuantity
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_mode is null)
                throw new InvalidOperationException("Select a measurement mode first.");
            if (!_measurements.TryGetValue((_mode, typeof(TQuantity)), out var behavior))
                throw new InvalidOperationException(
                    $"Mode '{_mode}' cannot measure {typeof(TQuantity).Name}.");
            var elapsed = _clock.Elapsed;
            if (elapsed < TimeSpan.Zero) throw new InvalidOperationException("Simulation clock returned negative elapsed time.");
            var result = (TQuantity)behavior(elapsed);
            ct.ThrowIfCancellationRequested();
            double canonical;
            try { canonical = result.As(result.QuantityInfo.BaseUnitInfo.Value); }
            catch (ArgumentException error) { throw new InvalidOperationException("Mock reading cannot be represented in its base unit.", error); }
            if (!double.IsFinite(canonical)) throw new InvalidOperationException("Nonfinite mock reading.");
            return result;
        }
        finally { _gate.Release(); }
    }
}
