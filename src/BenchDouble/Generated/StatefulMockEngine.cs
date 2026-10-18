using System.Collections.ObjectModel;
using BenchDouble.Simulation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BenchDouble.Generated;

/// <summary>Input to a virtual-time scalar measurement model.</summary>
/// <param name="Mode">Active instrument mode.</param>
/// <param name="Capability">Requested capability.</param>
/// <param name="Elapsed">Run-scoped virtual time.</param>
/// <param name="Revision">Monotonic state revision.</param>
/// <param name="Fallback">Explicit override or definition default.</param>
public sealed record MockReadingContext(string Mode, string Capability, TimeSpan Elapsed, long Revision, double? Fallback);

/// <summary>Independent immutable copy of a mock instrument's state.</summary>
/// <param name="Mode">Current mode, if selected.</param>
/// <param name="Revision">Monotonic state revision.</param>
/// <param name="Elapsed">Virtual time at capture.</param>
/// <param name="Overrides">Explicit readings copied at capture.</param>
public sealed record StatefulMockSnapshot(string? Mode, long Revision, TimeSpan Elapsed, IReadOnlyDictionary<string, double> Overrides);

/// <summary>Provenance-preserving scalar measurement from a coherent virtual acquisition.</summary>
/// <param name="AcquisitionId">Shared SMU and oscilloscope acquisition identifier.</param>
/// <param name="SourceRevision">Configuration revision selected at the acquisition boundary.</param>
/// <param name="Elapsed">Virtual acquisition timestamp.</param>
/// <param name="Capability">Imported capability identifier.</param>
/// <param name="Quantity">Physical quantity name.</param>
/// <param name="Unit">Canonical unit identifier.</param>
/// <param name="Value">Finite captured scalar value.</param>
/// <param name="Mode">Mock operating mode used to authorize the reading.</param>
/// <param name="MockRevision">Mock state revision at authorization.</param>
public sealed record CapturedMockReading(Guid AcquisitionId, long SourceRevision, TimeSpan Elapsed,
    string Capability, string Quantity, string Unit, double Value, string Mode, long MockRevision);

/// <summary>Exclusive mode-aware simulation state, with virtual-time behaviors and one-shot faults.</summary>
/// <remarks>Callbacks execute inside the exclusive transaction and must never reenter this engine.</remarks>
public sealed class StatefulMockEngine
{
    private readonly ImportedInstrumentDefinition _definition;
    private readonly ISimulationClock _clock;
    private readonly ILogger<StatefulMockEngine> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, double> _overrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<MockReadingContext, double>> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<MockReadingContext, CancellationToken, ValueTask<double>>> _asyncModels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<Exception>> _faults = new(StringComparer.Ordinal);
    private string? _mode;
    private long _revision;
    private long _loggingFailures;

    /// <summary>Number of diagnostic sink failures; never changes simulated I/O outcomes.</summary>
    public long LoggingFailureCount => Interlocked.Read(ref _loggingFailures);

    private void Emit(Action emit)
    {
        try { emit(); } catch { Interlocked.Increment(ref _loggingFailures); }
    }

    /// <summary>Construct a simulated instrument with injected clock and logger.</summary>
    /// <param name="definition">Validated instrument definition.</param>
    /// <param name="clock">Optional virtual clock, defaulting to an independent manual clock.</param>
    /// <param name="logger">Optional structured logger.</param>
    public StatefulMockEngine(ImportedInstrumentDefinition definition, ISimulationClock? clock = null,
        ILogger<StatefulMockEngine>? logger = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _clock = clock ?? new ManualSimulationClock();
        _logger = logger ?? NullLogger<StatefulMockEngine>.Instance;
    }

    /// <summary>Atomically select a supported operating mode.</summary>
    /// <param name="mode">Declared mode identifier.</param>
    /// <param name="ct">Cancellation while waiting for exclusive access.</param>
    /// <returns>Completion of mode transition.</returns>
    public async ValueTask ConfigureModeAsync(string mode, CancellationToken ct = default)
    {
        _definition.ModeCommand(mode);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_mode == mode) return;
            _mode = mode;
            ++_revision;
            Emit(() => _logger.LogInformation("Mock {Instrument} mode {Mode} revision {Revision}", _definition.DriverName, mode, _revision));
        }
        finally { _gate.Release(); }
    }

    /// <summary>Measure a compatible capability using a model, override, or definition default.</summary>
    /// <param name="capability">Declared scalar capability.</param>
    /// <param name="ct">Cancellation while waiting for the transaction.</param>
    /// <returns>Finite reading in the declared canonical unit.</returns>
    public async ValueTask<double> MeasureAsync(string capability, CancellationToken ct = default)
    {
        var metadata = _definition.Capability(capability);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_mode is null || !metadata.Modes.Contains(_mode))
                throw new InvalidOperationException("Incompatible measurement mode.");
            if (_faults.TryGetValue(capability, out var faults) && faults.Count > 0)
            {
                var error = faults.Dequeue();
                ++_revision;
                Emit(() => _logger.LogWarning("Mock {Instrument} fault on {Capability}; ErrorType={ErrorType}", _definition.DriverName, capability, error.GetType().Name));
                throw error;
            }
            double? fallback = _overrides.TryGetValue(capability, out var value) ? value : metadata.MockDefault;
            var context = new MockReadingContext(_mode, capability, _clock.Elapsed, _revision, fallback);
            double result = _asyncModels.TryGetValue(capability, out var asyncModel)
                ? await asyncModel(context, ct).ConfigureAwait(false)
                : _models.TryGetValue(capability, out var model) ? model(context)
                : fallback ?? throw new InvalidOperationException("No configured mock reading.");
            ct.ThrowIfCancellationRequested();
            if (!double.IsFinite(result)) throw new InvalidOperationException("Nonfinite mock reading.");
            return result;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Measure voltage or current from a fixed acquisition with complete provenance.</summary>
    /// <remarks>
    /// Values come from the immutable source snapshot, not a live bus or a mutable override.
    /// Mode and quantity checks precede one-shot fault consumption. The engine's transaction
    /// gate serializes authorization against mode changes and injected faults.
    /// </remarks>
    /// <param name="capability">Declared voltage or current capability.</param>
    /// <param name="acquisition">Correlated source and waveform snapshot.</param>
    /// <param name="ct">Cancellation while waiting for exclusive mock access.</param>
    /// <returns>Canonical scalar reading with source and mock revision identifiers.</returns>
    /// <exception cref="ArgumentException">Acquisition components, identity or timing are inconsistent.</exception>
    public async ValueTask<CapturedMockReading> ReadCapturedAsync(string capability,
        VirtualCoherentAcquisition acquisition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        var metadata = _definition.Capability(capability);
        if (acquisition.Measurement is null || acquisition.SourceState is null ||
            acquisition.Waveform is null || acquisition.AcquisitionId == Guid.Empty ||
            acquisition.Waveform.AcquisitionId != acquisition.AcquisitionId ||
            acquisition.Waveform.TimeAt(0).Seconds != acquisition.Measurement.Elapsed.TotalSeconds)
            throw new ArgumentException("Inconsistent acquisition provenance.", nameof(acquisition));
        double value = (metadata.Quantity, metadata.Unit) switch
        {
            ("ElectricPotential", "Volt") => acquisition.Voltmeter.Volts,
            ("ElectricCurrent", "Ampere") => acquisition.Ammeter.Amperes,
            _ => throw new NotSupportedException("Only voltage and current can be projected from a source snapshot.")
        };
        if (!double.IsFinite(value))
            throw new InvalidOperationException("Nonfinite captured measurement.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_mode is null || !metadata.Modes.Contains(_mode))
                throw new InvalidOperationException("Incompatible measurement mode.");
            if (_faults.TryGetValue(capability, out var faults) && faults.Count > 0)
            {
                var error = faults.Dequeue();
                ++_revision;
                Emit(() => _logger.LogWarning(
                    "Mock {Instrument} acquisition {AcquisitionId} fault on {Capability}; ErrorType={ErrorType}",
                    _definition.DriverName, acquisition.AcquisitionId, capability, error.GetType().Name));
                throw error;
            }
            Emit(() => _logger.LogDebug(
                "Mock {Instrument} acquisition {AcquisitionId} source revision {SourceRevision} mock revision {MockRevision} capability {Capability}",
                _definition.DriverName, acquisition.AcquisitionId, acquisition.SourceRevision, _revision, capability));
            return new CapturedMockReading(acquisition.AcquisitionId, acquisition.SourceRevision,
                acquisition.Measurement.Elapsed, capability, metadata.Quantity, metadata.Unit, value, _mode, _revision);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Read a captured voltage or current value without discarding validation.</summary>
    /// <param name="capability">Declared scalar capability.</param>
    /// <param name="acquisition">Immutable coherent acquisition.</param>
    /// <param name="ct">Cancellation while waiting for the transaction.</param>
    /// <returns>Canonical scalar value.</returns>
    public async ValueTask<double> MeasureCapturedAsync(string capability,
        VirtualCoherentAcquisition acquisition, CancellationToken ct = default) =>
        (await ReadCapturedAsync(capability, acquisition, ct).ConfigureAwait(false)).Value;

    /// <summary>Override a capability with a finite canonical-unit reading.</summary>
    /// <param name="capability">Declared capability.</param>
    /// <param name="value">Finite scalar in the declared unit.</param>
    public void SetReading(string capability, double value)
    {
        _definition.Capability(capability);
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        _gate.Wait();
        try
        {
            if (_overrides.TryGetValue(capability, out var existing) && existing == value) return;
            _overrides[capability] = value; ++_revision;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Remove one explicit reading, restoring its definition fallback.</summary>
    /// <param name="capability">Declared scalar capability.</param>
    public void ClearReading(string capability)
    {
        _definition.Capability(capability);
        _gate.Wait();
        try { if (_overrides.Remove(capability)) ++_revision; }
        finally { _gate.Release(); }
    }

    /// <summary>Remove all explicit scalar overrides without changing the operating mode.</summary>
    public void ClearAllReadings()
    {
        _gate.Wait();
        try
        {
            if (_overrides.Count == 0) return;
            _overrides.Clear();
            ++_revision;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Install or remove a non-reentrant deterministic model.</summary>
    /// <param name="capability">Declared capability.</param>
    /// <param name="behavior">Pure model or null for definition fallback.</param>
    public void SetBehavior(string capability, Func<MockReadingContext, double>? behavior)
    {
        _definition.Capability(capability);
        _gate.Wait();
        try
        {
            if (behavior is null && !_models.ContainsKey(capability) && !_asyncModels.ContainsKey(capability)) return;
            _asyncModels.Remove(capability);
            if (behavior is null) _models.Remove(capability);
            else _models[capability] = behavior;
            ++_revision;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Install or remove an async model serialized with mode transitions.</summary>
    /// <remarks>The callback executes inside the exclusive transaction and must not reenter this engine.</remarks>
    /// <param name="capability">Declared scalar capability.</param>
    /// <param name="behavior">Cancellation-aware async model, or null to restore fallback behavior.</param>
    public void SetAsyncBehavior(string capability,
        Func<MockReadingContext, CancellationToken, ValueTask<double>>? behavior)
    {
        _definition.Capability(capability);
        _gate.Wait();
        try
        {
            if (behavior is null && !_models.ContainsKey(capability) && !_asyncModels.ContainsKey(capability)) return;
            _models.Remove(capability);
            if (behavior is null) _asyncModels.Remove(capability);
            else _asyncModels[capability] = behavior;
            ++_revision;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Queue a fault consumed only by the next mode-compatible measurement.</summary>
    /// <param name="capability">Declared capability.</param>
    /// <param name="failure">Exception to throw once.</param>
    public void QueueFault(string capability, Exception failure)
    {
        _definition.Capability(capability);
        ArgumentNullException.ThrowIfNull(failure);
        _gate.Wait();
        try
        {
            if (!_faults.TryGetValue(capability, out var queue))
                _faults[capability] = queue = new Queue<Exception>();
            queue.Enqueue(failure);
            ++_revision;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Discard pending faults for one capability without changing mode, readings or models.</summary>
    /// <param name="capability">Declared scalar capability.</param>
    /// <returns>Number of discarded faults; zero does not advance the revision.</returns>
    public int ClearFaults(string capability)
    {
        _definition.Capability(capability);
        _gate.Wait();
        try
        {
            if (!_faults.Remove(capability, out var faults) || faults.Count == 0) return 0;
            ++_revision;
            return faults.Count;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Capture immutable mode, virtual time, revision and explicit readings.</summary>
    /// <returns>Independent state snapshot.</returns>
    public StatefulMockSnapshot Snapshot()
    {
        _gate.Wait();
        try { return new(_mode, _revision, _clock.Elapsed,
            new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(_overrides))); }
        finally { _gate.Release(); }
    }

    /// <summary>Reset the mode, overrides, behaviors and pending faults.</summary>
    public void Reset()
    {
        _gate.Wait();
        try
        {
            _mode = null;
            _overrides.Clear();
            _models.Clear();
            _asyncModels.Clear();
            _faults.Clear();
            ++_revision;
            Emit(() => _logger.LogInformation("Mock {Instrument} reset revision {Revision}", _definition.DriverName, _revision));
        }
        finally { _gate.Release(); }
    }
}
