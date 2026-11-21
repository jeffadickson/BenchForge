using Starlark.Eval;
using Starlark.Syntax;
using UnitsNet;

namespace BenchForge.Procedures;

/// <summary>One-based source position of a Starlark procedure statement or diagnostic.</summary>
/// <param name="Line">One-based line number in the procedure source.</param>
/// <param name="Column">One-based column number in the procedure source.</param>
public readonly record struct ProcedureLocation(int Line, int Column = 1);

/// <summary>Physical quantity supported by the mock-only Starlark procedure runtime.</summary>
public enum MeasurementKind
{
    /// <summary>Electric potential measured in volts (V).</summary>
    Voltage,

    /// <summary>Electric resistance measured in ohms (Ohm).</summary>
    Resistance
}

/// <summary>Uncatchable execution budgets enforced on every Starlark procedure run.</summary>
/// <param name="MaxSteps">Maximum interpreter statements and expressions allowed per run.</param>
/// <param name="MaxAllocatedBytes">Maximum single-object allocation ceiling in bytes.</param>
/// <param name="Timeout">Maximum wall-clock execution duration for one procedure run.</param>
public sealed record StarlarkProcedureLimits(
    long MaxSteps = 100_000,
    long MaxAllocatedBytes = 1024 * 1024,
    TimeSpan? Timeout = null)
{
    /// <summary>Validate that all configured execution limits are strictly positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Any limit is non-positive or non-finite.</exception>
    public void Validate()
    {
        if (MaxSteps <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxSteps), "MaxSteps must be positive.");
        if (MaxAllocatedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxAllocatedBytes), "MaxAllocatedBytes must be positive.");
        if (Timeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive.");
    }
}

/// <summary>Immutable unit-tagged scalar measurement exposed as a first-class Starlark value.</summary>
public sealed class ProcedureQuantity : IThreadSafeHostValue
{
    /// <summary>Create an immutable quantity backed by a canonical UnitsNet value.</summary>
    /// <param name="kind">Physical quantity kind.</param>
    /// <param name="value">Finite canonical numeric value (V for Voltage, Ohm for Resistance).</param>
    /// <param name="unit">Canonical display unit symbol ("V" or "Ohm").</param>
    /// <param name="quantity">Underlying typed UnitsNet quantity.</param>
    /// <exception cref="ArgumentOutOfRangeException">Value is not finite.</exception>
    /// <exception cref="ArgumentNullException">Unit or quantity is null.</exception>
    public ProcedureQuantity(MeasurementKind kind, double value, string unit, IQuantity quantity)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Measurement value must be finite.");
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentNullException.ThrowIfNull(quantity);
        Kind = kind;
        Value = value;
        Unit = unit;
        Quantity = quantity;
    }

    /// <summary>Numeric value in canonical SI units (V or Ohm).</summary>
    public double Value { get; }

    /// <summary>Canonical unit symbol ("V" or "Ohm").</summary>
    public string Unit { get; }

    /// <summary>Measurement kind name ("VOLTAGE" or "RESISTANCE").</summary>
    public string KindName => Kind == MeasurementKind.Resistance ? "RESISTANCE" : "VOLTAGE";

    /// <summary>Strongly typed measurement kind for host verification.</summary>
    public MeasurementKind Kind { get; }

    /// <summary>Underlying UnitsNet quantity preserving dimensional metadata.</summary>
    public IQuantity Quantity { get; }

    /// <summary>Starlark type name returned by <c>type()</c>.</summary>
    public string TypeName => "quantity";

    /// <summary>Human-readable representation returned by <c>str()</c> and <c>repr()</c>.</summary>
    /// <returns>Formatted value and unit symbol.</returns>
    public string Repr() => $"{Value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)} {Unit}";

    /// <summary>Resolve script-visible attributes (<c>value</c>, <c>unit</c>, <c>kind</c>).</summary>
    /// <param name="name">Attribute name requested by Starlark code.</param>
    /// <param name="value">Resolved attribute value when found.</param>
    /// <returns>True when the attribute exists; false otherwise.</returns>
    public bool TryGetAttribute(string name, out object? value)
    {
        switch (name)
        {
            case "value":
                value = Value;
                return true;
            case "unit":
                value = Unit;
                return true;
            case "kind":
                value = KindName;
                return true;
            default:
                value = null;
                return false;
        }
    }

    /// <summary>List script-visible attribute names for <c>dir()</c>.</summary>
    /// <returns>Exposed attribute names.</returns>
    public IEnumerable<string> Attributes() => new[] { "value", "unit", "kind" };

    /// <summary>Compare equality by matching measurement kind and canonical value.</summary>
    /// <param name="other">Other Starlark value.</param>
    /// <returns>True when both are quantities of the same kind and equal value.</returns>
    public bool EqualsValue(object? other) =>
        other is ProcedureQuantity q && q.Kind == Kind && q.Value.Equals(Value);

    /// <summary>Compute a hash code consistent with <see cref="EqualsValue"/>.</summary>
    /// <returns>Deterministic hash code.</returns>
    public int HashValue() => HashCode.Combine(Kind, Value);

    /// <summary>Order two quantities only when their physical dimensions match.</summary>
    /// <param name="other">Other Starlark value.</param>
    /// <returns>Comparison result when dimensions match; null otherwise.</returns>
    public int? CompareTo(object? other) =>
        other is ProcedureQuantity q && q.Kind == Kind ? Value.CompareTo(q.Value) : null;
}

/// <summary>Static instrument role declaration discovered during Starlark AST preflight.</summary>
/// <param name="Role">Experiment instrument role identifier.</param>
/// <param name="Location">Source location of the declaration call.</param>
public sealed record ProcedureInstrumentDeclaration(string Role, ProcedureLocation Location);

/// <summary>Static configure call discovered during Starlark AST preflight.</summary>
/// <param name="Role">Experiment instrument role identifier.</param>
/// <param name="Kind">Requested measurement kind.</param>
/// <param name="Location">Source location of the configure call.</param>
public sealed record ProcedureConfigureReference(string Role, MeasurementKind Kind, ProcedureLocation Location);

/// <summary>Static measure call discovered during Starlark AST preflight.</summary>
/// <param name="Role">Experiment instrument role identifier.</param>
/// <param name="Kind">Requested measurement kind.</param>
/// <param name="Location">Source location of the measure call.</param>
public sealed record ProcedureMeasureReference(string Role, MeasurementKind Kind, ProcedureLocation Location);

/// <summary>Parsed immutable Starlark procedure module with static preflight metadata.</summary>
/// <param name="FileName">Logical procedure filename used for diagnostics.</param>
/// <param name="Source">Original Starlark source text.</param>
/// <param name="SyntaxTree">Parsed immutable Starlark syntax tree.</param>
/// <param name="DeclaredInstruments">Statically discovered instrument role declarations.</param>
/// <param name="StaticConfigures">Statically discovered literal configure calls.</param>
/// <param name="StaticMeasures">Statically discovered literal measure calls.</param>
public sealed record ProcedureDocument(
    string FileName,
    string Source,
    AstModule SyntaxTree,
    IReadOnlyList<ProcedureInstrumentDeclaration> DeclaredInstruments,
    IReadOnlyList<ProcedureConfigureReference> StaticConfigures,
    IReadOnlyList<ProcedureMeasureReference> StaticMeasures);

/// <summary>A syntax, static-preflight, or validation diagnostic tied to a source position.</summary>
/// <param name="Location">One-based line and column of the diagnostic.</param>
/// <param name="Message">Human-readable error description.</param>
public sealed record ProcedureDiagnostic(ProcedureLocation Location, string Message);

/// <summary>Result of parsing a Starlark procedure; invalid documents cannot execute.</summary>
/// <param name="Document">Parsed immutable document when syntax parsing succeeded, or null on syntax failure.</param>
/// <param name="Diagnostics">All syntax or static preflight diagnostics found during parsing.</param>
public sealed record ProcedureParseResult(
    ProcedureDocument? Document,
    IReadOnlyList<ProcedureDiagnostic> Diagnostics)
{
    /// <summary>True when no diagnostics were recorded and a syntax tree was produced.</summary>
    public bool Success => Diagnostics.Count == 0 && Document is not null;
}
