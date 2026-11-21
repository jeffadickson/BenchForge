namespace BenchForge.Procedures;

/// <summary>Declared capabilities of a pluggable procedure scripting engine.</summary>
/// <param name="EngineName">Human-readable engine identifier (for example, <c>starlark-cs</c>).</param>
/// <param name="SupportsDeterministicFutures">True when the engine supports deterministic parallel futures.</param>
/// <param name="SupportsStaticTypeChecking">True when the engine supports pre-execution static type checking.</param>
/// <param name="SupportsWebAssembly">True when the engine can execute in browser WebAssembly sandboxes.</param>
public sealed record ProcedureEngineCapabilities(
    string EngineName,
    bool SupportsDeterministicFutures,
    bool SupportsStaticTypeChecking,
    bool SupportsWebAssembly);

/// <summary>Library-neutral execution budgets enforced on procedure script evaluation.</summary>
/// <param name="MaxSteps">Maximum interpreter statements/expressions allowed per run.</param>
/// <param name="MaxAllocatedBytes">Maximum single-object allocation ceiling in bytes.</param>
/// <param name="Timeout">Maximum wall-clock execution duration for one run.</param>
public sealed record ProcedureScriptBudget(
    long MaxSteps = 100_000,
    long MaxAllocatedBytes = 1024 * 1024,
    TimeSpan? Timeout = null)
{
    /// <summary>Validate that all configured execution budgets are strictly positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Any configured limit is non-positive.</exception>
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

/// <summary>Engine-neutral syntax or semantic preflight diagnostic for a procedure script.</summary>
/// <param name="Line">One-based line number in the procedure source.</param>
/// <param name="Column">One-based column number in the procedure source.</param>
/// <param name="Message">Operator-readable diagnostic description.</param>
public sealed record ProcedureScriptDiagnostic(int Line, int Column, string Message);

/// <summary>Result of validating a procedure script without executing it.</summary>
/// <param name="Diagnostics">All syntax and static preflight diagnostics found.</param>
public sealed record ProcedurePreflightResult(IReadOnlyList<ProcedureScriptDiagnostic> Diagnostics)
{
    /// <summary>True when preflight succeeded with zero diagnostics.</summary>
    public bool Success => Diagnostics.Count == 0;
}

/// <summary>
/// Engine-neutral scripting seam in BenchForge core so host applications and experiments can decouple
/// from specific scripting runtimes (such as <c>starlark-cs</c>) while those runtimes evolve.
/// </summary>
public interface IProcedureScriptEngine
{
    /// <summary>Engine capability metadata for feature negotiation and diagnostics.</summary>
    ProcedureEngineCapabilities Capabilities { get; }

    /// <summary>Parse and statically preflight a procedure script without executing code or touching instruments.</summary>
    /// <param name="source">Procedure script text.</param>
    /// <param name="fileName">Logical filename reported in diagnostics.</param>
    /// <returns>Preflight diagnostics; never executes side effects.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    ProcedurePreflightResult Preflight(string source, string fileName = "procedure.star");
}
