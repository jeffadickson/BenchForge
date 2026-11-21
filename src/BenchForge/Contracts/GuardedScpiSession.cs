using BenchForge.Async;

namespace BenchForge.Contracts;

/// <summary>
/// Exception thrown when an <see cref="IInstrumentOperationGuard"/> blocks a SCPI command prior to wire dispatch.
/// </summary>
public sealed class OperationGuardDeniedException : InvalidOperationException
{
    /// <summary>Create an exception carrying the structured guard denial decision.</summary>
    /// <param name="decision">The denial decision returned by the operation guard.</param>
    public OperationGuardDeniedException(OperationGuardDecision decision)
        : base($"Operation denied by guard '{decision.RuleId}': {decision.Reason}")
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
    }

    /// <summary>Structured denial decision including rule identifier and operator reason.</summary>
    public OperationGuardDecision Decision { get; }
}

/// <summary>
/// Wraps any synchronous or asynchronous SCPI session so every command is classified and evaluated by
/// an <see cref="IInstrumentOperationGuard"/> before any bytes reach the transport.
/// </summary>
public sealed class GuardedScpiSession : IScpiSession, IAsyncScpiSession
{
    private readonly string _instrumentId;
    private readonly IScpiSession? _syncInner;
    private readonly IAsyncScpiSession? _asyncInner;
    private readonly IInstrumentOperationGuard _guard;
    private readonly bool _isPhysicalHardware;

    /// <summary>Wrap a synchronous SCPI session with pre-dispatch guard enforcement.</summary>
    /// <param name="instrumentId">Logical instrument role or canonical resource identifier.</param>
    /// <param name="inner">Underlying synchronous SCPI session (lifetime remains caller-owned).</param>
    /// <param name="guard">Guard evaluated prior to every write or query.</param>
    /// <param name="isPhysicalHardware">True when targeting physical equipment; false for mocks or simulation.</param>
    public GuardedScpiSession(
        string instrumentId,
        IScpiSession inner,
        IInstrumentOperationGuard guard,
        bool isPhysicalHardware = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentId);
        _instrumentId = instrumentId;
        _syncInner = inner ?? throw new ArgumentNullException(nameof(inner));
        _asyncInner = inner as IAsyncScpiSession;
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _isPhysicalHardware = isPhysicalHardware;
    }

    /// <summary>Wrap an asynchronous SCPI session with pre-dispatch guard enforcement.</summary>
    /// <param name="instrumentId">Logical instrument role or canonical resource identifier.</param>
    /// <param name="inner">Underlying asynchronous SCPI session (lifetime remains caller-owned).</param>
    /// <param name="guard">Guard evaluated prior to every write or query.</param>
    /// <param name="isPhysicalHardware">True when targeting physical equipment; false for mocks or simulation.</param>
    public GuardedScpiSession(
        string instrumentId,
        IAsyncScpiSession inner,
        IInstrumentOperationGuard guard,
        bool isPhysicalHardware = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentId);
        _instrumentId = instrumentId;
        _asyncInner = inner ?? throw new ArgumentNullException(nameof(inner));
        _syncInner = inner as IScpiSession;
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _isPhysicalHardware = isPhysicalHardware;
    }

    /// <inheritdoc/>
    public void Write(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        EnforceSync(command, isQuery: false);
        if (_syncInner is not null)
        {
            _syncInner.Write(command);
            return;
        }
        _asyncInner!.WriteAsync(command).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public string Query(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        EnforceSync(command, isQuery: true);
        if (_syncInner is not null)
            return _syncInner.Query(command);
        return _asyncInner!.QueryAsync(command).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        cancellationToken.ThrowIfCancellationRequested();
        await EnforceAsync(command, isQuery: false, cancellationToken).ConfigureAwait(false);
        if (_asyncInner is not null)
        {
            await _asyncInner.WriteAsync(command, cancellationToken).ConfigureAwait(false);
            return;
        }
        _syncInner!.Write(command);
    }

    /// <inheritdoc/>
    public async ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        cancellationToken.ThrowIfCancellationRequested();
        await EnforceAsync(command, isQuery: true, cancellationToken).ConfigureAwait(false);
        if (_asyncInner is not null)
            return await _asyncInner.QueryAsync(command, cancellationToken).ConfigureAwait(false);
        return _syncInner!.Query(command);
    }

    /// <summary>Classify a SCPI command string into its safety operation category.</summary>
    /// <param name="command">SCPI command text.</param>
    /// <param name="isQuery">True when invoked as a query.</param>
    /// <returns>Classified <see cref="InstrumentOperationKind"/>.</returns>
    public static InstrumentOperationKind ClassifyScpi(string command, bool isQuery)
    {
        var trimmed = command.Trim();
        if (isQuery || trimmed.EndsWith('?'))
            return InstrumentOperationKind.Query;

        var upper = trimmed.ToUpperInvariant();
        if (upper.StartsWith("*RST", StringComparison.Ordinal) ||
            upper.StartsWith("*CLS", StringComparison.Ordinal) ||
            upper.StartsWith(":SYST:PRES", StringComparison.Ordinal) ||
            upper.StartsWith("SYST:PRES", StringComparison.Ordinal))
            return InstrumentOperationKind.Recovery;

        if (upper.Contains("OUTP", StringComparison.Ordinal) ||
            upper.Contains("OPER", StringComparison.Ordinal) ||
            upper.StartsWith("N1X", StringComparison.Ordinal))
            return InstrumentOperationKind.OutputControl;

        return InstrumentOperationKind.Write;
    }

    private void EnforceSync(string command, bool isQuery)
    {
        var decision = _guard.EvaluateAsync(BuildContext(command, isQuery)).AsTask().GetAwaiter().GetResult();
        if (!decision.Allowed)
            throw new OperationGuardDeniedException(decision);
    }

    private async ValueTask EnforceAsync(string command, bool isQuery, CancellationToken cancellationToken)
    {
        var decision = await _guard.EvaluateAsync(BuildContext(command, isQuery), cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed)
            throw new OperationGuardDeniedException(decision);
    }

    private InstrumentOperationContext BuildContext(string command, bool isQuery) =>
        new(_instrumentId, command.Trim(), ClassifyScpi(command, isQuery), 1, null, _isPhysicalHardware);
}

/// <summary>
/// Built-in fail-closed safety interlock that blocks output-energizing commands unless an explicit interlock token is armed.
/// </summary>
public sealed class PhysicalOutputInterlockGuard : IInstrumentOperationGuard
{
    private readonly Func<bool> _isInterlockArmed;
    private readonly bool _enforceOnSimulation;

    /// <summary>Create an output interlock guard backed by a live interlock predicate.</summary>
    /// <param name="isInterlockArmed">Returns true only when energizing outputs is explicitly permitted.</param>
    /// <param name="enforceOnSimulation">When true, also enforces the interlock on simulated instruments.</param>
    public PhysicalOutputInterlockGuard(Func<bool> isInterlockArmed, bool enforceOnSimulation = false)
    {
        _isInterlockArmed = isInterlockArmed ?? throw new ArgumentNullException(nameof(isInterlockArmed));
        _enforceOnSimulation = enforceOnSimulation;
    }

    /// <inheritdoc/>
    public ValueTask<OperationGuardDecision> EvaluateAsync(
        InstrumentOperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Kind == InstrumentOperationKind.OutputControl &&
            (context.IsPhysicalHardware || _enforceOnSimulation) &&
            !_isInterlockArmed())
        {
            return ValueTask.FromResult(OperationGuardDecision.Deny(
                "BF-INTERLOCK-OUTPUT",
                $"Output control on '{context.InstrumentId}' is blocked while the output interlock is disarmed."));
        }

        return ValueTask.FromResult(OperationGuardDecision.Allow("BF-INTERLOCK-OUTPUT"));
    }
}
