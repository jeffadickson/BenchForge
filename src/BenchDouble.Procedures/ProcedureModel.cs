namespace BenchDouble.Procedures;

/// <summary>Source position of a DSL statement for editor diagnostics.</summary>
public readonly record struct ProcedureLocation(int Line, int Column = 1);

/// <summary>Physical quantity supported by the initial mock-only DSL.</summary>
public enum MeasurementKind { Voltage, Resistance }

/// <summary>Stable AST node contract shared by parser, validator and runner.</summary>
public abstract record ProcedureStep(ProcedureLocation Location);

/// <summary>Declare a named instrument role from an experiment definition.</summary>
public sealed record InstrumentStep(string Role, ProcedureLocation Location)
    : ProcedureStep(Location);

/// <summary>Configure a typed DMM measurement mode.</summary>
public sealed record ConfigureStep(string Role, MeasurementKind Kind, ProcedureLocation Location)
    : ProcedureStep(Location);

/// <summary>Read a quantity from a mock instrument into a named variable.</summary>
public sealed record MeasureStep(string Role, MeasurementKind Kind, string Variable,
    ProcedureLocation Location) : ProcedureStep(Location);

/// <summary>Advance the experiment's simulated clock; never sleeps in wall time.</summary>
public sealed record WaitStep(TimeSpan Duration, ProcedureLocation Location)
    : ProcedureStep(Location);

/// <summary>Assert inclusive lower/upper bounds of the same physical dimension.</summary>
public sealed record VerifyStep(string Variable, MeasurementKind Kind, double Minimum,
    double Maximum, ProcedureLocation Location) : ProcedureStep(Location);

/// <summary>Parsed immutable step sequence. No executable code or hardware commands.</summary>
public sealed record ProcedureDocument(IReadOnlyList<ProcedureStep> Steps);

/// <summary>A parse or validation error tied to the source line.</summary>
public sealed record ProcedureDiagnostic(ProcedureLocation Location, string Message);

/// <summary>Returns a partial AST plus diagnostics; invalid documents are not runnable.</summary>
public sealed record ProcedureParseResult(ProcedureDocument Document,
    IReadOnlyList<ProcedureDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.Count == 0;
}
