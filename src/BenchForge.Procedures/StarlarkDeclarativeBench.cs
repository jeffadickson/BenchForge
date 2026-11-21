using System.Globalization;
using System.Numerics;
using BenchForge.Async;
using BenchForge.Client;
using BenchForge.Contracts;
using BenchForge.Flight;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using Starlark.Eval;
using UnitsNet;

namespace BenchForge.Procedures;

/// <summary>
/// Declarative Starlark bench and scripted-mock loader powered by <c>starlark-cs</c>.
/// Allows engineers and MBSE workflows to declare final fail-safe instrument maximums (<c>hard_limit(...)</c>)
/// and rich, stateful, sandboxed instrument mocks (<c>mock_psu</c>, <c>mock_dmm</c>, <c>mock_thermal</c>,
/// <c>mock_scpi</c>, etc.) in concise Starlark (<c>.star</c>) files.
/// </summary>
public static class StarlarkBenchLoader
{
    /// <summary>
    /// Evaluate a Starlark declarative bench script and apply its registered fail-safe hard limits and
    /// scripted mocks onto <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">Target bench builder.</param>
    /// <param name="starlarkSource">Starlark (<c>.star</c>) source text declaring limits and/or scripted mocks.</param>
    /// <param name="filename">Logical filename for diagnostics.</param>
    /// <param name="limits">Optional step, memory, and timeout budgets for the Starlark sandbox.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing any custom <see cref="StarlarkScriptedScpiSession"/> mocks keyed by role.</returns>
    public static BenchResult<IReadOnlyDictionary<string, StarlarkScriptedScpiSession>> ApplyTo(
        BenchBuilder builder,
        string starlarkSource,
        string filename = "bench.star",
        StarlarkProcedureLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(starlarkSource))
            return BenchResult<IReadOnlyDictionary<string, StarlarkScriptedScpiSession>>.Fail(
                BenchError.Validation("BF-STARBENCH-EMPTY", "Starlark bench declaration must not be empty."));

        var budget = limits ?? new StarlarkProcedureLimits();
        budget.Validate();

        var interp = new Interpreter
        {
            Dialect = ProcedureParser.ProcedureDialect,
            MaxSteps = budget.MaxSteps,
            MaxAllocatedBytes = budget.MaxAllocatedBytes,
            Timeout = budget.Timeout ?? TimeSpan.FromSeconds(5),
            Cancellation = cancellationToken
        };

        var scpiMocks = new Dictionary<string, StarlarkScriptedScpiSession>(StringComparer.OrdinalIgnoreCase);
        var pendingLimits = new List<(string Role, InstrumentHardLimits Limits)>();

        interp.Register("hard_limit", (
            string role,
            double? max_v = null,
            double? min_v = null,
            double? max_i = null,
            double? min_i = null,
            double? max_w = null,
            double? max_dbm = null,
            double? min_freq_hz = null,
            double? max_freq_hz = null,
            double? min_temp_c = null,
            double? max_temp_c = null,
            double? min_ohm = null,
            double? max_ohm = null,
            bool require_compliance = true,
            bool program_hw_prot = true,
            double hw_margin = 1.0,
            bool auto_safe = false,
            string? note = null) =>
        {
            Power? effectivePower = max_w is { } w ? Power.FromWatts((decimal)w) : null;
            if (max_dbm is { } dbm)
            {
                var dbmPower = Power.FromWatts((decimal)Math.Pow(10.0, (dbm - 30.0) / 10.0));
                effectivePower = effectivePower is null || dbmPower.Watts < effectivePower.Value.Watts ? dbmPower : effectivePower;
            }

            var hard = new InstrumentHardLimits(
                MaxVoltage: max_v is { } mv ? ElectricPotential.FromVolts(mv) : null,
                MinVoltage: min_v is { } nv ? ElectricPotential.FromVolts(nv) : null,
                MaxCurrent: max_i is { } mi ? ElectricCurrent.FromAmperes(mi) : null,
                MinCurrent: min_i is { } ni ? ElectricCurrent.FromAmperes(ni) : null,
                MaxPower: effectivePower,
                MinFrequency: min_freq_hz is { } nf ? Frequency.FromHertz(nf) : null,
                MaxFrequency: max_freq_hz is { } mf ? Frequency.FromHertz(mf) : null,
                MinTemperature: min_temp_c is { } nt ? Temperature.FromDegreesCelsius(nt) : null,
                MaxTemperature: max_temp_c is { } mt ? Temperature.FromDegreesCelsius(mt) : null,
                MinResistance: min_ohm is { } nr ? ElectricResistance.FromOhms(nr) : null,
                MaxResistance: max_ohm is { } mr ? ElectricResistance.FromOhms(mr) : null,
                RequireComplianceBeforeOutputOn: require_compliance,
                ProgramHardwareProtection: program_hw_prot,
                HardwareProtectionMarginFactor: hw_margin,
                AutoSafeOnViolation: auto_safe,
                PolicyNote: note);
            hard.Validate();
            pendingLimits.Add((role, hard));
        });

        interp.Register("require_hard_limits_for_physical", (bool enabled = true) =>
        {
            builder.RequireHardLimitsForPhysical(enabled);
        });

        interp.Register("mock_psu", (string role, double load_ohm = 10.0, double default_i_lim = 0.1, ICallable? load_fn = null) =>
        {
            if (load_fn is null)
                builder.AddMockSmu(role, load_ohm, default_i_lim);
            else
                builder.AddInstrument(new StarlarkScriptedSmu(role, interp, load_fn, default_i_lim));
        });

        interp.Register("mock_dmm", (string role, double nominal_v = 0.0, double nominal_ohm = 100.0, ICallable? measure_v_fn = null) =>
        {
            if (measure_v_fn is null)
                builder.AddMockDmm(role, nominal_v, nominal_ohm);
            else
                builder.AddInstrument(new StarlarkScriptedDmm(role, interp, measure_v_fn, nominal_ohm));
        });

        interp.Register("mock_thermal", (string role, double initial_c = 25.0, ICallable? step_fn = null) =>
        {
            if (step_fn is null)
                builder.AddMockThermal(role, initial_c);
            else
                builder.AddInstrument(new StarlarkScriptedThermalController(role, interp, step_fn, initial_c));
        });

        interp.Register("mock_scpi", (string role, ICallable handler_fn) =>
        {
            scpiMocks[role] = new StarlarkScriptedScpiSession(role, interp, handler_fn);
        });

        interp.Register("mock_fgen", (string role) => builder.AddMockFunctionGenerator(role));
        interp.Register("mock_scope", (string role, double dc_v = 3.3, double ripple_v = 0.008) => builder.AddMockOscilloscope(role, dc_v, ripple_v));
        interp.Register("mock_switch", (string role) => builder.AddMockSwitchMatrix(role));
        interp.Register("mock_eload", (string role, double bus_v = 28.0) => builder.AddMockElectronicLoad(role, bus_v));
        interp.Register("mock_rf_siggen", (string role) => builder.AddMockRfSignalGenerator(role));
        interp.Register("mock_specan", (string role) => builder.AddMockSpectrumAnalyzer(role));
        interp.Register("mock_rf_power_meter", (string role, double watts = 0.001) => builder.AddMockRfPowerMeter(role, (decimal)watts));
        interp.Register("mock_counter", (string role, double freq_hz = 10_000_000.0) => builder.AddMockFrequencyCounter(role, freq_hz));
        interp.Register("mock_lcr", (string role, double cap_f = 1e-6, double esr_ohm = 0.05) => builder.AddMockLcrMeter(role, cap_f, esr_ohm));

        try
        {
            _ = interp.Execute(filename, starlarkSource);
            foreach (var (role, hard) in pendingLimits)
                builder.WithHardLimits(role, hard);
        }
        catch (BenchOperationException ex)
        {
            return BenchResult<IReadOnlyDictionary<string, StarlarkScriptedScpiSession>>.Fail(ex.Error);
        }
        catch (Exception ex)
        {
            return BenchResult<IReadOnlyDictionary<string, StarlarkScriptedScpiSession>>.Fail(
                BenchError.Validation("BF-STARBENCH-SCRIPT", $"Starlark bench declaration failed: {ex.Message}"));
        }

        return BenchResult<IReadOnlyDictionary<string, StarlarkScriptedScpiSession>>.Ok(scpiMocks);
    }

    /// <summary>Build a sealed <see cref="BenchSession"/> directly from a Starlark declarative bench script.</summary>
    /// <param name="benchName">Logical bench name.</param>
    /// <param name="starlarkSource">Starlark (<c>.star</c>) source declaring hard limits and scripted mocks.</param>
    /// <param name="filename">Logical filename for diagnostics.</param>
    /// <param name="limits">Optional Starlark execution budgets.</param>
    /// <param name="clock">Optional <see cref="TimeProvider"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing the built <see cref="BenchSession"/>.</returns>
    public static BenchResult<BenchSession> BuildBench(
        string benchName,
        string starlarkSource,
        string filename = "bench.star",
        StarlarkProcedureLimits? limits = null,
        TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        var builder = BenchSession.Create(benchName, clock);
        var apply = ApplyTo(builder, starlarkSource, filename, limits, cancellationToken);
        if (apply.IsFailure)
            return BenchResult<BenchSession>.Fail(apply.Error);

        try
        {
            return BenchResult<BenchSession>.Ok(builder.Build());
        }
        catch (BenchOperationException ex)
        {
            return BenchResult<BenchSession>.Fail(ex.Error);
        }
    }

    internal static double ToDouble(object? value, string context) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        BigInteger bi => (double)bi,
        decimal m => (double)m,
        _ => throw new InvalidOperationException($"Starlark callback '{context}' must return a number, got '{value?.GetType().Name ?? "None"}'.")
    };
}

/// <summary>
/// Starlark-scripted stateful SMU/PSU mock implementing <see cref="IBenchSourceMeasureUnit"/> and <see cref="IBenchPowerSupply"/>.
/// Invokes a sandboxed Starlark callback <c>load_fn(voltage_v, current_limit_a, output_on, step)</c> to compute
/// dynamic load current (supporting nonlinear spacecraft loads, inrush transients, and scripted faults).
/// </summary>
public sealed class StarlarkScriptedSmu : IBenchSourceMeasureUnit
{
    private readonly Interpreter _interp;
    private readonly ICallable _loadFn;
    private readonly Lock _gate = new();
    private double _voltageV;
    private double _currentLimitA;
    private bool _outputEnabled;
    private long _step;

    /// <summary>Create a Starlark-scripted SMU/PSU mock.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="interpreter">Bounded Starlark interpreter hosting <paramref name="loadFunction"/>.</param>
    /// <param name="loadFunction">Starlark callback <c>def fn(voltage_v, current_limit_a, output_on, step) -&gt; current_a</c>.</param>
    /// <param name="defaultCurrentLimitA">Initial current limit in amperes.</param>
    public StarlarkScriptedSmu(string role, Interpreter interpreter, ICallable loadFunction, double defaultCurrentLimitA = 0.1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _interp = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _loadFn = loadFunction ?? throw new ArgumentNullException(nameof(loadFunction));
        _currentLimitA = defaultCurrentLimitA;
        Identity = new InstrumentIdentity("BenchForge-Starlark", "StarlarkScriptedSmu", $"STAR-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration => null;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "SMU", Identity, Backend, ["SetVoltage", "GetVoltage", "SetCurrentLimit", "GetCurrentLimit", "MeasureVoltage", "MeasureCurrent", "SetOutputEnabled", "IsComplianceTripped", "SafeState", "StarlarkMock"]);

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
        {
            _outputEnabled = false;
            _voltageV = 0.0;
        }
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetVoltageAsync(ElectricPotential voltage, int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) _voltageV = voltage.Volts;
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetVoltageAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) return Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(_voltageV)));
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetCurrentLimitAsync(ElectricCurrent currentLimit, int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) _currentLimitA = currentLimit.Amperes;
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> GetCurrentLimitAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) return Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(_currentLimitA)));
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope())
            return Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(_outputEnabled ? _voltageV : 0.0)));
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        try
        {
            using (_gate.EnterScope())
            {
                _step++;
                var raw = _interp.CallValue(_loadFn, [Interop.ToStar(_voltageV), Interop.ToStar(_currentLimitA), Interop.ToStar(_outputEnabled), Interop.ToStar(_step)]);
                double amps = StarlarkBenchLoader.ToDouble(raw, $"{Role}.load_fn");
                return Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.FromAmperes(amps)));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(BenchResult<ElectricCurrent>.Fail(new BenchError("BF-STARMOCK-ERR", BenchErrorKind.ProtocolFault, ex.Message, Role, Remedy: "Check Starlark mock load_fn return value and budgets.")));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) _outputEnabled = enabled;
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult<bool>> GetOutputEnabledAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) return Task.FromResult(BenchResult<bool>.Ok(_outputEnabled));
    }

    /// <inheritdoc/>
    public async Task<BenchResult<bool>> IsComplianceTrippedAsync(int channel = 1, CancellationToken cancellationToken = default)
    {
        var cur = await MeasureCurrentAsync(channel, cancellationToken).ConfigureAwait(false);
        if (cur.IsFailure) return BenchResult<bool>.Fail(cur.Error);
        double lim;
        using (_gate.EnterScope()) lim = _currentLimitA;
        return BenchResult<bool>.Ok(Math.Abs(cur.Value.Amperes) >= lim - 1e-9);
    }
}

/// <summary>
/// Starlark-scripted digital multimeter mock implementing <see cref="IBenchDigitalMultimeter"/>.
/// Invokes <c>measure_v_fn(step)</c> on each DC voltage measurement.
/// </summary>
public sealed class StarlarkScriptedDmm : IBenchDigitalMultimeter
{
    private readonly Interpreter _interp;
    private readonly ICallable _measureVFn;
    private readonly double _nominalOhm;
    private readonly Lock _gate = new();
    private long _step;

    /// <summary>Create a Starlark-scripted DMM mock.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="interpreter">Bounded Starlark interpreter.</param>
    /// <param name="measureVoltageFn">Starlark callback <c>def fn(step) -&gt; volts</c>.</param>
    /// <param name="nominalResistanceOhm">Default resistance reading in ohms.</param>
    public StarlarkScriptedDmm(string role, Interpreter interpreter, ICallable measureVoltageFn, double nominalResistanceOhm = 100.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _interp = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _measureVFn = measureVoltageFn ?? throw new ArgumentNullException(nameof(measureVoltageFn));
        _nominalOhm = nominalResistanceOhm;
        Identity = new InstrumentIdentity("BenchForge-Starlark", "StarlarkScriptedDmm", $"STAR-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration => null;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "DMM", Identity, Backend, ["MeasureDcVoltage", "MeasureDcCurrent", "MeasureResistance", "SafeState", "StarlarkMock"]);
    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(BenchResult.Ok());

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureDcVoltageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using (_gate.EnterScope())
            {
                _step++;
                var raw = _interp.CallValue(_measureVFn, [Interop.ToStar(_step)]);
                return Task.FromResult(BenchResult<ElectricPotential>.Ok(ElectricPotential.FromVolts(StarlarkBenchLoader.ToDouble(raw, $"{Role}.measure_v_fn"))));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(BenchResult<ElectricPotential>.Fail(new BenchError("BF-STARMOCK-ERR", BenchErrorKind.ProtocolFault, ex.Message, Role)));
        }
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureDcCurrentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(BenchResult<ElectricCurrent>.Ok(ElectricCurrent.Zero));

    /// <inheritdoc/>
    public Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(BenchResult<ElectricResistance>.Ok(ElectricResistance.FromOhms(_nominalOhm)));
}

/// <summary>
/// Starlark-scripted thermal chamber mock implementing <see cref="IBenchThermalController"/>.
/// Invokes <c>step_fn(setpoint_c, current_c, step)</c> on each temperature reading to model thermal lag or soak curves.
/// </summary>
public sealed class StarlarkScriptedThermalController : IBenchThermalController
{
    private readonly Interpreter _interp;
    private readonly ICallable _stepFn;
    private readonly Lock _gate = new();
    private double _setpointC;
    private double _currentC;
    private long _step;

    /// <summary>Create a Starlark-scripted thermal controller mock.</summary>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="interpreter">Bounded Starlark interpreter.</param>
    /// <param name="stepFn">Starlark callback <c>def fn(setpoint_c, current_c, step) -&gt; new_temp_c</c>.</param>
    /// <param name="initialTempC">Initial chamber temperature in Celsius.</param>
    public StarlarkScriptedThermalController(string role, Interpreter interpreter, ICallable stepFn, double initialTempC = 25.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _interp = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _stepFn = stepFn ?? throw new ArgumentNullException(nameof(stepFn));
        _setpointC = initialTempC;
        _currentC = initialTempC;
        Identity = new InstrumentIdentity("BenchForge-Starlark", "StarlarkScriptedThermal", $"STAR-{role}", "1.0");
    }

    /// <inheritdoc/>
    public string Role { get; }
    /// <inheritdoc/>
    public InstrumentIdentity Identity { get; }
    /// <inheritdoc/>
    public InstrumentBackend Backend => InstrumentBackend.Mock;
    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration => null;
    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() =>
        new(Role, "THERMAL", Identity, Backend, ["SetSetpoint", "MeasureTemperature", "SafeState", "StarlarkMock"]);

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) _setpointC = 25.0;
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult> SetSetpointAsync(Temperature target, CancellationToken cancellationToken = default)
    {
        using (_gate.EnterScope()) _setpointC = target.DegreesCelsius;
        return Task.FromResult(BenchResult.Ok());
    }

    /// <inheritdoc/>
    public Task<BenchResult<Temperature>> MeasureTemperatureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using (_gate.EnterScope())
            {
                _step++;
                var raw = _interp.CallValue(_stepFn, [Interop.ToStar(_setpointC), Interop.ToStar(_currentC), Interop.ToStar(_step)]);
                _currentC = StarlarkBenchLoader.ToDouble(raw, $"{Role}.step_fn");
                return Task.FromResult(BenchResult<Temperature>.Ok(Temperature.FromDegreesCelsius(_currentC)));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(BenchResult<Temperature>.Fail(new BenchError("BF-STARMOCK-ERR", BenchErrorKind.ProtocolFault, ex.Message, Role)));
        }
    }
}

/// <summary>
/// Starlark-scripted SCPI session implementing <see cref="IAsyncScpiSession"/> so any custom SCPI instrument
/// (including all 81 generated BenchForge SCPI drivers!) can be backed by a concise Starlark handler
/// <c>def handler(command, is_query, state)</c>.
/// </summary>
public sealed class StarlarkScriptedScpiSession : IAsyncScpiSession
{
    private readonly Interpreter _interp;
    private readonly ICallable _handlerFn;
    private readonly StarDict _state = new();
    private readonly Lock _gate = new();

    /// <summary>Create a Starlark-scripted SCPI mock session.</summary>
    /// <param name="role">Logical instrument role identifier.</param>
    /// <param name="interpreter">Bounded Starlark interpreter.</param>
    /// <param name="handlerFn">Starlark callback <c>def handler(command, is_query, state) -&gt; str | None</c>.</param>
    public StarlarkScriptedScpiSession(string role, Interpreter interpreter, ICallable handlerFn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        _interp = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _handlerFn = handlerFn ?? throw new ArgumentNullException(nameof(handlerFn));
    }

    /// <summary>Logical instrument role identifier.</summary>
    public string Role { get; }

    /// <inheritdoc/>
    public ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.EnterScope())
        {
            _ = _interp.CallValue(_handlerFn, [Interop.ToStar(command), Interop.ToStar(false), _state]);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.EnterScope())
        {
            var reply = _interp.CallValue(_handlerFn, [Interop.ToStar(command), Interop.ToStar(true), _state]);
            string text = reply switch
            {
                null => string.Empty,
                string s => s,
                double d => d.ToString("G17", CultureInfo.InvariantCulture),
                BigInteger bi => bi.ToString(CultureInfo.InvariantCulture),
                _ => reply.ToString() ?? string.Empty
            };
            return ValueTask.FromResult(text);
        }
    }
}
