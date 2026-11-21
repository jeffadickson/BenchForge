using UnitsNet;

namespace BenchForge.Quantities;

/// <summary>Canonical physical dimensions supported across BenchForge drivers, mocks, and unit adapters.</summary>
public enum QuantityDimension
{
    /// <summary>Electric potential in volts (V).</summary>
    ElectricPotential,

    /// <summary>Electric current in amperes (A).</summary>
    ElectricCurrent,

    /// <summary>Electric resistance in ohms (Ohm).</summary>
    ElectricResistance,

    /// <summary>Frequency in hertz (Hz).</summary>
    Frequency,

    /// <summary>Power in watts (W).</summary>
    Power,

    /// <summary>Temperature in degrees Celsius (degC).</summary>
    Temperature,

    /// <summary>Time duration in seconds (s).</summary>
    Duration,

    /// <summary>Electric capacitance in farads (F).</summary>
    Capacitance,

    /// <summary>Dimensionless percentage ratio (%).</summary>
    Ratio
}

/// <summary>
/// Library-neutral immutable scalar physical quantity expressed in BenchForge's canonical unit.
/// Serves as the stable boundary between BenchForge core, UnitsNet, and future unit libraries such as SharpUnits.
/// </summary>
/// <param name="Dimension">Physical dimension of the quantity.</param>
/// <param name="Value">Finite numeric magnitude in <paramref name="CanonicalUnit"/>.</param>
/// <param name="CanonicalUnit">Canonical unit symbol (for example, <c>V</c>, <c>A</c>, <c>Ohm</c>, <c>Hz</c>).</param>
public readonly record struct CanonicalQuantity(
    QuantityDimension Dimension,
    double Value,
    string CanonicalUnit)
{
    /// <summary>Validate that the value is finite and the unit symbol is non-empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The numeric magnitude is NaN or infinite.</exception>
    /// <exception cref="ArgumentException">The unit symbol is blank.</exception>
    public void Validate()
    {
        if (!double.IsFinite(Value))
            throw new ArgumentOutOfRangeException(nameof(Value), "Quantity magnitude must be finite.");
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalUnit);
    }
}

/// <summary>
/// Adapter seam bridging BenchForge's library-neutral <see cref="CanonicalQuantity"/> with an external
/// strongly typed unit system (such as UnitsNet today or SharpUnits when available).
/// </summary>
/// <typeparam name="TExternal">External quantity base type or discriminated union.</typeparam>
public interface IQuantityConverter<TExternal>
{
    /// <summary>Convert an external typed quantity into a validated <see cref="CanonicalQuantity"/>.</summary>
    /// <param name="quantity">External typed quantity instance.</param>
    /// <returns>Library-neutral canonical quantity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quantity"/> is null.</exception>
    /// <exception cref="NotSupportedException">The external quantity's dimension is unsupported.</exception>
    CanonicalQuantity ToCanonical(TExternal quantity);

    /// <summary>Convert a <see cref="CanonicalQuantity"/> into the external unit system.</summary>
    /// <param name="quantity">Validated canonical quantity.</param>
    /// <returns>Converted external typed quantity.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The canonical magnitude is not finite.</exception>
    /// <exception cref="NotSupportedException">The dimension is unsupported by this converter.</exception>
    TExternal FromCanonical(CanonicalQuantity quantity);
}

/// <summary>Default <see cref="IQuantityConverter{TExternal}"/> implementation backed by <see cref="UnitsNet.IQuantity"/>.</summary>
public sealed class UnitsNetQuantityConverter : IQuantityConverter<IQuantity>
{
    /// <summary>Shared stateless converter instance.</summary>
    public static UnitsNetQuantityConverter Instance { get; } = new();

    /// <inheritdoc/>
    public CanonicalQuantity ToCanonical(IQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        CanonicalQuantity canonical = quantity switch
        {
            ElectricPotential v => new CanonicalQuantity(QuantityDimension.ElectricPotential, v.Volts, "V"),
            ElectricCurrent a => new CanonicalQuantity(QuantityDimension.ElectricCurrent, a.Amperes, "A"),
            ElectricResistance r => new CanonicalQuantity(QuantityDimension.ElectricResistance, r.Ohms, "Ohm"),
            Frequency f => new CanonicalQuantity(QuantityDimension.Frequency, f.Hertz, "Hz"),
            Power p => new CanonicalQuantity(QuantityDimension.Power, (double)p.Watts, "W"),
            Temperature t => new CanonicalQuantity(QuantityDimension.Temperature, t.DegreesCelsius, "degC"),
            Duration d => new CanonicalQuantity(QuantityDimension.Duration, d.Seconds, "s"),
            ElectricCapacitance c => new CanonicalQuantity(QuantityDimension.Capacitance, c.Farads, "F"),
            Ratio ratio => new CanonicalQuantity(QuantityDimension.Ratio, ratio.Percent, "%"),
            _ => throw new NotSupportedException($"Unsupported UnitsNet quantity type '{quantity.GetType().Name}'.")
        };
        canonical.Validate();
        return canonical;
    }

    /// <inheritdoc/>
    public IQuantity FromCanonical(CanonicalQuantity quantity)
    {
        quantity.Validate();
        return quantity.Dimension switch
        {
            QuantityDimension.ElectricPotential => ElectricPotential.FromVolts(quantity.Value),
            QuantityDimension.ElectricCurrent => ElectricCurrent.FromAmperes(quantity.Value),
            QuantityDimension.ElectricResistance => ElectricResistance.FromOhms(quantity.Value),
            QuantityDimension.Frequency => Frequency.FromHertz(quantity.Value),
            QuantityDimension.Power => Power.FromWatts((decimal)quantity.Value),
            QuantityDimension.Temperature => Temperature.FromDegreesCelsius(quantity.Value),
            QuantityDimension.Duration => Duration.FromSeconds(quantity.Value),
            QuantityDimension.Capacitance => ElectricCapacitance.FromFarads(quantity.Value),
            QuantityDimension.Ratio => Ratio.FromPercent(quantity.Value),
            _ => throw new NotSupportedException($"Unsupported physical dimension '{quantity.Dimension}'.")
        };
    }
}
