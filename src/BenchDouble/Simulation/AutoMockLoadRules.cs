using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>
/// Declarative matching conditions for one simulated load measurement.
/// All times are experiment-scoped logical elapsed time, never wall time.
/// </summary>
public sealed record AutoMockLoadCondition(
    TimeSpan? From = null,
    TimeSpan? Until = null,
    ElectricPotential? MinimumVoltage = null,
    ElectricPotential? MaximumVoltage = null)
{
    public void Validate()
    {
        if (From is { } start && start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(From));
        if (Until is { } end && (end < TimeSpan.Zero || (From is { } beginning && end <= beginning)))
            throw new ArgumentOutOfRangeException(nameof(Until));
        if (MinimumVoltage is { } low && (!double.IsFinite(low.Volts) || low.Volts < 0))
            throw new ArgumentOutOfRangeException(nameof(MinimumVoltage));
        if (MaximumVoltage is { } high && (!double.IsFinite(high.Volts) || high.Volts < 0 ||
            (MinimumVoltage is { } minimum && high.Volts < minimum.Volts)))
            throw new ArgumentOutOfRangeException(nameof(MaximumVoltage));
    }

    public bool Matches(ElectricPotential voltage, TimeSpan elapsed)
    {
        if (!double.IsFinite(voltage.Volts)) throw new ArgumentOutOfRangeException(nameof(voltage));
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        return (From is null || elapsed >= From.Value) &&
        (Until is null || elapsed < Until.Value) &&
        (MinimumVoltage is null || voltage.Volts >= MinimumVoltage.Value.Volts) &&
        (MaximumVoltage is null || voltage.Volts <= MaximumVoltage.Value.Volts);
    }
}

/// <summary>Supported safe, finite and serializable mock actions; no executable expression strings.</summary>
public enum AutoMockLoadActionKind { FixedCurrent, MultiplyBaseline }

/// <summary>A deterministic declarative rule applied to the injected baseline load.</summary>
public sealed record AutoMockLoadRule(
    string Name,
    int Priority,
    AutoMockLoadCondition Condition,
    AutoMockLoadActionKind Action,
    double Value)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentNullException.ThrowIfNull(Condition);
        Condition.Validate();
        if (!double.IsFinite(Value) || Value < 0)
            throw new ArgumentOutOfRangeException(nameof(Value),
                $"Rule '{Name}' requires a finite nonnegative value.");
        if (!Enum.IsDefined(Action))
            throw new ArgumentException($"Invalid action for AutoMock rule '{Name}'.");
    }
}

/// <summary>
/// Creates immutable rule sets at experiment composition time. A higher priority
/// wins; ties use declaration order. No hot mutation of rules during a run.
/// </summary>
public sealed class AutoMockLoadRulesBuilder
{
    private readonly List<AutoMockLoadRule> _rules = new();
    public AutoMockLoadRulesBuilder FixedCurrent(
        string name, ElectricCurrent current,
        AutoMockLoadCondition? when = null, int priority = 0)
    {
        Add(new(name, priority, when ?? new(), AutoMockLoadActionKind.FixedCurrent,
            current.Amperes));
        return this;
    }

    public AutoMockLoadRulesBuilder ScaleBaseline(
        string name, double factor,
        AutoMockLoadCondition? when = null, int priority = 0)
    {
        Add(new(name, priority, when ?? new(), AutoMockLoadActionKind.MultiplyBaseline,
            factor));
        return this;
    }

    /// <summary>Import a validated rule, for example from a JSON rule document.</summary>
    public AutoMockLoadRulesBuilder AddRule(AutoMockLoadRule rule)
    {
        Add(rule);
        return this;
    }

    private void Add(AutoMockLoadRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();
        if (_rules.Any(existing => existing.Name == rule.Name))
            throw new InvalidOperationException($"Duplicate AutoMock rule '{rule.Name}'.");
        if (_rules.Count >= 256) throw new InvalidOperationException("AutoMock rule limit reached.");
        _rules.Add(rule);
    }

    public IReadOnlyList<AutoMockLoadRule> Build() => Array.AsReadOnly(_rules.ToArray());
}

/// <summary>One evaluated mock measurement with rule provenance for future telemetry.</summary>
public sealed record AutoMockEvaluation(
    ElectricCurrent Current, string? MatchedRule, TimeSpan SimulatedElapsed);

/// <summary>
/// An ordinary ILoadBehavior decorator. Command/state behavior remains in the
/// instrument; the decorator only controls the simulated current reading.
/// </summary>
public sealed class RuleBasedLoadBehavior : ILoadBehavior
{
    private readonly ILoadBehavior _baseline;
    private readonly AutoMockLoadRule[] _rules;

    public RuleBasedLoadBehavior(
        ILoadBehavior baseline, IEnumerable<AutoMockLoadRule> rules)
    {
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.Take(257).ToArray();
        if (_rules.Length > 256) throw new ArgumentException("AutoMock rule limit reached.", nameof(rules));
        foreach (var rule in _rules) { ArgumentNullException.ThrowIfNull(rule); rule.Validate(); }
        if (_rules.Select(r => r.Name).Distinct(StringComparer.Ordinal).Count() != _rules.Length)
            throw new ArgumentException("AutoMock rule names must be unique.", nameof(rules));
        _rules = _rules.OrderByDescending(r => r.Priority).ToArray();
    }

    public ElectricCurrent CurrentAt(ElectricPotential voltage, TimeSpan elapsed) =>
        EvaluateAt(voltage, elapsed).Current;

    /// <summary>
    /// Evaluate the rules and expose which rule matched, without mutating state.
    /// Instrument logging can publish this result separately from physical state.
    /// </summary>
    public AutoMockEvaluation EvaluateAt(ElectricPotential voltage, TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero || !double.IsFinite(voltage.Volts))
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        foreach (var rule in _rules)
        {
            if (!rule.Condition.Matches(voltage, elapsed)) continue;
            double amps = rule.Action switch
            {
                AutoMockLoadActionKind.FixedCurrent => rule.Value,
                AutoMockLoadActionKind.MultiplyBaseline =>
                    _baseline.CurrentAt(voltage, elapsed).Amperes * rule.Value,
                _ => throw new InvalidOperationException("Unsupported mock rule action.")
            };
            if (!double.IsFinite(amps) || amps < 0)
                throw new InvalidOperationException($"Rule '{rule.Name}' produced an invalid current.");
            return new AutoMockEvaluation(ElectricCurrent.FromAmperes(amps),
                rule.Name, elapsed);
        }
        var baseline = _baseline.CurrentAt(voltage, elapsed);
        if (!double.IsFinite(baseline.Amperes) || baseline.Amperes < 0)
            throw new InvalidOperationException("Baseline produced an invalid current.");
        return new AutoMockEvaluation(baseline, null, elapsed);
    }
}
