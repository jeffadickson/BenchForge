using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UnitsNet;

namespace BenchForge.Flight;

/// <summary>
/// Physical quantity exported from Cameo Systems Modeler / SysML (QUDV) pairing a finite numeric value with an
/// explicit physical unit symbol and optional QUDV metadata. Supports deserialization from either a SysML JSON object
/// (<c>{ "value": 250, "unit": "mA" }</c>) or a compact quantity string (<c>"250 mA"</c>).
/// </summary>
/// <param name="Value">Finite numeric magnitude expressed in <paramref name="Unit"/>.</param>
/// <param name="Unit">Explicit physical unit symbol (for example, <c>V</c>, <c>mA</c>, <c>dBm</c>, <c>MHz</c>, <c>degC</c>, <c>uF</c>, <c>s</c>).</param>
/// <param name="QuantityKind">Optional SysML QUDV quantity kind (for example, <c>ElectricPotential</c>, <c>ElectricCurrent</c>, <c>Frequency</c>).</param>
/// <param name="ValueType">Optional SysML QUDV value type name (for example, <c>Volts</c>, <c>Milliamperes</c>, <c>Megahertz</c>).</param>
[JsonConverter(typeof(CameoQuantityJsonConverter))]
public sealed record CameoQuantity(
    double Value,
    string Unit,
    string? QuantityKind = null,
    string? ValueType = null)
{
    /// <summary>Parse a compact quantity string (for example, <c>"28.0 V"</c>, <c>"250 mA"</c>, <c>"10 MHz"</c>, <c>"7 dBm"</c>, <c>"30 s"</c>).</summary>
    /// <param name="text">Quantity literal containing a finite number followed by a unit symbol.</param>
    /// <returns>Parsed <see cref="CameoQuantity"/> with explicit unit.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is empty, malformed, or omits its physical unit.</exception>
    public static CameoQuantity Parse(string text)
    {
        if (TryParse(text, out var parsed, out var error))
            return parsed!;
        throw new ArgumentException(error, nameof(text));
    }

    /// <summary>Attempt to parse a compact Cameo quantity string into a <see cref="CameoQuantity"/>.</summary>
    /// <param name="text">Quantity literal containing a finite number and explicit physical unit.</param>
    /// <param name="quantity">Parsed quantity on success; otherwise null.</param>
    /// <param name="error">Human-readable validation message on failure.</param>
    /// <returns>True when parsing succeeded with a finite value and non-empty unit.</returns>
    public static bool TryParse(string? text, out CameoQuantity? quantity, out string error)
    {
        quantity = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Cameo quantity literal must not be empty.";
            return false;
        }

        var match = CameoUnitConverter.QuantityLiteralRegex.Match(text);
        if (!match.Success ||
            !double.TryParse(match.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double num) ||
            !double.IsFinite(num))
        {
            error = $"Invalid Cameo quantity literal '{text}'. Expected '<number> <unit>' (for example, '28 V', '250 mA', '10 MHz').";
            return false;
        }

        string unit = match.Groups["unit"].Value.Trim();
        if (string.IsNullOrWhiteSpace(unit))
        {
            error = $"Cameo quantity literal '{text}' is missing an explicit physical unit.";
            return false;
        }

        quantity = new CameoQuantity(num, unit);
        error = string.Empty;
        return true;
    }

    /// <summary>Format this quantity as a compact invariant string (<c>"&lt;value&gt; &lt;unit&gt;"</c>).</summary>
    /// <returns>Human-readable quantity string with unit.</returns>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0:G12} {1}", Value, Unit);

    /// <summary>Convert this quantity into a <see cref="CameoValueProperty"/> bound to <paramref name="name"/>.</summary>
    /// <param name="name">SysML value property name.</param>
    /// <returns>Typed <see cref="CameoValueProperty"/> preserving value, unit, quantity kind, and value type.</returns>
    public CameoValueProperty ToValueProperty(string name) =>
        new(name, Value, Unit, QuantityKind, ValueType);
}

/// <summary>
/// JSON converter for <see cref="CameoQuantity"/> accepting either SysML QUDV object syntax
/// (<c>{ "value": 10, "unit": "MHz", "quantityKind": "Frequency" }</c>) or compact string syntax (<c>"10 MHz"</c>).
/// </summary>
public sealed class CameoQuantityJsonConverter : JsonConverter<CameoQuantity>
{
    /// <summary>Read a <see cref="CameoQuantity"/> from a JSON string literal or SysML value object.</summary>
    /// <param name="reader">Active UTF-8 JSON reader.</param>
    /// <param name="typeToConvert">Target CLR type.</param>
    /// <param name="options">Serializer options.</param>
    /// <returns>Deserialized <see cref="CameoQuantity"/>.</returns>
    public override CameoQuantity? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            string? text = reader.GetString();
            if (!CameoQuantity.TryParse(text, out var parsed, out var err))
                throw new JsonException(err);
            return parsed;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            double? value = null;
            string? unit = null;
            string? quantityKind = null;
            string? valueType = null;

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.Equals("value", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out double d))
                        value = d;
                    else
                        throw new JsonException("Cameo quantity object 'value' must be a finite number.");
                }
                else if (prop.Name.Equals("unit", StringComparison.OrdinalIgnoreCase))
                {
                    unit = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                }
                else if (prop.Name.Equals("quantityKind", StringComparison.OrdinalIgnoreCase))
                {
                    quantityKind = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                }
                else if (prop.Name.Equals("valueType", StringComparison.OrdinalIgnoreCase))
                {
                    valueType = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                }
            }

            if (value is null || !double.IsFinite(value.Value))
                throw new JsonException("Cameo quantity object requires a finite numeric 'value'.");
            if (string.IsNullOrWhiteSpace(unit))
                throw new JsonException("Cameo quantity object requires a non-empty 'unit' symbol.");

            return new CameoQuantity(value.Value, unit.Trim(), quantityKind, valueType);
        }

        throw new JsonException($"Expected string or object for CameoQuantity, found {reader.TokenType}.");
    }

    /// <summary>Write a <see cref="CameoQuantity"/> in SysML QUDV JSON object format preserving value, unit, and optional metadata.</summary>
    /// <param name="writer">Active UTF-8 JSON writer.</param>
    /// <param name="value">Quantity to serialize.</param>
    /// <param name="options">Serializer options.</param>
    public override void Write(Utf8JsonWriter writer, CameoQuantity value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteNumber("value", value.Value);
        writer.WriteString("unit", value.Unit);
        if (!string.IsNullOrWhiteSpace(value.QuantityKind))
            writer.WriteString("quantityKind", value.QuantityKind);
        if (!string.IsNullOrWhiteSpace(value.ValueType))
            writer.WriteString("valueType", value.ValueType);
        writer.WriteEndObject();
    }
}

/// <summary>
/// Strict, fail-closed dimensional converter mapping Cameo / SysML QUDV units into strongly typed
/// <c>UnitsNet</c> quantities and exporting typed quantities back to Cameo QUDV value properties.
/// Any unknown or dimensionally mismatched unit throws <see cref="ArgumentException"/> rather than silently coercing.
/// </summary>
public static class CameoUnitConverter
{
    internal static readonly Regex QuantityLiteralRegex = new(
        @"^\s*(?<num>[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*(?<unit>[A-Za-z°µΩ/%][A-Za-z0-9°µΩ/%._^-]*)\s*$",
        RegexOptions.Compiled);

    /// <summary>Convert a numeric value and unit symbol to <see cref="ElectricPotential"/>.</summary>
    /// <param name="value">Finite numeric magnitude.</param>
    /// <param name="unit">Voltage unit symbol (<c>V</c>, <c>mV</c>, <c>uV</c>, <c>kV</c>).</param>
    /// <param name="allowEmptyAsSi">When true, an empty unit string defaults to SI volts; otherwise an explicit unit is required.</param>
    /// <returns>Typed <see cref="ElectricPotential"/>.</returns>
    public static ElectricPotential ToElectricPotential(double value, string? unit, bool allowEmptyAsSi = false)
    {
        EnsureFinite(value, nameof(value));
        string u = NormalizeUnit(unit, allowEmptyAsSi, "voltage");
        return u switch
        {
            "" or "v" or "volt" or "volts" => ElectricPotential.FromVolts(value),
            "mv" or "millivolt" or "millivolts" => ElectricPotential.FromMillivolts(value),
            "uv" or "µv" or "microvolt" or "microvolts" => ElectricPotential.FromMicrovolts(value),
            "kv" or "kilovolt" or "kilovolts" => ElectricPotential.FromKilovolts(value),
            _ => throw new ArgumentException($"Unsupported voltage unit '{unit}'. Expected V, mV, uV, or kV.")
        };
    }

    /// <summary>Convert a <see cref="CameoQuantity"/> to <see cref="ElectricPotential"/>.</summary>
    /// <param name="quantity">Cameo quantity with explicit voltage unit.</param>
    /// <returns>Typed <see cref="ElectricPotential"/>.</returns>
    public static ElectricPotential ToElectricPotential(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return ToElectricPotential(quantity.Value, quantity.Unit, allowEmptyAsSi: false);
    }

    /// <summary>Convert a numeric value and unit symbol to <see cref="ElectricCurrent"/>.</summary>
    /// <param name="value">Finite numeric magnitude.</param>
    /// <param name="unit">Current unit symbol (<c>A</c>, <c>mA</c>, <c>uA</c>, <c>nA</c>, <c>kA</c>).</param>
    /// <param name="allowEmptyAsSi">When true, an empty unit string defaults to SI amperes; otherwise an explicit unit is required.</param>
    /// <returns>Typed <see cref="ElectricCurrent"/>.</returns>
    public static ElectricCurrent ToElectricCurrent(double value, string? unit, bool allowEmptyAsSi = false)
    {
        EnsureFinite(value, nameof(value));
        string u = NormalizeUnit(unit, allowEmptyAsSi, "current");
        return u switch
        {
            "" or "a" or "amp" or "amps" or "ampere" or "amperes" => ElectricCurrent.FromAmperes(value),
            "ma" or "milliamp" or "milliamps" or "milliampere" or "milliamperes" => ElectricCurrent.FromMilliamperes(value),
            "ua" or "µa" or "microamp" or "microamps" or "microampere" or "microamperes" => ElectricCurrent.FromMicroamperes(value),
            "na" or "nanoamp" or "nanoamps" or "nanoampere" or "nanoamperes" => ElectricCurrent.FromNanoamperes(value),
            "ka" or "kiloamp" or "kiloamperes" => ElectricCurrent.FromKiloamperes(value),
            _ => throw new ArgumentException($"Unsupported current unit '{unit}'. Expected A, mA, uA, nA, or kA.")
        };
    }

    /// <summary>Convert a <see cref="CameoQuantity"/> to <see cref="ElectricCurrent"/>.</summary>
    /// <param name="quantity">Cameo quantity with explicit current unit.</param>
    /// <returns>Typed <see cref="ElectricCurrent"/>.</returns>
    public static ElectricCurrent ToElectricCurrent(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return ToElectricCurrent(quantity.Value, quantity.Unit, allowEmptyAsSi: false);
    }

    /// <summary>Convert a numeric value and unit symbol to <see cref="Power"/> (including logarithmic RF <c>dBm</c>).</summary>
    /// <param name="value">Finite numeric magnitude.</param>
    /// <param name="unit">Power unit symbol (<c>W</c>, <c>mW</c>, <c>uW</c>, <c>kW</c>, <c>dBm</c>).</param>
    /// <param name="allowEmptyAsSi">When true, an empty unit string defaults to SI watts; otherwise an explicit unit is required.</param>
    /// <returns>Typed <see cref="Power"/>.</returns>
    public static Power ToPower(double value, string? unit, bool allowEmptyAsSi = false)
    {
        EnsureFinite(value, nameof(value));
        string u = NormalizeUnit(unit, allowEmptyAsSi, "power");
        return u switch
        {
            "" or "w" or "watt" or "watts" => Power.FromWatts((decimal)value),
            "mw" or "milliwatt" or "milliwatts" => Power.FromMilliwatts((decimal)value),
            "uw" or "µw" or "microwatt" or "microwatts" => Power.FromMicrowatts((decimal)value),
            "kw" or "kilowatt" or "kilowatts" => Power.FromKilowatts((decimal)value),
            "dbm" => Power.FromWatts((decimal)Math.Pow(10.0, (value - 30.0) / 10.0)),
            _ => throw new ArgumentException($"Unsupported power unit '{unit}'. Expected W, mW, uW, kW, or dBm.")
        };
    }

    /// <summary>Convert a <see cref="CameoQuantity"/> to <see cref="Power"/>.</summary>
    /// <param name="quantity">Cameo quantity with explicit power unit.</param>
    /// <returns>Typed <see cref="Power"/>.</returns>
    public static Power ToPower(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return ToPower(quantity.Value, quantity.Unit, allowEmptyAsSi: false);
    }

    /// <summary>Convert a numeric value and unit symbol to <see cref="Frequency"/>.</summary>
    /// <param name="value">Finite numeric magnitude.</param>
    /// <param name="unit">Frequency unit symbol (<c>Hz</c>, <c>kHz</c>, <c>MHz</c>, <c>GHz</c>).</param>
    /// <param name="allowEmptyAsSi">When true, an empty unit string defaults to SI hertz; otherwise an explicit unit is required.</param>
    /// <returns>Typed <see cref="Frequency"/>.</returns>
    public static Frequency ToFrequency(double value, string? unit, bool allowEmptyAsSi = false)
    {
        EnsureFinite(value, nameof(value));
        string u = NormalizeUnit(unit, allowEmptyAsSi, "frequency");
        return u switch
        {
            "" or "hz" or "hertz" => Frequency.FromHertz(value),
            "khz" or "kilohertz" => Frequency.FromKilohertz(value),
            "mhz" or "megahertz" => Frequency.FromMegahertz(value),
            "ghz" or "gigahertz" => Frequency.FromGigahertz(value),
            _ => throw new ArgumentException($"Unsupported frequency unit '{unit}'. Expected Hz, kHz, MHz, or GHz.")
        };
    }

    /// <summary>Convert a <see cref="CameoQuantity"/> to <see cref="Frequency"/>.</summary>
    /// <param name="quantity">Cameo quantity with explicit frequency unit.</param>
    /// <returns>Typed <see cref="Frequency"/>.</returns>
    public static Frequency ToFrequency(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return ToFrequency(quantity.Value, quantity.Unit, allowEmptyAsSi: false);
    }

    /// <summary>Convert a numeric value and unit symbol to <see cref="Temperature"/>.</summary>
    /// <param name="value">Finite numeric magnitude.</param>
    /// <param name="unit">Temperature unit symbol (<c>degC</c>, <c>C</c>, <c>°C</c>, <c>K</c>, <c>degF</c>, <c>°F</c>).</param>
    /// <param name="allowEmptyAsSi">When true, an empty unit string defaults to degrees Celsius; otherwise an explicit unit is required.</param>
    /// <returns>Typed <see cref="Temperature"/>.</returns>
    public static Temperature ToTemperature(double value, string? unit, bool allowEmptyAsSi = false)
    {
        EnsureFinite(value, nameof(value));
        string u = NormalizeUnit(unit, allowEmptyAsSi, "temperature");
        return u switch
        {
            "" or "c" or "degc" or "°c" or "celsius" => Temperature.FromDegreesCelsius(value),
            "k" or "kelvin" or "kelvins" => Temperature.FromKelvins(value),
            "f" or "degf" or "°f" or "fahrenheit" => Temperature.FromDegreesFahrenheit(value),
            _ => throw new ArgumentException($"Unsupported temperature unit '{unit}'. Expected degC, K, or degF.")
        };
    }

    /// <summary>Convert a <see cref="CameoQuantity"/> to <see cref="Temperature"/>.</summary>
    /// <param name="quantity">Cameo quantity with explicit temperature unit.</param>
    /// <returns>Typed <see cref="Temperature"/>.</returns>
    public static Temperature ToTemperature(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return ToTemperature(quantity.Value, quantity.Unit, allowEmptyAsSi: false);
    }

    /// <summary>Convert a numeric value and unit symbol to <see cref="ElectricResistance"/> (case-aware for <c>mOhm</c> vs <c>MOhm</c>).</summary>
    /// <param name="value">Finite numeric magnitude.</param>
    /// <param name="unit">Resistance unit symbol (<c>Ohm</c>, <c>mOhm</c>, <c>uOhm</c>, <c>kOhm</c>, <c>MOhm</c>).</param>
    /// <param name="allowEmptyAsSi">When true, an empty unit string defaults to SI ohms; otherwise an explicit unit is required.</param>
    /// <returns>Typed <see cref="ElectricResistance"/>.</returns>
    public static ElectricResistance ToElectricResistance(double value, string? unit, bool allowEmptyAsSi = false)
    {
        EnsureFinite(value, nameof(value));
        string trimmed = (unit ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (allowEmptyAsSi) return ElectricResistance.FromOhms(value);
            throw new ArgumentException("Missing explicit resistance unit. Expected Ohm, mOhm, uOhm, kOhm, or MOhm.");
        }

        // Preserve case distinction between milliohm ('mOhm'/'mΩ') and megaohm ('MOhm'/'MΩ').
        if (trimmed is "MOhm" or "MΩ" or "Mohm")
            return ElectricResistance.FromMegaohms(value);
        if (trimmed is "mOhm" or "mΩ" or "mohm")
            return ElectricResistance.FromMilliohms(value);

        return trimmed.ToLowerInvariant() switch
        {
            "ohm" or "ohms" or "ω" => ElectricResistance.FromOhms(value),
            "milliohm" or "milliohms" => ElectricResistance.FromMilliohms(value),
            "uohm" or "µohm" or "uω" or "µω" or "microohm" or "microohms" => ElectricResistance.FromMicroohms(value),
            "kohm" or "kω" or "kiloohm" or "kiloohms" => ElectricResistance.FromKiloohms(value),
            "megaohm" or "megaohms" => ElectricResistance.FromMegaohms(value),
            _ => throw new ArgumentException($"Unsupported resistance unit '{unit}'. Expected Ohm, mOhm, uOhm, kOhm, or MOhm.")
        };
    }

    /// <summary>Convert a <see cref="CameoQuantity"/> to <see cref="ElectricResistance"/>.</summary>
    /// <param name="quantity">Cameo quantity with explicit resistance unit.</param>
    /// <returns>Typed <see cref="ElectricResistance"/>.</returns>
    public static ElectricResistance ToElectricResistance(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return ToElectricResistance(quantity.Value, quantity.Unit, allowEmptyAsSi: false);
    }

    /// <summary>Convert a numeric value and unit symbol to <see cref="ElectricCapacitance"/>.</summary>
    /// <param name="value">Finite numeric magnitude.</param>
    /// <param name="unit">Capacitance unit symbol (<c>F</c>, <c>mF</c>, <c>uF</c>, <c>nF</c>, <c>pF</c>).</param>
    /// <param name="allowEmptyAsSi">When true, an empty unit string defaults to SI farads; otherwise an explicit unit is required.</param>
    /// <returns>Typed <see cref="ElectricCapacitance"/>.</returns>
    public static ElectricCapacitance ToElectricCapacitance(double value, string? unit, bool allowEmptyAsSi = false)
    {
        EnsureFinite(value, nameof(value));
        string u = NormalizeUnit(unit, allowEmptyAsSi, "capacitance");
        return u switch
        {
            "" or "f" or "farad" or "farads" => ElectricCapacitance.FromFarads(value),
            "mf" or "millifarad" or "millifarads" => ElectricCapacitance.FromMillifarads(value),
            "uf" or "µf" or "microfarad" or "microfarads" => ElectricCapacitance.FromMicrofarads(value),
            "nf" or "nanofarad" or "nanofarads" => ElectricCapacitance.FromNanofarads(value),
            "pf" or "picofarad" or "picofarads" => ElectricCapacitance.FromPicofarads(value),
            _ => throw new ArgumentException($"Unsupported capacitance unit '{unit}'. Expected F, mF, uF, nF, or pF.")
        };
    }

    /// <summary>Convert a <see cref="CameoQuantity"/> to <see cref="ElectricCapacitance"/>.</summary>
    /// <param name="quantity">Cameo quantity with explicit capacitance unit.</param>
    /// <returns>Typed <see cref="ElectricCapacitance"/>.</returns>
    public static ElectricCapacitance ToElectricCapacitance(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return ToElectricCapacitance(quantity.Value, quantity.Unit, allowEmptyAsSi: false);
    }

    /// <summary>Convert a numeric value and unit symbol to a non-negative <see cref="TimeSpan"/> duration.</summary>
    /// <param name="value">Finite non-negative numeric magnitude.</param>
    /// <param name="unit">Duration unit symbol (<c>s</c>, <c>ms</c>, <c>us</c>, <c>min</c>, <c>h</c>).</param>
    /// <param name="allowEmptyAsSi">When true, an empty unit string defaults to SI seconds; otherwise an explicit unit is required.</param>
    /// <returns>Typed <see cref="TimeSpan"/>.</returns>
    public static TimeSpan ToDuration(double value, string? unit, bool allowEmptyAsSi = false)
    {
        EnsureFinite(value, nameof(value));
        if (value < 0)
            throw new ArgumentException("Duration value must be non-negative.");
        string u = NormalizeUnit(unit, allowEmptyAsSi, "duration");
        return u switch
        {
            "" or "s" or "sec" or "second" or "seconds" => TimeSpan.FromSeconds(value),
            "ms" or "millisecond" or "milliseconds" => TimeSpan.FromMilliseconds(value),
            "us" or "µs" or "microsecond" or "microseconds" => TimeSpan.FromMicroseconds(value),
            "min" or "minute" or "minutes" => TimeSpan.FromMinutes(value),
            "h" or "hr" or "hour" or "hours" => TimeSpan.FromHours(value),
            _ => throw new ArgumentException($"Unsupported duration unit '{unit}'. Expected s, ms, us, min, or h.")
        };
    }

    /// <summary>Convert a <see cref="CameoQuantity"/> to a <see cref="TimeSpan"/> duration.</summary>
    /// <param name="quantity">Cameo quantity with explicit duration unit.</param>
    /// <returns>Typed <see cref="TimeSpan"/>.</returns>
    public static TimeSpan ToDuration(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return ToDuration(quantity.Value, quantity.Unit, allowEmptyAsSi: false);
    }

    /// <summary>Convert a Cameo thermal resistance quantity (<c>degC/W</c>, <c>K/W</c>, <c>mK/W</c>) into degrees Celsius per watt.</summary>
    /// <param name="quantity">Cameo quantity with thermal resistance unit.</param>
    /// <returns>Thermal resistance in degrees Celsius per watt.</returns>
    public static double ToCelsiusPerWatt(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        EnsureFinite(quantity.Value, nameof(quantity));
        string u = NormalizeUnit(quantity.Unit, allowEmptyAsSi: false, "thermal resistance");
        return u switch
        {
            "degc/w" or "c/w" or "°c/w" or "k/w" => quantity.Value,
            "mk/w" or "mdegc/w" => quantity.Value * 1e-3,
            _ => throw new ArgumentException($"Unsupported thermal resistance unit '{quantity.Unit}'. Expected degC/W, K/W, or mK/W.")
        };
    }

    /// <summary>Convert a Cameo temperature coefficient (<c>1/degC</c>, <c>1/K</c>, <c>ppm/degC</c>, <c>%/degC</c>) into fractional change per degree Celsius.</summary>
    /// <param name="quantity">Cameo quantity with temperature-coefficient unit.</param>
    /// <returns>Fractional temperature coefficient per degree Celsius.</returns>
    public static double ToPerCelsius(CameoQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        EnsureFinite(quantity.Value, nameof(quantity));
        string u = NormalizeUnit(quantity.Unit, allowEmptyAsSi: false, "temperature coefficient");
        return u switch
        {
            "1/degc" or "1/c" or "1/°c" or "1/k" or "/degc" or "/c" or "/k" => quantity.Value,
            "ppm/degc" or "ppm/c" or "ppm/°c" or "ppm/k" => quantity.Value * 1e-6,
            "%/degc" or "%/c" or "%/°c" or "%/k" => quantity.Value * 1e-2,
            _ => throw new ArgumentException($"Unsupported temperature coefficient unit '{quantity.Unit}'. Expected 1/degC, ppm/degC, or %/degC.")
        };
    }

    /// <summary>Create a SysML QUDV <see cref="CameoQuantity"/> from an <see cref="ElectricPotential"/>.</summary>
    /// <param name="voltage">Typed voltage quantity.</param>
    /// <returns>Cameo quantity with explicit unit and QUDV metadata.</returns>
    public static CameoQuantity FromElectricPotential(ElectricPotential voltage) =>
        Math.Abs(voltage.Volts) > 0 && Math.Abs(voltage.Volts) < 1.0
            ? new CameoQuantity(voltage.Millivolts, "mV", "ElectricPotential", "Millivolts")
            : new CameoQuantity(voltage.Volts, "V", "ElectricPotential", "Volts");

    /// <summary>Create a SysML QUDV <see cref="CameoQuantity"/> from an <see cref="ElectricCurrent"/>.</summary>
    /// <param name="current">Typed current quantity.</param>
    /// <returns>Cameo quantity with explicit unit and QUDV metadata.</returns>
    public static CameoQuantity FromElectricCurrent(ElectricCurrent current) =>
        Math.Abs(current.Amperes) > 0 && Math.Abs(current.Amperes) < 1.0
            ? new CameoQuantity(current.Milliamperes, "mA", "ElectricCurrent", "Milliamperes")
            : new CameoQuantity(current.Amperes, "A", "ElectricCurrent", "Amperes");

    /// <summary>Create a SysML QUDV <see cref="CameoQuantity"/> from an <see cref="ElectricResistance"/>.</summary>
    /// <param name="resistance">Typed resistance quantity.</param>
    /// <returns>Cameo quantity with explicit unit and QUDV metadata.</returns>
    public static CameoQuantity FromElectricResistance(ElectricResistance resistance) =>
        Math.Abs(resistance.Ohms) > 0 && Math.Abs(resistance.Ohms) < 1.0
            ? new CameoQuantity(resistance.Milliohms, "mOhm", "ElectricResistance", "Milliohms")
            : new CameoQuantity(resistance.Ohms, "Ohm", "ElectricResistance", "Ohms");

    /// <summary>Create a SysML QUDV <see cref="CameoQuantity"/> from a <see cref="Power"/>.</summary>
    /// <param name="power">Typed power quantity.</param>
    /// <returns>Cameo quantity with explicit unit and QUDV metadata.</returns>
    public static CameoQuantity FromPower(Power power) =>
        Math.Abs((double)power.Watts) > 0 && Math.Abs((double)power.Watts) < 1.0
            ? new CameoQuantity((double)power.Milliwatts, "mW", "Power", "Milliwatts")
            : new CameoQuantity((double)power.Watts, "W", "Power", "Watts");

    /// <summary>Create a SysML QUDV <see cref="CameoQuantity"/> from a <see cref="Frequency"/>.</summary>
    /// <param name="frequency">Typed frequency quantity.</param>
    /// <returns>Cameo quantity with explicit unit and QUDV metadata.</returns>
    public static CameoQuantity FromFrequency(Frequency frequency)
    {
        double hz = Math.Abs(frequency.Hertz);
        if (hz >= 1e9) return new CameoQuantity(frequency.Gigahertz, "GHz", "Frequency", "Gigahertz");
        if (hz >= 1e6) return new CameoQuantity(frequency.Megahertz, "MHz", "Frequency", "Megahertz");
        if (hz >= 1e3) return new CameoQuantity(frequency.Kilohertz, "kHz", "Frequency", "Kilohertz");
        return new CameoQuantity(frequency.Hertz, "Hz", "Frequency", "Hertz");
    }

    /// <summary>Create a SysML QUDV <see cref="CameoQuantity"/> from a <see cref="Temperature"/>.</summary>
    /// <param name="temperature">Typed temperature quantity.</param>
    /// <returns>Cameo quantity with explicit unit and QUDV metadata.</returns>
    public static CameoQuantity FromTemperature(Temperature temperature) =>
        new(temperature.DegreesCelsius, "degC", "ThermodynamicTemperature", "DegreesCelsius");

    /// <summary>Create a SysML QUDV <see cref="CameoQuantity"/> from an <see cref="ElectricCapacitance"/>.</summary>
    /// <param name="capacitance">Typed capacitance quantity.</param>
    /// <returns>Cameo quantity with explicit unit and QUDV metadata.</returns>
    public static CameoQuantity FromElectricCapacitance(ElectricCapacitance capacitance)
    {
        double f = Math.Abs(capacitance.Farads);
        if (f > 0 && f < 1e-9) return new CameoQuantity(capacitance.Picofarads, "pF", "ElectricCapacitance", "Picofarads");
        if (f > 0 && f < 1e-6) return new CameoQuantity(capacitance.Nanofarads, "nF", "ElectricCapacitance", "Nanofarads");
        if (f > 0 && f < 1e-3) return new CameoQuantity(capacitance.Microfarads, "uF", "ElectricCapacitance", "Microfarads");
        return new CameoQuantity(capacitance.Farads, "F", "ElectricCapacitance", "Farads");
    }

    /// <summary>Create a SysML QUDV <see cref="CameoQuantity"/> from a <see cref="TimeSpan"/> duration.</summary>
    /// <param name="duration">Typed duration.</param>
    /// <returns>Cameo quantity with explicit unit and QUDV metadata.</returns>
    public static CameoQuantity FromDuration(TimeSpan duration) =>
        duration.TotalSeconds > 0 && duration.TotalSeconds < 1.0
            ? new CameoQuantity(duration.TotalMilliseconds, "ms", "Time", "Milliseconds")
            : new CameoQuantity(duration.TotalSeconds, "s", "Time", "Seconds");

    private static string NormalizeUnit(string? unit, bool allowEmptyAsSi, string quantityLabel)
    {
        string trimmed = (unit ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (allowEmptyAsSi) return string.Empty;
            throw new ArgumentException($"Missing explicit {quantityLabel} unit.");
        }
        return trimmed.ToLowerInvariant();
    }

    private static void EnsureFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Quantity value must be finite.", paramName);
    }
}
