using System.Text.Json;
using BenchForge.Flight;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using BenchForge.Simulation;
using UnitsNet;

namespace BenchForge.Client;

/// <summary>
/// Ergonomic, strongly typed, railroad-oriented bench session designed to make writing flight-hardware test clients
/// effortless for both human engineers and AI coding agents.
/// </summary>
public sealed class BenchSession : IAsyncDisposable
{
    private readonly Dictionary<string, IBenchInstrument> _instruments;
    private readonly HardLimitRegistry _hardLimits;
    private readonly AdjustableSimulationClock _simClock;
    private readonly SimulationScheduler _scheduler;
    private readonly FlightRecorder _recorder;
    private readonly DigitalTwinPlant? _digitalTwin;
    private readonly List<FlightTestStepRecord> _steps = new();
    private readonly Lock _stepGate = new();
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    private readonly TimeProvider _clock;
    private bool _disposed;

    internal BenchSession(
        string benchName,
        Dictionary<string, IBenchInstrument> instruments,
        TimeProvider clock,
        HardLimitRegistry? hardLimits = null,
        AdjustableSimulationClock? simClock = null,
        DigitalTwinPlant? digitalTwin = null)
    {
        BenchName = benchName;
        _instruments = instruments;
        _clock = clock;
        _hardLimits = hardLimits ?? new HardLimitRegistry();
        _hardLimits.Seal();
        _simClock = simClock ?? new AdjustableSimulationClock(clock);
        _scheduler = new SimulationScheduler(_simClock);
        _recorder = new FlightRecorder(benchName, _simClock, clock);
        _digitalTwin = digitalTwin;
    }

    /// <summary>Logical name of this bench session.</summary>
    public string BenchName { get; }

    /// <summary>Sealed fail-safe hard-limit registry enforced across all registered instruments (tighten-only).</summary>
    public HardLimitRegistry HardLimits => _hardLimits;

    /// <summary>Canonical run-scoped virtual simulation clock shared by time-aware mocks and digital-twin plants.</summary>
    public AdjustableSimulationClock SimulationClock => _simClock;

    /// <summary>Explicitly pumped virtual event scheduler bound to <see cref="SimulationClock"/>.</summary>
    public SimulationScheduler Scheduler => _scheduler;

    /// <summary>Deterministic SHA-256 hash-chained flight recorder attached to this bench session.</summary>
    public FlightRecorder Recorder => _recorder;

    /// <summary>Optional coupled closed-loop spacecraft UUT digital twin backing coupled bench mocks.</summary>
    public DigitalTwinPlant? DigitalTwin => _digitalTwin;

    /// <summary>
    /// Advance the canonical <see cref="SimulationClock"/> by <paramref name="delta"/>, propagate the time step to
    /// the coupled <see cref="DigitalTwin"/> and all <see cref="ISimulationTimeAware"/> instruments, and log the step
    /// into <see cref="Recorder"/>.
    /// </summary>
    /// <param name="delta">Non-negative logical simulation duration to advance.</param>
    /// <returns>Railroad outcome containing the updated <see cref="SimulationClockStatus"/>.</returns>
    public BenchResult<SimulationClockStatus> AdvanceSimulationTime(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
            return BenchResult<SimulationClockStatus>.Fail(BenchError.Validation("BF-TIME-NEG", "Simulation time delta must be non-negative."));

        try
        {
            var status = _simClock.Status();
            if (status.Mode is SimulationClockMode.RealTime or SimulationClockMode.Accelerated or SimulationClockMode.Slowed)
                _simClock.Pause();
            if (delta > TimeSpan.Zero)
                _simClock.AdvanceBy(delta);
            var updated = _simClock.Status();

            if (_digitalTwin is not null)
                _digitalTwin.OnSimulationTimeAdvanced(updated.Elapsed, delta);

            foreach (var inst in _instruments.Values.OrderBy(i => i.Role, StringComparer.Ordinal))
            {
                if (inst is ISimulationTimeAware timeAware)
                {
                    // Avoid double-stepping a DigitalTwinPlant already stepped above.
                    if (_digitalTwin is not null && inst is CoupledTwinPowerSupply or CoupledTwinThermalController)
                        continue;
                    if (_digitalTwin is not null && inst is HardLimitedPowerSupply or HardLimitedThermalController)
                        continue;
                    timeAware.OnSimulationTimeAdvanced(updated.Elapsed, delta);
                }
            }

            _recorder.Record("bench", "AdvanceSimulationTime", "TimeAdvance", $"dt={delta.TotalSeconds:G6}s, elapsed={updated.Elapsed.TotalSeconds:G6}s");
            return BenchResult<SimulationClockStatus>.Ok(updated);
        }
        catch (Exception ex)
        {
            return BenchResult<SimulationClockStatus>.Fail(BenchError.Validation("BF-TIME-ERR", ex.Message));
        }
    }

    /// <summary>Begin building a configured local, simulated, or hybrid bench session.</summary>
    /// <param name="benchName">Logical bench or test-stand name.</param>
    /// <param name="clock">Optional <see cref="TimeProvider"/> for calibration and timestamp checks.</param>
    /// <returns>Fluent <see cref="BenchBuilder"/>.</returns>
    public static BenchBuilder Create(string benchName, TimeProvider? clock = null) =>
        new(benchName, clock ?? TimeProvider.System);

    /// <summary>Return machine-readable descriptors for every registered instrument role.</summary>
    /// <returns>Ordered array of instrument descriptors.</returns>
    public IReadOnlyList<BenchInstrumentDescriptor> Describe() =>
        _instruments.Values.OrderBy(i => i.Role, StringComparer.Ordinal).Select(i => i.Describe()).ToArray();

    /// <summary>
    /// Serialize the full bench inventory, capabilities, provenance, calibration metadata, simulation clock status,
    /// and registered fail-safe hard limits as indented JSON so human operators and AI agents can discover every role and ceiling without guessing.
    /// </summary>
    /// <returns>JSON manifest string.</returns>
    public string DescribeJson()
    {
        var clockStatus = _simClock.Status();
        return JsonSerializer.Serialize(
            new
            {
                benchName = BenchName,
                simulationClock = new
                {
                    mode = clockStatus.Mode.ToString(),
                    speed = clockStatus.Speed,
                    elapsedSeconds = clockStatus.Elapsed.TotalSeconds
                },
                hasDigitalTwin = _digitalTwin is not null,
                instruments = Describe(),
                hardLimits = _hardLimits.Snapshot().ToDictionary(
                    kv => kv.Key,
                    kv => new
                    {
                        maxVoltageV = kv.Value.MaxVoltage?.Volts,
                        minVoltageV = kv.Value.MinVoltage?.Volts,
                        maxCurrentA = kv.Value.MaxCurrent?.Amperes,
                        minCurrentA = kv.Value.MinCurrent?.Amperes,
                        maxPowerW = kv.Value.MaxPower?.Watts,
                        minFrequencyHz = kv.Value.MinFrequency?.Hertz,
                        maxFrequencyHz = kv.Value.MaxFrequency?.Hertz,
                        minTemperatureC = kv.Value.MinTemperature?.DegreesCelsius,
                        maxTemperatureC = kv.Value.MaxTemperature?.DegreesCelsius,
                        minResistanceOhm = kv.Value.MinResistance?.Ohms,
                        maxResistanceOhm = kv.Value.MaxResistance?.Ohms,
                        requireComplianceBeforeOutputOn = kv.Value.RequireComplianceBeforeOutputOn,
                        programHardwareProtection = kv.Value.ProgramHardwareProtection,
                        autoSafeOnViolation = kv.Value.AutoSafeOnViolation,
                        policyNote = kv.Value.PolicyNote
                    },
                    StringComparer.OrdinalIgnoreCase)
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Monotonically tighten the registered hard limits for <paramref name="role"/> at runtime.
    /// Existing limits can only be narrowed, never raised or removed.
    /// </summary>
    /// <param name="role">Registered instrument role.</param>
    /// <param name="stricter">Stricter ceiling constraints to intersect with existing limits.</param>
    /// <returns>Railroad result containing the new effective limits, or a lookup/validation error.</returns>
    public BenchResult<InstrumentHardLimits> TryTightenHardLimits(string role, InstrumentHardLimits stricter)
    {
        if (!_instruments.ContainsKey(role))
            return BenchResult<InstrumentHardLimits>.Fail(new BenchError("BF-ROLE-MISSING", BenchErrorKind.ResourceNotFound, $"Instrument role '{role}' is not registered on bench '{BenchName}'.", role));
        return _hardLimits.TryTighten(role, stricter);
    }

    /// <summary>
    /// Program registered fail-safe maximums into instrument-resident hardware protection registers (OVP/OCP/OPP)
    /// for every role with registered hard limits, recording honest hardware-protection provenance in the evidence trail.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing one <see cref="HardwareProtectionReport"/> per limited instrument role.</returns>
    public async Task<BenchResult<IReadOnlyList<HardwareProtectionReport>>> ProgramAllHardwareLimitsAsync(
        CancellationToken cancellationToken = default)
    {
        var reports = new List<HardwareProtectionReport>();
        foreach (var (role, limits) in _hardLimits.Snapshot().OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!limits.ProgramHardwareProtection) continue;
            if (!_instruments.TryGetValue(role, out var inst)) continue;

            if (inst is IHardwareLimitProgrammable programmable)
            {
                var res = await programmable.ProgramHardwareLimitsAsync(limits, 1, cancellationToken).ConfigureAwait(false);
                if (res.IsFailure)
                {
                    RecordStep($"ProgramHardwareLimits({role})", false, res.Error.Message, res.Error);
                    return BenchResult<IReadOnlyList<HardwareProtectionReport>>.Fail(res.Error);
                }
                reports.Add(res.Value);
                RecordStep($"ProgramHardwareLimits({role})", true, res.Value.Notes);
            }
            else
            {
                var report = new HardwareProtectionReport(
                    role,
                    HardwareProtectionSupported: false,
                    OvpProgrammed: false,
                    OvpThreshold: null,
                    OcpProgrammed: false,
                    OcpThreshold: null,
                    OppProgrammed: false,
                    OppThreshold: null,
                    HardwareProtectionVerifiedOnKillTest: false,
                    Notes: $"Instrument '{role}' ({inst.Describe().InstrumentClass}) has no hardware protection register interface; software hard limits enforced only while host runs.");
                reports.Add(report);
                RecordStep($"ProgramHardwareLimits({role})", true, report.Notes);
            }
        }
        return BenchResult<IReadOnlyList<HardwareProtectionReport>>.Ok(reports);
    }

    /// <summary>Verify that all instruments with declared calibration certificates are current before running flight tests.</summary>
    /// <param name="requireCalibrationForPhysical">When true, physical instruments without calibration metadata also fail preflight.</param>
    /// <returns>Railroad result passing when all instruments are calibrated and current.</returns>
    public BenchResult PreflightCalibration(bool requireCalibrationForPhysical = true)
    {
        var now = _clock.GetUtcNow();
        foreach (var inst in _instruments.Values.OrderBy(i => i.Role, StringComparer.Ordinal))
        {
            if (inst.Calibration is null)
            {
                if (requireCalibrationForPhysical && inst.Backend == Contracts.InstrumentBackend.Physical)
                {
                    return BenchResult.Fail(new BenchError(
                        "BF-CAL-MISSING",
                        BenchErrorKind.CalibrationExpired,
                        $"Physical instrument '{inst.Role}' ({inst.Identity.Model}) has no calibration record.",
                        inst.Role,
                        Remedy: "Attach a current InstrumentCalibrationInfo certificate before running flight tests."));
                }
                continue;
            }

            if (!inst.Calibration.IsCurrent(now))
            {
                return BenchResult.Fail(new BenchError(
                    "BF-CAL-EXPIRED",
                    BenchErrorKind.CalibrationExpired,
                    $"Calibration '{inst.Calibration.CertificateId}' on '{inst.Role}' expired on {inst.Calibration.DueUtc:O}.",
                    inst.Role,
                    Remedy: $"Recalibrate '{inst.Role}' or swap in a calibrated unit before testing flight hardware."));
            }
        }
        return BenchResult.Ok();
    }

    /// <summary>Resolve a registered digital multimeter by role using railroad error handling.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing the <see cref="IBenchDigitalMultimeter"/> or a structured lookup error.</returns>
    public BenchResult<IBenchDigitalMultimeter> TryGetDmm(string role) => Resolve<IBenchDigitalMultimeter>(role, "DMM");

    /// <summary>Resolve a registered DC power supply (or SMU) by role using railroad error handling.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing the <see cref="IBenchPowerSupply"/> or a structured lookup error.</returns>
    public BenchResult<IBenchPowerSupply> TryGetPowerSupply(string role) => Resolve<IBenchPowerSupply>(role, "PSU");

    /// <summary>Resolve a registered SMU by role using railroad error handling.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing the <see cref="IBenchSourceMeasureUnit"/> or a structured lookup error.</returns>
    public BenchResult<IBenchSourceMeasureUnit> TryGetSmu(string role) => Resolve<IBenchSourceMeasureUnit>(role, "SMU");

    /// <summary>Resolve a registered thermal controller by role using railroad error handling.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing the <see cref="IBenchThermalController"/> or a structured lookup error.</returns>
    public BenchResult<IBenchThermalController> TryGetThermal(string role) => Resolve<IBenchThermalController>(role, "THERMAL");

    /// <summary>Resolve a registered function generator by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchFunctionGenerator"/>.</returns>
    public BenchResult<IBenchFunctionGenerator> TryGetFunctionGenerator(string role) => Resolve<IBenchFunctionGenerator>(role, "FGEN");

    /// <summary>Resolve a registered oscilloscope by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchOscilloscope"/>.</returns>
    public BenchResult<IBenchOscilloscope> TryGetOscilloscope(string role) => Resolve<IBenchOscilloscope>(role, "SCOPE");

    /// <summary>Resolve a registered switch matrix by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchSwitchMatrix"/>.</returns>
    public BenchResult<IBenchSwitchMatrix> TryGetSwitchMatrix(string role) => Resolve<IBenchSwitchMatrix>(role, "SWITCH");

    /// <summary>Resolve a registered programmable electronic load by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchElectronicLoad"/>.</returns>
    public BenchResult<IBenchElectronicLoad> TryGetElectronicLoad(string role) => Resolve<IBenchElectronicLoad>(role, "ELOAD");

    /// <summary>Resolve a registered RF signal generator by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchRfSignalGenerator"/>.</returns>
    public BenchResult<IBenchRfSignalGenerator> TryGetRfSignalGenerator(string role) => Resolve<IBenchRfSignalGenerator>(role, "RFSIGGEN");

    /// <summary>Resolve a registered spectrum analyzer by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchSpectrumAnalyzer"/>.</returns>
    public BenchResult<IBenchSpectrumAnalyzer> TryGetSpectrumAnalyzer(string role) => Resolve<IBenchSpectrumAnalyzer>(role, "SPECAN");

    /// <summary>Resolve a registered RF power meter by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchRfPowerMeter"/>.</returns>
    public BenchResult<IBenchRfPowerMeter> TryGetRfPowerMeter(string role) => Resolve<IBenchRfPowerMeter>(role, "PWRMETER");

    /// <summary>Resolve a registered frequency counter by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchFrequencyCounter"/>.</returns>
    public BenchResult<IBenchFrequencyCounter> TryGetFrequencyCounter(string role) => Resolve<IBenchFrequencyCounter>(role, "COUNTER");

    /// <summary>Resolve a registered LCR meter by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchLcrMeter"/>.</returns>
    public BenchResult<IBenchLcrMeter> TryGetLcrMeter(string role) => Resolve<IBenchLcrMeter>(role, "LCR");

    /// <summary>Resolve a registered Vector Network Analyzer by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchVectorNetworkAnalyzer"/>.</returns>
    public BenchResult<IBenchVectorNetworkAnalyzer> TryGetVna(string role) => Resolve<IBenchVectorNetworkAnalyzer>(role, "VNA");

    /// <summary>Resolve a registered multiplexed DAQ by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchDataAcquisition"/>.</returns>
    public BenchResult<IBenchDataAcquisition> TryGetDaq(string role) => Resolve<IBenchDataAcquisition>(role, "DAQ");

    /// <summary>Resolve a registered Digital I/O controller by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchDigitalIo"/>.</returns>
    public BenchResult<IBenchDigitalIo> TryGetDigitalIo(string role) => Resolve<IBenchDigitalIo>(role, "DIO");

    /// <summary>Resolve a registered safety interlock / watchdog loop by role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing <see cref="IBenchSafetyInterlock"/>.</returns>
    public BenchResult<IBenchSafetyInterlock> TryGetSafetyInterlock(string role) => Resolve<IBenchSafetyInterlock>(role, "INTERLOCK");

    /// <summary>Resolve the test-only <see cref="IBenchMockControl"/> handle for a simulated instrument role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Railroad result containing the mock control interface, or <see cref="BenchErrorKind.UnsupportedCapability"/> for physical devices.</returns>
    public BenchResult<IBenchMockControl> TryGetMockControl(string role) => Resolve<IBenchMockControl>(role, "MockControl");

    /// <summary>Convenience accessor for a DMM role (throws <see cref="BenchOperationException"/> if missing or mismatched).</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchDigitalMultimeter"/>.</returns>
    public IBenchDigitalMultimeter Dmm(string role) => TryGetDmm(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a power supply or SMU role (throws <see cref="BenchOperationException"/> if missing or mismatched).</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchPowerSupply"/>.</returns>
    public IBenchPowerSupply PowerSupply(string role) => TryGetPowerSupply(role).GetValueOrThrow();

    /// <summary>Convenience accessor for an SMU role (throws <see cref="BenchOperationException"/> if missing or mismatched).</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchSourceMeasureUnit"/>.</returns>
    public IBenchSourceMeasureUnit Smu(string role) => TryGetSmu(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a thermal controller role (throws <see cref="BenchOperationException"/> if missing or mismatched).</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchThermalController"/>.</returns>
    public IBenchThermalController Thermal(string role) => TryGetThermal(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a function generator role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchFunctionGenerator"/>.</returns>
    public IBenchFunctionGenerator FunctionGenerator(string role) => TryGetFunctionGenerator(role).GetValueOrThrow();

    /// <summary>Convenience accessor for an oscilloscope role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchOscilloscope"/>.</returns>
    public IBenchOscilloscope Oscilloscope(string role) => TryGetOscilloscope(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a switch matrix role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchSwitchMatrix"/>.</returns>
    public IBenchSwitchMatrix SwitchMatrix(string role) => TryGetSwitchMatrix(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a programmable electronic load role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchElectronicLoad"/>.</returns>
    public IBenchElectronicLoad ElectronicLoad(string role) => TryGetElectronicLoad(role).GetValueOrThrow();

    /// <summary>Convenience accessor for an RF signal generator role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchRfSignalGenerator"/>.</returns>
    public IBenchRfSignalGenerator RfSignalGenerator(string role) => TryGetRfSignalGenerator(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a spectrum analyzer role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchSpectrumAnalyzer"/>.</returns>
    public IBenchSpectrumAnalyzer SpectrumAnalyzer(string role) => TryGetSpectrumAnalyzer(role).GetValueOrThrow();

    /// <summary>Convenience accessor for an RF power meter role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchRfPowerMeter"/>.</returns>
    public IBenchRfPowerMeter RfPowerMeter(string role) => TryGetRfPowerMeter(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a frequency counter role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchFrequencyCounter"/>.</returns>
    public IBenchFrequencyCounter FrequencyCounter(string role) => TryGetFrequencyCounter(role).GetValueOrThrow();

    /// <summary>Convenience accessor for an LCR meter role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchLcrMeter"/>.</returns>
    public IBenchLcrMeter LcrMeter(string role) => TryGetLcrMeter(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a Vector Network Analyzer role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchVectorNetworkAnalyzer"/>.</returns>
    public IBenchVectorNetworkAnalyzer Vna(string role) => TryGetVna(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a multiplexed DAQ role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchDataAcquisition"/>.</returns>
    public IBenchDataAcquisition Daq(string role) => TryGetDaq(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a Digital I/O controller role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchDigitalIo"/>.</returns>
    public IBenchDigitalIo DigitalIo(string role) => TryGetDigitalIo(role).GetValueOrThrow();

    /// <summary>Convenience accessor for a safety interlock / watchdog loop role.</summary>
    /// <param name="role">Logical bench role name.</param>
    /// <returns>Resolved <see cref="IBenchSafetyInterlock"/>.</returns>
    public IBenchSafetyInterlock SafetyInterlock(string role) => TryGetSafetyInterlock(role).GetValueOrThrow();

    /// <summary>Execute a safe monotonic voltage ramp on an SMU role and record the step outcome into the evidence trail.</summary>
    /// <param name="role">SMU bench role name.</param>
    /// <param name="targetVoltage">Final target voltage.</param>
    /// <param name="currentLimit">Compliance current limit programmed prior to energizing.</param>
    /// <param name="maxStep">Maximum voltage increment per ramp step.</param>
    /// <param name="stepDelay">Optional dwell between steps.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad result containing all step readbacks.</returns>
    public async Task<BenchResult<IReadOnlyList<VoltageRampSample>>> RampVoltageSafelyAsync(
        string role,
        ElectricPotential targetVoltage,
        ElectricCurrent currentLimit,
        ElectricPotential maxStep,
        TimeSpan? stepDelay = null,
        CancellationToken cancellationToken = default)
    {
        var smuRes = TryGetSmu(role);
        if (smuRes.IsFailure)
            return BenchResult<IReadOnlyList<VoltageRampSample>>.Fail(smuRes.Error);

        var ramp = await SafeVoltageRampController
            .RampToAsync(smuRes.Value, targetVoltage, currentLimit, maxStep, stepDelay, true, cancellationToken)
            .ConfigureAwait(false);

        RecordStep(
            $"Ramp({role}->{targetVoltage.Volts:G4}V)",
            ramp.IsSuccess,
            ramp.IsSuccess
                ? $"Completed {ramp.Value.Count} step(s); final {ramp.Value[^1].MeasuredVoltage.Volts:F4} V / {ramp.Value[^1].MeasuredCurrent.Amperes:F4} A"
                : ramp.Error.Message,
            ramp.IsFailure ? ramp.Error : null);

        return ramp;
    }

    /// <summary>Record a custom step result into the session's as-run flight evidence trail.</summary>
    /// <param name="stepName">Step identifier.</param>
    /// <param name="passed">Pass/fail outcome.</param>
    /// <param name="summary">Measured values or failure summary.</param>
    /// <param name="error">Optional structured error.</param>
    public void RecordStep(string stepName, bool passed, string summary, BenchError? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);
        using (_stepGate.EnterScope())
            _steps.Add(new FlightTestStepRecord(stepName, passed, summary ?? string.Empty, error, _clock.GetUtcNow()));
    }

    /// <summary>Seal all recorded steps, instrument descriptors, and calibration metadata into a tamper-evident <see cref="FlightEvidencePackage"/>.</summary>
    /// <param name="unitUnderTestSerial">Flight hardware serial number.</param>
    /// <param name="operatorPrincipal">Operator or automation identity.</param>
    /// <param name="procedureFingerprint">Optional Starlark procedure fingerprint.</param>
    /// <param name="auditChainHeadSha256">Optional security audit chain head digest.</param>
    /// <returns>Sealed <see cref="FlightEvidencePackage"/>.</returns>
    public FlightEvidencePackage SealEvidence(
        string unitUnderTestSerial,
        string operatorPrincipal,
        string? procedureFingerprint = null,
        string? auditChainHeadSha256 = null)
    {
        FlightTestStepRecord[] snapshot;
        using (_stepGate.EnterScope()) snapshot = _steps.ToArray();
        return FlightEvidencePackage.Seal(
            unitUnderTestSerial,
            BenchName,
            operatorPrincipal,
            _startedUtc,
            _clock.GetUtcNow(),
            Describe(),
            snapshot,
            procedureFingerprint,
            auditChainHeadSha256);
    }

    /// <summary>Attempt to transition every registered instrument to its safe idle state, reporting the first failure if any.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when all instruments confirmed safe state; otherwise the first failure with recovery guidance.</returns>
    public async Task<BenchResult> EmergencySafeAllAsync(CancellationToken cancellationToken = default)
    {
        BenchError? firstError = null;
        foreach (var inst in _instruments.Values.OrderBy(i => i.Role, StringComparer.Ordinal))
        {
            var res = await inst.SafeStateAsync(cancellationToken).ConfigureAwait(false);
            if (res.IsFailure && firstError is null)
                firstError = res.Error;
        }
        return firstError is null ? BenchResult.Ok() : BenchResult.Fail(firstError);
    }

    /// <summary>Transition all instruments to safe state upon session disposal.</summary>
    /// <returns>Completion of safe shutdown.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await EmergencySafeAllAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private BenchResult<T> Resolve<T>(string role, string expectedKind) where T : class
    {
        if (string.IsNullOrWhiteSpace(role))
            return BenchResult<T>.Fail(BenchError.Validation("BF-ROLE-EMPTY", "Instrument role must be non-empty."));
        if (!_instruments.TryGetValue(role, out var inst))
        {
            return BenchResult<T>.Fail(new BenchError(
                "BF-ROLE-MISSING",
                BenchErrorKind.ResourceNotFound,
                $"Instrument role '{role}' is not registered on bench '{BenchName}'.",
                role,
                Remedy: $"Available roles: {string.Join(", ", _instruments.Keys.OrderBy(k => k, StringComparer.Ordinal))}."));
        }
        if (inst is not T typed)
        {
            return BenchResult<T>.Fail(new BenchError(
                "BF-ROLE-TYPE",
                BenchErrorKind.UnsupportedCapability,
                $"Instrument role '{role}' ({inst.Describe().InstrumentClass}) does not support {expectedKind}.",
                role,
                Remedy: "Call BenchSession.DescribeJson() to inspect supported capabilities per role."));
        }
        return BenchResult<T>.Ok(typed);
    }
}

/// <summary>Fluent builder for assembling symmetric physical or simulated <see cref="BenchSession"/> instances.</summary>
public sealed class BenchBuilder
{
    private readonly string _benchName;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, IBenchInstrument> _instruments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FlightSafeEnvelope> _envelopes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HardLimitRegistry _hardLimits = new();
    private bool _requireHardLimitsForPhysical;
    private AdjustableSimulationClock? _simClock;
    private DigitalTwinPlant? _digitalTwin;

    internal BenchBuilder(string benchName, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchName);
        _benchName = benchName;
        _clock = clock;
    }

    /// <summary>Register any physical, remote, or custom <see cref="IBenchInstrument"/> implementation.</summary>
    /// <param name="instrument">Canonical bench instrument instance.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddInstrument(IBenchInstrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        if (!_instruments.TryAdd(instrument.Role, instrument))
            throw new InvalidOperationException($"Duplicate bench role '{instrument.Role}'.");
        return this;
    }

    /// <summary>Attach a <see cref="FlightSafeEnvelope"/> enforced automatically on the named SMU role.</summary>
    /// <param name="role">Target SMU role identifier.</param>
    /// <param name="envelope">Validated flight-hardware electrical envelope.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder WithFlightEnvelope(string role, FlightSafeEnvelope envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(envelope);
        envelope.Validate();
        _envelopes[role] = envelope;
        _ = _hardLimits.TryTighten(role, InstrumentHardLimits.FromFlightEnvelope(envelope));
        return this;
    }

    /// <summary>
    /// Register final fail-safe <see cref="InstrumentHardLimits"/> on any instrument role (PSU, SMU, Thermal, FGEN,
    /// Electronic Load, RF Signal Generator, or LCR meter). Repeated calls may only monotonically tighten limits;
    /// attempting to relax an already-registered maximum throws <see cref="BenchOperationException"/>.
    /// </summary>
    /// <param name="role">Target instrument role identifier.</param>
    /// <param name="limits">Validated final fail-safe maximums and minimums.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder WithHardLimits(string role, InstrumentHardLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(limits);
        var res = _hardLimits.TryRegister(role, limits);
        if (res.IsFailure)
            throw new BenchOperationException(res.Error);
        return this;
    }

    /// <summary>
    /// Require every physical output-capable instrument to have registered <see cref="InstrumentHardLimits"/> before
    /// <see cref="Build"/> succeeds (fail-closed flight-hardware protection policy).
    /// </summary>
    /// <param name="require">True to fail closed when any physical output instrument lacks hard limits.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder RequireHardLimitsForPhysical(bool require = true)
    {
        _requireHardLimitsForPhysical = require;
        return this;
    }

    /// <summary>Add a stateful simulated digital multimeter role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="nominalVoltageVolts">Initial simulated DC voltage in volts.</param>
    /// <param name="nominalResistanceOhms">Initial simulated resistance in ohms.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockDmm(
        string role,
        double nominalVoltageVolts = 0.0,
        double nominalResistanceOhms = 100.0,
        InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockDigitalMultimeter(role, nominalVoltageVolts, 0.0, nominalResistanceOhms, calibration));

    /// <summary>Add a stateful simulated SMU role modeling resistive load physics and compliance clamping.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="loadResistanceOhms">Simulated load resistance in ohms.</param>
    /// <param name="defaultCurrentLimitAmperes">Initial compliance current in amperes.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockSmu(
        string role,
        double loadResistanceOhms = 10.0,
        double defaultCurrentLimitAmperes = 0.1,
        InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockSourceMeasureUnit(role, loadResistanceOhms, defaultCurrentLimitAmperes, calibration));

    /// <summary>Add a stateful simulated thermal chamber controller role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="initialTempCelsius">Initial chamber temperature in Celsius.</param>
    /// <param name="calibration">Optional calibration certificate metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockThermal(
        string role,
        double initialTempCelsius = 25.0,
        InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockThermalController(role, initialTempCelsius, calibration));

    /// <summary>Add a stateful simulated function generator role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockFunctionGenerator(string role, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockFunctionGenerator(role, calibration));

    /// <summary>Add a stateful simulated oscilloscope role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="dcOffsetVolts">Simulated DC rail offset in volts.</param>
    /// <param name="rippleAmplitudeVolts">Simulated AC sinusoidal ripple amplitude in volts.</param>
    /// <param name="sampleCount">Samples per waveform acquisition.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockOscilloscope(string role, double dcOffsetVolts = 3.3, double rippleAmplitudeVolts = 0.008, int sampleCount = 1024, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockOscilloscope(role, dcOffsetVolts, rippleAmplitudeVolts, sampleCount, calibration));

    /// <summary>Add a stateful simulated switch matrix role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockSwitchMatrix(string role, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockSwitchMatrix(role, calibration));

    /// <summary>Add a stateful simulated programmable DC electronic load role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="sourceVoltageVolts">Simulated bus voltage in volts.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockElectronicLoad(string role, double sourceVoltageVolts = 28.0, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockElectronicLoad(role, sourceVoltageVolts, calibration));

    /// <summary>Add a stateful simulated RF signal generator role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockRfSignalGenerator(string role, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockRfSignalGenerator(role, calibration));

    /// <summary>Add a stateful simulated spectrum analyzer role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockSpectrumAnalyzer(string role, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockSpectrumAnalyzer(role, calibration));

    /// <summary>Add a stateful simulated RF power meter role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="measuredWatts">Initial simulated RF power in watts.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockRfPowerMeter(string role, decimal measuredWatts = 0.001m, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockRfPowerMeter(role, measuredWatts, calibration));

    /// <summary>Add a stateful simulated universal frequency counter role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="frequencyHz">Initial simulated frequency in hertz.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockFrequencyCounter(string role, double frequencyHz = 10_000_000.0, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockFrequencyCounter(role, frequencyHz, calibration));

    /// <summary>Add a stateful simulated LCR / impedance meter role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="capacitanceFarads">Initial simulated capacitance in farads.</param>
    /// <param name="resistanceOhms">Initial simulated resistance in ohms.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockLcrMeter(string role, double capacitanceFarads = 1e-6, double resistanceOhms = 0.05, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockLcrMeter(role, capacitanceFarads, resistanceOhms, calibration));

    /// <summary>Add a stateful simulated Vector Network Analyzer role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockVna(string role, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockVectorNetworkAnalyzer(role, calibration));

    /// <summary>Add a time-aware stateful simulated multiplexed DAQ role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="channelCount">Initial channel count.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockDaq(string role, int channelCount = 8, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockDataAcquisition(role, channelCount, calibration));

    /// <summary>Add a stateful simulated discrete Digital I/O role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockDigitalIo(string role, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockDigitalIo(role, calibration));

    /// <summary>Add a time-aware stateful simulated safety interlock and dead-man watchdog role.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="watchdogTimeout">Maximum logical time permitted between watchdog kicks before auto-trip.</param>
    /// <param name="calibration">Optional calibration metadata.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder AddMockSafetyInterlock(string role, TimeSpan? watchdogTimeout = null, InstrumentCalibrationInfo? calibration = null) =>
        AddInstrument(new StatefulMockSafetyInterlock(role, watchdogTimeout, calibration));

    /// <summary>Configure the canonical run-scoped <see cref="AdjustableSimulationClock"/> shared by all time-aware mocks and digital-twin models.</summary>
    /// <param name="simulationClock">Caller-configured simulation clock.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder WithSimulationClock(AdjustableSimulationClock simulationClock)
    {
        _simClock = simulationClock ?? throw new ArgumentNullException(nameof(simulationClock));
        return this;
    }

    /// <summary>Attach a shared closed-loop <see cref="DigitalTwinPlant"/> stepped automatically by <see cref="BenchSession.AdvanceSimulationTime"/>.</summary>
    /// <param name="plant">Closed-loop spacecraft UUT physics plant.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public BenchBuilder WithDigitalTwin(DigitalTwinPlant plant)
    {
        _digitalTwin = plant ?? throw new ArgumentNullException(nameof(plant));
        return this;
    }

    /// <summary>
    /// Build the configured <see cref="BenchSession"/>, automatically wrapping instruments with declared flight envelopes
    /// and sealing all registered fail-safe <see cref="InstrumentHardLimits"/>.
    /// </summary>
    /// <returns>Configured <see cref="BenchSession"/> with a sealed <see cref="HardLimitRegistry"/>.</returns>
    public BenchSession Build()
    {
        var resolved = new Dictionary<string, IBenchInstrument>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, inst) in _instruments)
        {
            var hardLimit = _hardLimits.TryGet(role);
            if (_requireHardLimitsForPhysical &&
                inst.Backend == Contracts.InstrumentBackend.Physical &&
                IsOutputCapable(inst) &&
                hardLimit is null)
            {
                throw new BenchOperationException(new BenchError(
                    "BF-HARDLIMIT-UNREGISTERED",
                    BenchErrorKind.EnvelopeViolation,
                    $"Physical output-capable instrument '{role}' ({inst.Describe().InstrumentClass}) cannot be opened without registered fail-safe InstrumentHardLimits.",
                    role,
                    Remedy: $"Call WithHardLimits(\"{role}\", ...) before Build()."));
            }

            IBenchInstrument wrapped = inst;
            if (hardLimit is not null)
                wrapped = InstrumentHardLimitWrapper.Wrap(wrapped, hardLimit, _hardLimits);
            if (wrapped is IBenchSourceMeasureUnit smu && _envelopes.TryGetValue(role, out var env))
                wrapped = new FlightEnvelopedSmuProxy(smu, env);

            resolved[role] = wrapped;
        }
        _hardLimits.Seal();
        return new BenchSession(_benchName, resolved, _clock, _hardLimits, _simClock, _digitalTwin);
    }

    private static bool IsOutputCapable(IBenchInstrument instrument) =>
        instrument is IBenchPowerSupply
            or IBenchSourceMeasureUnit
            or IBenchThermalController
            or IBenchFunctionGenerator
            or IBenchElectronicLoad
            or IBenchRfSignalGenerator
            or IBenchLcrMeter
            or IBenchVectorNetworkAnalyzer;
}
