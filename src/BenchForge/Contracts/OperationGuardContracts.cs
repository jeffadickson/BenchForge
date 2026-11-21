using BenchForge.Quantities;

namespace BenchForge.Contracts;

/// <summary>Classification of an instrument operation evaluated before transport dispatch.</summary>
public enum InstrumentOperationKind
{
    /// <summary>Read-only state query or measurement.</summary>
    Query,

    /// <summary>State-mutating configuration or setpoint write.</summary>
    Write,

    /// <summary>Output-energizing or actuation command requiring heightened safety checks.</summary>
    OutputControl,

    /// <summary>Instrument reset or safe-state recovery operation.</summary>
    Recovery
}

/// <summary>Immutable descriptor of a pending instrument command evaluated by safety or contract guards.</summary>
/// <param name="InstrumentId">Logical instrument role or resource identifier.</param>
/// <param name="OperationName">Logical driver operation or parameter name.</param>
/// <param name="Kind">Operation safety classification.</param>
/// <param name="Channel">One-based instrument channel index.</param>
/// <param name="RequestedValue">Optional canonical quantity requested for a write or setpoint.</param>
/// <param name="IsPhysicalHardware">True when targeting physical equipment rather than a simulation double.</param>
public sealed record InstrumentOperationContext(
    string InstrumentId,
    string OperationName,
    InstrumentOperationKind Kind,
    int Channel = 1,
    CanonicalQuantity? RequestedValue = null,
    bool IsPhysicalHardware = false);

/// <summary>Result returned by an <see cref="IInstrumentOperationGuard"/> evaluation.</summary>
/// <param name="Allowed">True when the operation satisfies the guard; false to block dispatch.</param>
/// <param name="RuleId">Stable rule or contract identifier (for example, a formal contract or interlock ID).</param>
/// <param name="Reason">Optional operator-readable explanation when blocked.</param>
public sealed record OperationGuardDecision(
    bool Allowed,
    string RuleId,
    string? Reason = null)
{
    /// <summary>Create an allow decision attributed to <paramref name="ruleId"/>.</summary>
    /// <param name="ruleId">Stable rule identifier.</param>
    /// <returns>An allowed <see cref="OperationGuardDecision"/>.</returns>
    public static OperationGuardDecision Allow(string ruleId) => new(true, ruleId, null);

    /// <summary>Create a deny decision that prevents command dispatch.</summary>
    /// <param name="ruleId">Stable rule identifier.</param>
    /// <param name="reason">Actionable denial explanation without secret or raw payload leakage.</param>
    /// <returns>A denied <see cref="OperationGuardDecision"/>.</returns>
    public static OperationGuardDecision Deny(string ruleId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(false, ruleId, reason);
    }
}

/// <summary>
/// Pluggable pre-dispatch verification seam for safety interlocks, policy checkers, and formal contract
/// verifiers (such as SharpContracts) without coupling drivers to any specific verifier implementation.
/// </summary>
public interface IInstrumentOperationGuard
{
    /// <summary>Evaluate whether a pending instrument operation may proceed.</summary>
    /// <param name="context">Immutable operation metadata.</param>
    /// <param name="cancellationToken">Cancellation token observed before returning a decision.</param>
    /// <returns>Decision indicating whether dispatch is permitted; any denial fails closed.</returns>
    ValueTask<OperationGuardDecision> EvaluateAsync(
        InstrumentOperationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Evaluates an ordered set of <see cref="IInstrumentOperationGuard"/> instances, failing closed on the first denial.</summary>
public sealed class CompositeOperationGuard : IInstrumentOperationGuard
{
    private readonly IInstrumentOperationGuard[] _guards;

    /// <summary>Create a composite guard from an immutable snapshot of child guards.</summary>
    /// <param name="guards">Ordered guards to evaluate sequentially.</param>
    /// <exception cref="ArgumentNullException"><paramref name="guards"/> or any element is null.</exception>
    public CompositeOperationGuard(IEnumerable<IInstrumentOperationGuard> guards)
    {
        ArgumentNullException.ThrowIfNull(guards);
        _guards = guards.ToArray();
        foreach (var guard in _guards)
            ArgumentNullException.ThrowIfNull(guard);
    }

    /// <inheritdoc/>
    public async ValueTask<OperationGuardDecision> EvaluateAsync(
        InstrumentOperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var guard in _guards)
        {
            var decision = await guard.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed)
                return decision;
        }
        return OperationGuardDecision.Allow("BF-GUARD-COMPOSITE");
    }
}
