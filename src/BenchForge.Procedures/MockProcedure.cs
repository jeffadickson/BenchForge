using BenchForge.Experiments;
using BenchForge.Simulation;
using Microsoft.Extensions.Logging;
using Starlark.Eval;
using UnitsNet;

namespace BenchForge.Procedures;

/// <summary>
/// Semantic preflight for Starlark mock-only procedures. Validation inspects statically
/// declared instrument roles and operations without executing script code, touching
/// instruments, or advancing simulated time.
/// </summary>
public static class ProcedureValidator
{
    /// <summary>
    /// Validate a parsed Starlark procedure against the experiment's registered instruments.
    /// </summary>
    /// <param name="document">Parsed Starlark procedure document.</param>
    /// <param name="instruments">Instruments registered in the target experiment session.</param>
    /// <returns>Read-only list of preflight diagnostics; empty when preflight passes.</returns>
    /// <exception cref="ArgumentNullException">Document or instruments list is null.</exception>
    public static IReadOnlyList<ProcedureDiagnostic> Validate(
        ProcedureDocument document,
        IReadOnlyList<ExperimentInstrument> instruments)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(instruments);

        var errors = new List<ProcedureDiagnostic>();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var available = instruments.ToDictionary(x => x.Role, StringComparer.Ordinal);

        foreach (var declaration in document.DeclaredInstruments)
        {
            if (!declared.Add(declaration.Role))
            {
                errors.Add(new ProcedureDiagnostic(declaration.Location, $"Duplicate instrument declaration '{declaration.Role}'."));
                continue;
            }
            if (!available.TryGetValue(declaration.Role, out var instrument))
            {
                errors.Add(new ProcedureDiagnostic(declaration.Location, $"Unknown experiment instrument '{declaration.Role}'."));
                continue;
            }
            if (instrument.Backend != "Simulated" ||
                instrument.InterfaceType != typeof(ITypedConfigurableMeasurementSource))
            {
                errors.Add(new ProcedureDiagnostic(declaration.Location, $"Instrument '{declaration.Role}' must be a simulated typed meter."));
            }
        }

        var configuredKinds = new Dictionary<string, HashSet<MeasurementKind>>(StringComparer.Ordinal);
        foreach (var configure in document.StaticConfigures)
        {
            if (!declared.Contains(configure.Role))
            {
                errors.Add(new ProcedureDiagnostic(configure.Location, $"Instrument '{configure.Role}' must be declared before use."));
                continue;
            }
            if (!configuredKinds.TryGetValue(configure.Role, out var set))
            {
                set = new HashSet<MeasurementKind>();
                configuredKinds[configure.Role] = set;
            }
            set.Add(configure.Kind);
        }

        foreach (var measure in document.StaticMeasures)
        {
            if (!declared.Contains(measure.Role))
            {
                errors.Add(new ProcedureDiagnostic(measure.Location, $"Instrument '{measure.Role}' must be declared before use."));
                continue;
            }
            if (!configuredKinds.TryGetValue(measure.Role, out var set) || !set.Contains(measure.Kind))
            {
                errors.Add(new ProcedureDiagnostic(measure.Location, $"Configure '{measure.Role}' for {measure.Kind} before measuring."));
            }
        }

        return errors.AsReadOnly();
    }
}

/// <summary>Failed limit assertion with one-based Starlark source location; the runner records a failed run.</summary>
/// <param name="location">One-based line and column of the failing verification call.</param>
/// <param name="message">Assertion failure details including measured value and bounds.</param>
public sealed class ProcedureAssertionException(
    ProcedureLocation location,
    string message) : Exception($"Procedure line {location.Line}: {message}")
{
    /// <summary>One-based source location of the failing assertion.</summary>
    public ProcedureLocation Location { get; } = location;
}

/// <summary>
/// Executes a validated, hermetic Starlark procedure against a simulated experiment session
/// using <c>starlark-cs</c>. Enforces uncatchable step, memory, wall-clock, and cancellation
/// budgets; uses the existing <see cref="ExperimentRunner"/> lifecycle and virtual clock;
/// never connects to physical instruments.
/// </summary>
public sealed class MockProcedure : ExperimentProcedure
{
    private const double MaxWaitSeconds = 86_400;

    private readonly ProcedureDocument _document;
    private readonly ExperimentSession _session;
    private readonly StarlarkProcedureLimits _limits;

    /// <summary>
    /// Construct a runnable Starlark mock procedure after running semantic preflight validation.
    /// </summary>
    /// <param name="name">Human-readable procedure run name.</param>
    /// <param name="document">Parsed Starlark procedure document.</param>
    /// <param name="session">Simulated experiment session providing registered mock instruments and virtual clock.</param>
    /// <param name="limits">Optional uncatchable execution budgets; defaults to bounded step/memory/timeout limits.</param>
    /// <exception cref="ArgumentException">Name is blank or preflight validation fails.</exception>
    /// <exception cref="ArgumentNullException">Document or session is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Any configured limit is non-positive.</exception>
    public MockProcedure(
        string name,
        ProcedureDocument document,
        ExperimentSession session,
        StarlarkProcedureLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _limits = limits ?? new StarlarkProcedureLimits();
        _limits.Validate();

        var diagnostics = ProcedureValidator.Validate(document, session.RegisteredInstruments);
        if (diagnostics.Count > 0)
        {
            throw new ArgumentException(
                string.Join("; ", diagnostics.Select(x => $"line {x.Location.Line}: {x.Message}")),
                nameof(document));
        }

        Name = name;
    }

    /// <summary>Human-readable procedure name reported in <see cref="ExperimentRun"/>.</summary>
    public override string Name { get; }

    /// <summary>Number of Starlark interpreter steps consumed during the most recent execution.</summary>
    public long LastRunSteps { get; private set; }

    /// <summary>
    /// Execute the Starlark procedure inside a bounded, hermetic interpreter bound to simulated mocks.
    /// </summary>
    /// <param name="context">Experiment run context for emitting unit-tagged samples and structured logs.</param>
    /// <param name="token">Cancellation token enforced cooperatively by both the Starlark engine and async mocks.</param>
    /// <returns>Completion of the procedure execution.</returns>
    /// <exception cref="ProcedureAssertionException">A <c>verify_between</c> assertion failed.</exception>
    /// <exception cref="ExecutionLimitException">The procedure exceeded step, memory, or timeout limits.</exception>
    /// <exception cref="OperationCanceledException">Execution was cancelled via <paramref name="token"/>.</exception>
    /// <exception cref="StarlarkException">A script runtime error or host validation violation occurred.</exception>
    public override Task ExecuteAsync(ExperimentContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        token.ThrowIfCancellationRequested();

        var declaredRoles = new HashSet<string>(StringComparer.Ordinal);
        var configuredModes = new Dictionary<string, MeasurementKind>(StringComparer.Ordinal);
        var available = _session.RegisteredInstruments.ToDictionary(x => x.Role, StringComparer.Ordinal);
        int unnamedSampleIndex = 0;

        var interpreter = new Interpreter
        {
            Dialect = ProcedureParser.ProcedureDialect,
            MaxSteps = _limits.MaxSteps,
            MaxAllocatedBytes = _limits.MaxAllocatedBytes,
            Timeout = _limits.Timeout ?? TimeSpan.FromSeconds(10),
            Cancellation = token,
            Print = line => context.Logger.LogInformation("Procedure {ProcedureName}: {Line}", Name, line)
        };

        interpreter.Register("instrument", (string role) =>
        {
            token.ThrowIfCancellationRequested();
            if (!available.TryGetValue(role, out var instrument))
                throw new InvalidOperationException($"Unknown experiment instrument '{role}'.");
            if (instrument.Backend != "Simulated" ||
                instrument.InterfaceType != typeof(ITypedConfigurableMeasurementSource))
                throw new InvalidOperationException($"Instrument '{role}' must be a simulated typed meter.");
            if (!declaredRoles.Add(role))
                throw new InvalidOperationException($"Duplicate instrument declaration '{role}'.");
        });

        interpreter.Register("configure", async (string role, string kindText) =>
        {
            token.ThrowIfCancellationRequested();
            if (!declaredRoles.Contains(role))
                throw new InvalidOperationException($"Instrument '{role}' must be declared before use.");
            if (!ProcedureParser.TryParseKind(kindText, out var kind))
                throw new ArgumentException("Unsupported measurement kind. Use 'VOLTAGE' or 'RESISTANCE'.");

            var meter = Meter(role);
            if (kind == MeasurementKind.Resistance)
                await meter.ConfigureAsync<ElectricResistance>(token).ConfigureAwait(false);
            else
                await meter.ConfigureAsync<ElectricPotential>(token).ConfigureAwait(false);
            configuredModes[role] = kind;
        });

        interpreter.Register("measure", async (string role, string kindText, string? name) =>
        {
            token.ThrowIfCancellationRequested();
            if (!declaredRoles.Contains(role))
                throw new InvalidOperationException($"Instrument '{role}' must be declared before use.");
            if (!ProcedureParser.TryParseKind(kindText, out var kind))
                throw new ArgumentException("Unsupported measurement kind. Use 'VOLTAGE' or 'RESISTANCE'.");
            if (!configuredModes.TryGetValue(role, out var mode) || mode != kind)
                throw new InvalidOperationException($"Configure '{role}' for {kind} before measuring.");

            var meter = Meter(role);
            ProcedureQuantity reading;
            if (kind == MeasurementKind.Resistance)
            {
                var resistance = await meter.MeasureAsync<ElectricResistance>(token).ConfigureAwait(false);
                reading = new ProcedureQuantity(kind, resistance.Ohms, "Ohm", resistance);
            }
            else
            {
                var voltage = await meter.MeasureAsync<ElectricPotential>(token).ConfigureAwait(false);
                reading = new ProcedureQuantity(kind, voltage.Volts, "V", voltage);
            }

            string sampleName = string.IsNullOrWhiteSpace(name)
                ? $"sample_{++unnamedSampleIndex}"
                : name;

            await context.EmitAsync(
                new ExperimentSample(
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, double>
                    {
                        [sampleName] = reading.Value,
                        ["simulatedElapsedSeconds"] = _session.Clock.Elapsed.TotalSeconds
                    },
                    new Dictionary<string, string>
                    {
                        [sampleName] = reading.Unit,
                        ["simulatedElapsedSeconds"] = "s"
                    }),
                token).ConfigureAwait(false);

            return reading;
        });

        interpreter.Register("wait_s", (double seconds) =>
        {
            token.ThrowIfCancellationRequested();
            if (!double.IsFinite(seconds) || seconds < 0 || seconds > MaxWaitSeconds)
                throw new ArgumentOutOfRangeException(nameof(seconds), "wait_s expects a duration between 0 and 86400 seconds.");
            // Deterministic simulation advance only; never sleeps wall-clock time.
            _session.Clock.AdvanceBy(TimeSpan.FromSeconds(seconds));
        });

        interpreter.Register("verify_between", (ProcedureQuantity reading, double minimum, double maximum, string unit) =>
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(reading);
            if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
                throw new ArgumentOutOfRangeException(nameof(minimum), "verify_between requires finite bounds with minimum <= maximum.");
            if (!ProcedureParser.TryParseUnit(unit, out var expectedKind))
                throw new ArgumentException("verify_between requires unit 'Ohm' or 'V'.");
            if (reading.Kind != expectedKind)
                throw new InvalidOperationException($"Unit dimension '{unit}' does not match measurement kind {reading.Kind}.");
            if (reading.Value < minimum || reading.Value > maximum)
            {
                string detail = $"{reading.Value:G9} {reading.Unit} is outside [{minimum:G9}, {maximum:G9}] {reading.Unit}";
                throw new StarlarkException(detail, new LimitCheckFailure(detail));
            }
        });

        try
        {
            interpreter.Execute(_document.SyntaxTree);
            LastRunSteps = interpreter.LastRunSteps;
        }
        catch (StarlarkException ex) when (ex.InnerException is LimitCheckFailure failure)
        {
            LastRunSteps = interpreter.LastRunSteps;
            var location = ex.Span is { } span && ex.CodeMap is { } map
                ? ProcedureParser.ResolveLocation(map, span)
                : new ProcedureLocation(1, 1);
            throw new ProcedureAssertionException(location, failure.Message);
        }
        catch
        {
            LastRunSteps = interpreter.LastRunSteps;
            throw;
        }

        return Task.CompletedTask;
    }

    private ITypedConfigurableMeasurementSource Meter(string role) =>
        _session.Instrument<ITypedConfigurableMeasurementSource>(role);

    private sealed class LimitCheckFailure(string message) : Exception(message);
}
