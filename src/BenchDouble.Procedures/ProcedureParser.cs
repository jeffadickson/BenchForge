using Starlark.Syntax;

namespace BenchDouble.Procedures;

/// <summary>
/// Hermetic Starlark procedure parser powered by <c>starlark-cs</c>.
/// Produces an immutable <see cref="AstModule"/> and static preflight metadata;
/// parsing never executes code, touches instruments, or reads files.
/// </summary>
public static class ProcedureParser
{
    private const int MaxDocumentChars = 128 * 1024;
    private const double MaxWaitSeconds = 86_400;

    /// <summary>
    /// Restricted deterministic Starlark dialect for procedures: permits top-level statements,
    /// loops, functions, and f-strings while disabling <c>load()</c> and recursion.
    /// </summary>
    public static Dialect ProcedureDialect { get; } = Dialect.Standard with
    {
        EnableTopLevelStmt = true,
        EnableLoad = false,
        EnableRecursion = false,
        EnableFStrings = true
    };

    /// <summary>
    /// Parse a Starlark procedure source string into an immutable syntax tree and static diagnostics.
    /// </summary>
    /// <param name="source">Starlark procedure text (at most 128 KiB).</param>
    /// <param name="fileName">Logical source name reported in diagnostics.</param>
    /// <returns>Parse result containing the syntax tree and any syntax or static-preflight diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> exceeds the 128 KiB safety ceiling or <paramref name="fileName"/> is blank.</exception>
    public static ProcedureParseResult Parse(string source, string fileName = "procedure.star")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (source.Length > MaxDocumentChars)
            throw new ArgumentException("Procedure exceeds maximum source length.", nameof(source));

        AstModule ast;
        try
        {
            ast = AstModule.Parse(fileName, source, ProcedureDialect);
        }
        catch (StarlarkSyntaxException ex)
        {
            var location = ResolveLocation(ex.Location.File, ex.Location.Span);
            return new ProcedureParseResult(
                null,
                new[] { new ProcedureDiagnostic(location, ex.RawMessage) });
        }

        var diagnostics = new List<ProcedureDiagnostic>();
        var instruments = new List<ProcedureInstrumentDeclaration>();
        var configures = new List<ProcedureConfigureReference>();
        var measures = new List<ProcedureMeasureReference>();

        WalkStatement(ast.Statement, ast.CodeMap, instruments, configures, measures, diagnostics);

        var document = new ProcedureDocument(
            fileName,
            source,
            ast,
            instruments.AsReadOnly(),
            configures.AsReadOnly(),
            measures.AsReadOnly());

        return new ProcedureParseResult(document, diagnostics.AsReadOnly());
    }

    internal static ProcedureLocation ResolveLocation(CodeMap codeMap, Span span)
    {
        var resolved = codeMap.ResolveSpan(span);
        return new ProcedureLocation(resolved.Begin.Line + 1, resolved.Begin.Column + 1);
    }

    internal static bool TryParseKind(string value, out MeasurementKind kind)
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

    internal static bool TryParseUnit(string value, out MeasurementKind kind)
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

    private static void WalkStatement(
        Stmt stmt,
        CodeMap codeMap,
        List<ProcedureInstrumentDeclaration> instruments,
        List<ProcedureConfigureReference> configures,
        List<ProcedureMeasureReference> measures,
        List<ProcedureDiagnostic> diagnostics)
    {
        stmt.VisitOwnExprs(expr => WalkExpression(expr, codeMap, instruments, configures, measures, diagnostics));
        stmt.VisitStmt(child => WalkStatement(child, codeMap, instruments, configures, measures, diagnostics));
    }

    private static void WalkExpression(
        Expr expr,
        CodeMap codeMap,
        List<ProcedureInstrumentDeclaration> instruments,
        List<ProcedureConfigureReference> configures,
        List<ProcedureMeasureReference> measures,
        List<ProcedureDiagnostic> diagnostics)
    {
        if (expr is CallExpr call && call.Function is IdentExpr ident)
        {
            var location = ResolveLocation(codeMap, call.Span);
            InspectHostCall(ident.Name, call.Args, location, instruments, configures, measures, diagnostics);
        }
        expr.VisitExpr(child => WalkExpression(child, codeMap, instruments, configures, measures, diagnostics));
    }

    private static void InspectHostCall(
        string name,
        IReadOnlyList<Argument> args,
        ProcedureLocation location,
        List<ProcedureInstrumentDeclaration> instruments,
        List<ProcedureConfigureReference> configures,
        List<ProcedureMeasureReference> measures,
        List<ProcedureDiagnostic> diagnostics)
    {
        switch (name)
        {
            case "instrument":
                if (args.Count == 1 && TryStringArg(args[0], out var role))
                    instruments.Add(new ProcedureInstrumentDeclaration(role, location));
                else
                    diagnostics.Add(new ProcedureDiagnostic(location, "instrument(role) requires one string literal role name."));
                break;

            case "configure":
                if (args.Count >= 2 && TryStringArg(args[0], out var cfgRole) && TryStringArg(args[1], out var cfgKindText))
                {
                    if (TryParseKind(cfgKindText, out var cfgKind))
                        configures.Add(new ProcedureConfigureReference(cfgRole, cfgKind, location));
                    else
                        diagnostics.Add(new ProcedureDiagnostic(location, "Unsupported measurement kind. Use 'VOLTAGE' or 'RESISTANCE'."));
                }
                break;

            case "measure":
                if (args.Count >= 2 && TryStringArg(args[0], out var measRole) && TryStringArg(args[1], out var measKindText))
                {
                    if (TryParseKind(measKindText, out var measKind))
                        measures.Add(new ProcedureMeasureReference(measRole, measKind, location));
                    else
                        diagnostics.Add(new ProcedureDiagnostic(location, "Unsupported measurement kind. Use 'VOLTAGE' or 'RESISTANCE'."));
                }
                break;

            case "wait_s":
                if (args.Count == 1 && TryNumericLiteral(args[0], out var seconds))
                {
                    if (!double.IsFinite(seconds) || seconds < 0 || seconds > MaxWaitSeconds)
                        diagnostics.Add(new ProcedureDiagnostic(location, "wait_s expects a duration between 0 and 86400 seconds."));
                }
                break;

            case "verify_between":
                if (args.Count >= 4 && TryStringArg(args[3], out var unitText) && !TryParseUnit(unitText, out _))
                    diagnostics.Add(new ProcedureDiagnostic(location, "verify_between requires unit 'Ohm' or 'V'."));
                if (args.Count >= 3 && TryNumericLiteral(args[1], out var low) && TryNumericLiteral(args[2], out var high) && low > high)
                    diagnostics.Add(new ProcedureDiagnostic(location, "verify_between lower limit must not exceed upper limit."));
                break;
        }
    }

    private static bool TryStringArg(Argument arg, out string value)
    {
        if (arg is PositionalArgument pos && pos.Value is StringLiteral literal)
        {
            value = literal.Value;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryNumericLiteral(Argument arg, out double value)
    {
        if (arg is PositionalArgument pos)
            return TryNumericExpr(pos.Value, out value);
        value = 0;
        return false;
    }

    private static bool TryNumericExpr(Expr expr, out double value)
    {
        switch (expr)
        {
            case FloatLiteral f:
                value = f.Value;
                return true;
            case IntLiteral i:
                value = (double)i.Value.Value;
                return true;
            case MinusExpr m when TryNumericExpr(m.Operand, out var inner):
                value = -inner;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
