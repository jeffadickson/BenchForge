using BenchDouble.Experiments;
using BenchDouble.Simulation;
using UnitsNet;

namespace BenchDouble.Procedures;

/// <summary>
/// Semantic preflight for the restricted mock-only DSL. Validation never
/// sends instrument commands or advances simulated time.
/// </summary>
public static class ProcedureValidator
{
    public static IReadOnlyList<ProcedureDiagnostic> Validate(
        ProcedureDocument document, IReadOnlyList<ExperimentInstrument> instruments)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(instruments);
        var errors = new List<ProcedureDiagnostic>();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var configured = new Dictionary<string, MeasurementKind>(StringComparer.Ordinal);
        var variables = new Dictionary<string, MeasurementKind>(StringComparer.Ordinal);
        var available = instruments.ToDictionary(x => x.Role, StringComparer.Ordinal);

        foreach (var step in document.Steps)
        {
            switch (step)
            {
                case InstrumentStep declaration:
                    if (!declared.Add(declaration.Role))
                        errors.Add(new(step.Location, $"Duplicate instrument declaration '{declaration.Role}'."));
                    else if (!available.TryGetValue(declaration.Role, out var instrument))
                        errors.Add(new(step.Location, $"Unknown experiment instrument '{declaration.Role}'."));
                    else if (instrument.Backend != "Simulated" ||
                             instrument.InterfaceType != typeof(ITypedConfigurableMeasurementSource))
                        errors.Add(new(step.Location,
                            $"Instrument '{declaration.Role}' must be a simulated typed meter."));
                    break;
                case ConfigureStep configure:
                    if (!declared.Contains(configure.Role))
                        errors.Add(new(step.Location,
                            $"Instrument '{configure.Role}' must be declared before use."));
                    else
                        configured[configure.Role] = configure.Kind;
                    break;
                case MeasureStep measure:
                    if (!declared.Contains(measure.Role))
                        errors.Add(new(step.Location,
                            $"Instrument '{measure.Role}' must be declared before use."));
                    else if (!configured.TryGetValue(measure.Role, out var mode) ||
                             mode != measure.Kind)
                        errors.Add(new(step.Location,
                            $"Configure '{measure.Role}' for {measure.Kind} before measuring."));
                    if (!variables.TryAdd(measure.Variable, measure.Kind))
                        errors.Add(new(step.Location,
                            $"Variable '{measure.Variable}' is already defined."));
                    break;
                case VerifyStep verify:
                    if (!variables.TryGetValue(verify.Variable, out var kind))
                        errors.Add(new(step.Location,
                            $"Unknown measurement variable '{verify.Variable}'."));
                    else if (kind != verify.Kind)
                        errors.Add(new(step.Location,
                            $"Unit dimension does not match variable '{verify.Variable}'."));
                    break;
            }
        }
        return errors.AsReadOnly();
    }
}

/// <summary>Failed limit assertion with source location; the runner records a failed run.</summary>
public sealed class ProcedureAssertionException(
    ProcedureLocation location, string message) : Exception(
        $"Procedure line {location.Line}: {message}")
{
    public ProcedureLocation Location { get; } = location;
}

/// <summary>
/// Executes a validated, non-Turing-complete procedure against a simulated
/// experiment session. It uses the existing ExperimentRunner lifecycle and
/// TypedMockMeter; it cannot connect to physical instruments.
/// </summary>
public sealed class MockProcedure : ExperimentProcedure
{
    private readonly ProcedureDocument _document;
    private readonly ExperimentSession _session;
    private readonly Dictionary<string, (MeasurementKind Kind, double Value)> _variables =
        new(StringComparer.Ordinal);

    public MockProcedure(string name, ProcedureDocument document, ExperimentSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        var diagnostics = ProcedureValidator.Validate(document, session.RegisteredInstruments);
        if (diagnostics.Count > 0)
            throw new ArgumentException(string.Join("; ",
                diagnostics.Select(x => $"line {x.Location.Line}: {x.Message}")), nameof(document));
        Name = name;
    }

    public override string Name { get; }

    public override async Task ExecuteAsync(ExperimentContext context, CancellationToken token)
    {
        _variables.Clear();
        foreach (var step in _document.Steps)
        {
            token.ThrowIfCancellationRequested();
            switch (step)
            {
                case InstrumentStep:
                    break; // Role resolution is explicit during subsequent operations.
                case ConfigureStep configure:
                    var instrument = Meter(configure.Role);
                    if (configure.Kind == MeasurementKind.Resistance)
                        await instrument.ConfigureAsync<ElectricResistance>(token);
                    else
                        await instrument.ConfigureAsync<ElectricPotential>(token);
                    break;
                case MeasureStep measure:
                    var meter = Meter(measure.Role);
                    double value = measure.Kind == MeasurementKind.Resistance
                        ? (await meter.MeasureAsync<ElectricResistance>(token)).Ohms
                        : (await meter.MeasureAsync<ElectricPotential>(token)).Volts;
                    _variables.Add(measure.Variable, (measure.Kind, value));
                    await context.EmitAsync(new ExperimentSample(DateTimeOffset.UtcNow,
                        new Dictionary<string, double>
                        {
                            [measure.Variable] = value,
                            ["simulatedElapsedSeconds"] = _session.Clock.Elapsed.TotalSeconds
                        },
                        new Dictionary<string, string>
                        {
                            [measure.Variable] = measure.Kind == MeasurementKind.Resistance ? "Ohm" : "V",
                            ["simulatedElapsedSeconds"] = "s"
                        }), token);
                    break;
                case WaitStep wait:
                    // No real waiting: deterministic virtual time advances only in simulation.
                    _session.Clock.AdvanceBy(wait.Duration);
                    break;
                case VerifyStep verify:
                    var measured = _variables[verify.Variable];
                    if (measured.Value < verify.Minimum || measured.Value > verify.Maximum)
                        throw new ProcedureAssertionException(step.Location,
                            $"{verify.Variable} = {measured.Value:G9} is outside " +
                            $"[{verify.Minimum:G9}, {verify.Maximum:G9}] " +
                            (verify.Kind == MeasurementKind.Resistance ? "Ohm" : "V"));
                    break;
            }
        }
    }

    private ITypedConfigurableMeasurementSource Meter(string role) =>
        _session.Instrument<ITypedConfigurableMeasurementSource>(role);
}
