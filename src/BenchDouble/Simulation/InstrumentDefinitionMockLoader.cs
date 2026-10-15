using System.Text.Json;
using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>
/// Converts reviewed or unreviewed schema-v2 *data* into a simulation preview.
/// This never creates a physical driver, evaluates expressions or sends SCPI.
/// Unknown quantities and missing mock defaults are not silently invented.
/// </summary>
public static class InstrumentDefinitionMockLoader
{
    public static ModeAwareMockInstrument CreatePreview(string json, ISimulationClock clock)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(clock);
        if (json.Length > 512 * 1024)
            throw new ArgumentException("Instrument definition exceeds 512 KiB.", nameof(json));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2)
            throw new NotSupportedException("Expected Instrument Definer schema version 2.");
        var modes = root.GetProperty("modes");
        var builder = new ModeAwareMockBuilder();
        foreach (var mode in modes.EnumerateArray())
            builder.Mode(mode.GetProperty("id").GetString()
                ?? throw new FormatException("Mode id is required."));
        foreach (var cap in root.GetProperty("capabilities").EnumerateArray())
        {
            if (!cap.TryGetProperty("mockDefault", out var value) ||
                value.ValueKind != JsonValueKind.Number)
                continue; // Unknown readings remain unsupported, never zero by guess.
            double number = value.GetDouble();
            if (!double.IsFinite(number))
                throw new FormatException("Mock default must be finite.");
            if (cap.TryGetProperty("minimum", out var low) &&
                low.ValueKind == JsonValueKind.Number && number < low.GetDouble())
                throw new FormatException("Mock default is below minimum.");
            if (cap.TryGetProperty("maximum", out var high) &&
                high.ValueKind == JsonValueKind.Number && number > high.GetDouble())
                throw new FormatException("Mock default exceeds maximum.");
            var quantity = cap.GetProperty("quantity").GetString();
            var unit = cap.GetProperty("unit").GetString();
            var access = cap.GetProperty("access").GetString();
            if (access is not ("read" or "readwrite"))
                continue;
            foreach (var mode in cap.GetProperty("modes").EnumerateArray())
            {
                string modeId = mode.GetString()
                    ?? throw new FormatException("Capability mode must be named.");
                switch ((quantity, unit))
                {
                    case ("ElectricPotential", "Volt"):
                        builder.Measurement(modeId, _ => ElectricPotential.FromVolts(number));
                        break;
                    case ("ElectricCurrent", "Ampere"):
                        builder.Measurement(modeId, _ => ElectricCurrent.FromAmperes(number));
                        break;
                    case ("ElectricResistance", "Ohm"):
                        builder.Measurement(modeId, _ => ElectricResistance.FromOhms(number));
                        break;
                    case ("Frequency", "Hertz"):
                        builder.Measurement(modeId, _ => Frequency.FromHertz(number));
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Mock preview cannot generate quantity {quantity}/{unit}.");
                }
            }
        }
        return builder.Build(clock);
    }
}
