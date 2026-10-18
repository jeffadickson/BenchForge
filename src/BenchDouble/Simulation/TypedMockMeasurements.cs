using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>
/// A measurement source with an explicitly requested UnitsNet quantity type.
/// C# does not infer TQuantity from the assignment target. The instrument
/// remains responsible for mode and capability validation.
/// </summary>
public interface ITypedMeasurementSource
{
    ValueTask<TQuantity> MeasureAsync<TQuantity>(CancellationToken ct = default)
        where TQuantity : struct, IQuantity;
}

/// <summary>Generic measurement source with explicit instrument mode selection.</summary>
public interface ITypedConfigurableMeasurementSource : ITypedMeasurementSource
{
    ValueTask ConfigureAsync<TQuantity>(CancellationToken ct = default)
        where TQuantity : struct, IQuantity;
}

/// <summary>
/// Configures which quantity types a mock instrument supports. Registering a
/// quantity binds a strongly typed behavior; unregistered quantities fail
/// rather than silently returning an arbitrary zero or converting dimensions.
/// </summary>
public sealed class TypedMockMeasurementsBuilder
{
    private readonly Dictionary<Type, Func<TimeSpan, object>> _handlers = new();

    /// <summary>Register a deterministic, time-indexed measurement behavior.</summary>
    public TypedMockMeasurementsBuilder Register<TQuantity>(Func<TimeSpan, TQuantity> behavior)
        where TQuantity : struct, IQuantity
    {
        ArgumentNullException.ThrowIfNull(behavior);
        if (!_handlers.TryAdd(typeof(TQuantity), elapsed => behavior(elapsed)))
            throw new InvalidOperationException(
                $"Measurement quantity {typeof(TQuantity).Name} is already registered.");
        return this;
    }

    /// <summary>Register a constant typed measurement, useful for simple AutoMocks.</summary>
    public TypedMockMeasurementsBuilder Constant<TQuantity>(TQuantity value)
        where TQuantity : struct, IQuantity =>
        Register<TQuantity>(_ => value);

    /// <summary>Freeze registrations so each experiment run has a stable capability set.</summary>
    public TypedMockMeasurements Build() =>
        new(new Dictionary<Type, Func<TimeSpan, object>>(_handlers));
}

/// <summary>
/// An immutable, strongly typed quantity-to-behavior registry. The internal
/// Type dictionary is implementation detail, not a public string-keyed signal bus.
/// </summary>
public sealed class TypedMockMeasurements
{
    private readonly IReadOnlyDictionary<Type, Func<TimeSpan, object>> _handlers;

    internal TypedMockMeasurements(IReadOnlyDictionary<Type, Func<TimeSpan, object>> handlers) =>
        _handlers = handlers;

    public bool Supports<TQuantity>() where TQuantity : struct, IQuantity =>
        _handlers.ContainsKey(typeof(TQuantity));

    public IReadOnlyList<Type> SupportedQuantities => _handlers.Keys.ToArray();

    public TQuantity Evaluate<TQuantity>(TimeSpan elapsed)
        where TQuantity : struct, IQuantity
    {
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (!_handlers.TryGetValue(typeof(TQuantity), out var behavior))
            throw new NotSupportedException(
                $"Mock does not support measurement of {typeof(TQuantity).Name}.");
        // The registration API ensures the delegate returns the requested
        // quantity type; it is never inferred from a numeric value.
        var result = (TQuantity)behavior(elapsed);
        double canonical;
        try { canonical = result.As(result.QuantityInfo.BaseUnitInfo.Value); }
        catch (ArgumentException error) { throw new InvalidOperationException("Mock reading cannot be represented in its base unit.", error); }
        if (!double.IsFinite(canonical)) throw new InvalidOperationException("Nonfinite typed mock reading.");
        return result;
    }
}

/// <summary>
/// Stateful DMM-like mock. Mode must be explicitly selected; a generic
/// measurement request never silently changes instrument configuration.
/// </summary>
public sealed class TypedMockMeter(
    TypedMockMeasurements measurements,
    ISimulationClock clock) : ITypedConfigurableMeasurementSource
{
    private readonly TypedMockMeasurements _measurements = measurements ?? throw new ArgumentNullException(nameof(measurements));
    private readonly ISimulationClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Type? _selectedQuantity;

    /// <summary>Configure a supported measurement mode using its quantity type.</summary>
    public async ValueTask ConfigureAsync<TQuantity>(CancellationToken ct = default)
        where TQuantity : struct, IQuantity
    {
        if (!_measurements.Supports<TQuantity>())
            throw new NotSupportedException(
                $"Meter does not support {typeof(TQuantity).Name}.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { _selectedQuantity = typeof(TQuantity); }
        finally { _gate.Release(); }
    }

    /// <summary>Read the selected mode; reject an incompatible requested type.</summary>
    public async ValueTask<TQuantity> MeasureAsync<TQuantity>(
        CancellationToken ct = default)
        where TQuantity : struct, IQuantity
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_selectedQuantity is null)
                throw new InvalidOperationException("Configure the meter mode before measuring.");
            if (_selectedQuantity != typeof(TQuantity))
                throw new InvalidOperationException(
                    $"Meter is configured for {_selectedQuantity.Name}, not {typeof(TQuantity).Name}.");
            var result = _measurements.Evaluate<TQuantity>(_clock.Elapsed);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        finally { _gate.Release(); }
    }
}
