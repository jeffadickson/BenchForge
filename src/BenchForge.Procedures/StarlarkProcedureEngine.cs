namespace BenchForge.Procedures;

/// <summary>
/// <see cref="IProcedureScriptEngine"/> adapter backed by <c>starlark-cs</c>, isolating callers from
/// upstream Starlark API changes while the scripting runtime evolves toward 1.0.
/// </summary>
public sealed class StarlarkProcedureEngine : IProcedureScriptEngine
{
    /// <summary>Shared stateless adapter instance.</summary>
    public static StarlarkProcedureEngine Instance { get; } = new();

    /// <inheritdoc/>
    public ProcedureEngineCapabilities Capabilities { get; } = new(
        EngineName: "starlark-cs",
        SupportsDeterministicFutures: true,
        SupportsStaticTypeChecking: true,
        SupportsWebAssembly: true);

    /// <inheritdoc/>
    public ProcedurePreflightResult Preflight(string source, string fileName = "procedure.star")
    {
        var parsed = ProcedureParser.Parse(source, fileName);
        var diagnostics = parsed.Diagnostics
            .Select(d => new ProcedureScriptDiagnostic(d.Location.Line, d.Location.Column, d.Message))
            .ToArray();
        return new ProcedurePreflightResult(Array.AsReadOnly(diagnostics));
    }
}
