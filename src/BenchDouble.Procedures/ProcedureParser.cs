using Parlot.Fluent;
using static Parlot.Fluent.Parsers;

namespace BenchDouble.Procedures;

/// <summary>
/// Small, deliberately non-Turing-complete procedure language implemented with
/// Parlot combinators. Produces typed AST nodes, never executes during parsing.
/// </summary>
public static class ProcedureParser
{
    private const int MaxDocumentChars = 128 * 1024;
    private const int MaxSteps = 2048;

    /// <summary>
    /// Parse a line-oriented v1 DSL. Unknown syntax produces a line-specific
    /// diagnostic instead of silently skipping a step.
    /// </summary>
    public static ProcedureParseResult Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MaxDocumentChars)
            throw new ArgumentException("Procedure exceeds maximum source length.", nameof(source));

        var steps = new List<ProcedureStep>();
        var errors = new List<ProcedureDiagnostic>();
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#') ||
                line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var location = new ProcedureLocation(i + 1);
            if (steps.Count >= MaxSteps)
            {
                errors.Add(new(location, "Procedure exceeds maximum step count."));
                break;
            }

            if (TryStatement(line, location, out var step, out var message))
                steps.Add(step!);
            else
                errors.Add(new(location, message ?? "Invalid procedure statement."));
        }

        return new(new ProcedureDocument(steps.AsReadOnly()), errors.AsReadOnly());
    }

    private static bool TryStatement(string text, ProcedureLocation location,
        out ProcedureStep? step, out string? error)
    {
        step = null;
        error = null;

        // INSTRUMENT Meter1
        var instrument = Terms.Keyword("INSTRUMENT", caseInsensitive: true)
            .SkipAnd(Terms.Identifier()).Eof();
        if (instrument.TryParse(text, out var role))
        {
            step = new InstrumentStep(role.ToString(), location);
            return true;
        }

        // CONFIGURE Meter1 RESISTANCE
        var configure = Terms.Keyword("CONFIGURE", caseInsensitive: true)
            .SkipAnd(Terms.Identifier()).And(Terms.Identifier()).Eof();
        if (configure.TryParse(text, out var config))
        {
            if (!TryKind(config.Item2.ToString(), out var kind))
                error = "Unsupported measurement kind. Use VOLTAGE or RESISTANCE.";
            else
            {
                step = new ConfigureStep(config.Item1.ToString(), kind, location);
                return true;
            }
            return false;
        }

        // MEASURE Meter1 RESISTANCE AS R1
        var measure = Terms.Keyword("MEASURE", caseInsensitive: true)
            .SkipAnd(Terms.Identifier()).And(Terms.Identifier())
            .AndSkip(Terms.Keyword("AS", caseInsensitive: true))
            .And(Terms.Identifier()).Eof();
        if (measure.TryParse(text, out var reading))
        {
            if (!TryKind(reading.Item2.ToString(), out var kind))
                error = "Unsupported measurement kind. Use VOLTAGE or RESISTANCE.";
            else
            {
                step = new MeasureStep(reading.Item1.ToString(), kind,
                    reading.Item3.ToString(), location);
                return true;
            }
            return false;
        }

        // WAIT 5 s (simulated time, not wall-clock time)
        var wait = Terms.Keyword("WAIT", caseInsensitive: true)
            .SkipAnd(Terms.Decimal()).And(Terms.Identifier()).Eof();
        if (wait.TryParse(text, out var delay))
        {
            if (!delay.Item2.ToString().Equals("s", StringComparison.OrdinalIgnoreCase) ||
                delay.Item1 < 0 || delay.Item1 > 86400)
                error = "WAIT expects a duration between 0 and 86400 s.";
            else
            {
                step = new WaitStep(TimeSpan.FromSeconds((double)delay.Item1), location);
                return true;
            }
            return false;
        }

        // VERIFY R1 BETWEEN 95 Ohm AND 105 Ohm
        var verify = Terms.Keyword("VERIFY", caseInsensitive: true)
            .SkipAnd(Terms.Identifier())
            .AndSkip(Terms.Keyword("BETWEEN", caseInsensitive: true))
            .And(Terms.Decimal()).And(Terms.Identifier())
            .AndSkip(Terms.Keyword("AND", caseInsensitive: true))
            .And(Terms.Decimal()).And(Terms.Identifier()).Eof();
        if (verify.TryParse(text, out var limit))
        {
            string lowerUnit = limit.Item3.ToString();
            string upperUnit = limit.Item5.ToString();
            if (!TryUnit(lowerUnit, out var kind) ||
                !TryUnit(upperUnit, out var upperKind) || kind != upperKind)
                error = "VERIFY requires matching physical units (Ohm or V).";
            else if (limit.Item2 > limit.Item4)
                error = "VERIFY lower limit must not exceed upper limit.";
            else
            {
                step = new VerifyStep(limit.Item1.ToString(), kind,
                    (double)limit.Item2, (double)limit.Item4, location);
                return true;
            }
            return false;
        }

        error = "Unknown or malformed statement. Expected INSTRUMENT, CONFIGURE, " +
            "MEASURE, WAIT, or VERIFY.";
        return false;
    }

    private static bool TryKind(string value, out MeasurementKind kind)
    {
        if (value.Equals("VOLTAGE", StringComparison.OrdinalIgnoreCase))
        {
            kind = MeasurementKind.Voltage;
            return true;
        }
        if (value.Equals("RESISTANCE", StringComparison.OrdinalIgnoreCase))
        {
            kind = MeasurementKind.Resistance;
            return true;
        }
        kind = default;
        return false;
    }

    private static bool TryUnit(string value, out MeasurementKind kind)
    {
        if (value.Equals("V", StringComparison.OrdinalIgnoreCase))
        {
            kind = MeasurementKind.Voltage;
            return true;
        }
        if (value.Equals("Ohm", StringComparison.OrdinalIgnoreCase))
        {
            kind = MeasurementKind.Resistance;
            return true;
        }
        kind = default;
        return false;
    }
}
