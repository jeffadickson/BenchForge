using UnitsNet;

namespace BenchDouble.Quantities;

/// <summary>Strongly typed measurement results; legacy double APIs remain available.</summary>
public interface ITypedDigitalMultimeter
{
    ElectricPotential MeasureDcVoltage();
    ElectricResistance MeasureResistance();
    ElectricCurrent MeasureDcCurrent();
}

/// <summary>Optional quantity-safe generator API.</summary>
public interface ITypedFunctionGenerator
{
    void ConfigureSine(Frequency frequency, ElectricPotential amplitudeVpp, int channel = 1);
}

/// <summary>Small, explicit adapter over current partial DMM SCPI support.
/// Only DC voltage is implemented; unsupported quantities fail predictably.</summary>
public sealed class TypedDmmAdapter(DigitalMultimeter meter) : ITypedDigitalMultimeter
{
    public ElectricPotential MeasureDcVoltage() =>
        ElectricPotential.FromVolts(meter.MeasureDcVoltage());

    public ElectricResistance MeasureResistance() =>
        throw new NotSupportedException("Resistance is not implemented by this instrument profile.");

    public ElectricCurrent MeasureDcCurrent() =>
        throw new NotSupportedException("Current is not implemented by this instrument profile.");
}

/// <summary>Typed function generator facade preserving the existing SCPI commands.</summary>
public sealed class TypedFunctionGeneratorAdapter(FunctionGenerator generator) : ITypedFunctionGenerator
{
    public void ConfigureSine(Frequency frequency, ElectricPotential amplitudeVpp, int channel = 1)
    {
        if (!double.IsFinite(frequency.Hertz) || frequency.Hertz <= 0)
            throw new ArgumentOutOfRangeException(nameof(frequency));
        if (!double.IsFinite(amplitudeVpp.Volts) || amplitudeVpp.Volts <= 0)
            throw new ArgumentOutOfRangeException(nameof(amplitudeVpp));
        generator.SetSine(frequency.Hertz, amplitudeVpp.Volts, channel);
    }
}

/// <summary>Combines a strongly typed quantity with provenance and quality.
/// Uncertainty, calibration, and time synchronization will be added as separate metadata.</summary>
public sealed record QuantityMeasurement<TQuantity>(
    TQuantity Value,
    DateTimeOffset Timestamp,
    string InstrumentId,
    string Quality = "Good") where TQuantity : struct;
