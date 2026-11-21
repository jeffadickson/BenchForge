using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchForge.Contracts;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using UnitsNet;

namespace BenchForge.Flight;

/// <summary>
/// Hard flight-hardware electrical and thermal safe operating envelope enforced before any actuation reaches hardware.
/// </summary>
/// <param name="MaxVoltage">Maximum permitted output voltage (inclusive).</param>
/// <param name="MinVoltage">Minimum permitted output voltage (inclusive; defaults to 0 V to prevent accidental reverse bias).</param>
/// <param name="MaxCurrent">Maximum permitted current limit / compliance ceiling (inclusive).</param>
/// <param name="MaxPower">Optional maximum instantaneous electrical power ceiling (<c>|V * I|</c>).</param>
/// <param name="MaxVoltageStep">Maximum voltage change permitted in a single ramp step to protect sensitive flight ASICs.</param>
/// <param name="RequireComplianceBeforeOutputOn">When true, blocks <c>SetOutputEnabledAsync(true)</c> unless <c>SetCurrentLimitAsync</c> was explicitly set first.</param>
public sealed record FlightSafeEnvelope(
    ElectricPotential MaxVoltage,
    ElectricPotential? MinVoltage = null,
    ElectricCurrent? MaxCurrent = null,
    Power? MaxPower = null,
    ElectricPotential? MaxVoltageStep = null,
    bool RequireComplianceBeforeOutputOn = true)
{
    /// <summary>Validate that all declared flight-envelope bounds are finite and consistent.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Any bound is non-finite or inverted.</exception>
    public void Validate()
    {
        double minV = (MinVoltage ?? ElectricPotential.Zero).Volts;
        double maxV = MaxVoltage.Volts;
        if (!double.IsFinite(minV) || !double.IsFinite(maxV) || minV > maxV)
            throw new ArgumentOutOfRangeException(nameof(MaxVoltage), "Voltage envelope bounds must be finite with MinVoltage <= MaxVoltage.");
        if (MaxCurrent is { } maxI && (!double.IsFinite(maxI.Amperes) || maxI.Amperes <= 0))
            throw new ArgumentOutOfRangeException(nameof(MaxCurrent), "MaxCurrent must be finite and positive.");
        if (MaxPower is { } maxP && maxP.Watts <= 0m)
            throw new ArgumentOutOfRangeException(nameof(MaxPower), "MaxPower must be positive.");
        if (MaxVoltageStep is { } step && (!double.IsFinite(step.Volts) || step.Volts <= 0))
            throw new ArgumentOutOfRangeException(nameof(MaxVoltageStep), "MaxVoltageStep must be finite and positive.");
    }

    /// <summary>Check whether a requested voltage setpoint lies inside this envelope.</summary>
    /// <param name="resourceId">Target instrument role.</param>
    /// <param name="requested">Requested voltage setpoint.</param>
    /// <param name="activeCurrentLimit">Optional active current limit used to check <see cref="MaxPower"/>.</param>
    /// <returns>Success or a structured <see cref="BenchErrorKind.EnvelopeViolation"/> error.</returns>
    public BenchResult CheckVoltage(string resourceId, ElectricPotential requested, ElectricCurrent? activeCurrentLimit = null)
    {
        double v = requested.Volts;
        double minV = (MinVoltage ?? ElectricPotential.Zero).Volts;
        double maxV = MaxVoltage.Volts;
        if (!double.IsFinite(v) || v < minV || v > maxV)
        {
            return BenchResult.Fail(BenchError.EnvelopeViolation(
                "BF-ENVELOPE-VOLT",
                $"Requested voltage {v:G6} V on '{resourceId}' violates flight envelope [{minV:G6}, {maxV:G6}] V.",
                resourceId,
                $"Keep '{resourceId}' voltage within [{minV:G6}, {maxV:G6}] V."));
        }

        if (MaxPower is { } maxP && activeCurrentLimit is { } cur)
        {
            double worstCaseWatts = Math.Abs(v * cur.Amperes);
            if (worstCaseWatts > (double)maxP.Watts)
            {
                return BenchResult.Fail(BenchError.EnvelopeViolation(
                    "BF-ENVELOPE-POWER",
                    $"Requested {v:G6} V * {cur.Amperes:G6} A = {worstCaseWatts:G6} W exceeds flight power envelope {maxP.Watts:G6} W on '{resourceId}'.",
                    resourceId,
                    $"Reduce voltage or current limit on '{resourceId}' so |V*I| <= {maxP.Watts:G6} W."));
            }
        }

        return BenchResult.Ok();
    }

    /// <summary>Check whether a requested current limit lies inside this envelope.</summary>
    /// <param name="resourceId">Target instrument role.</param>
    /// <param name="requested">Requested current compliance limit.</param>
    /// <returns>Success or a structured <see cref="BenchErrorKind.EnvelopeViolation"/> error.</returns>
    public BenchResult CheckCurrentLimit(string resourceId, ElectricCurrent requested)
    {
        double a = requested.Amperes;
        if (!double.IsFinite(a) || a <= 0)
        {
            return BenchResult.Fail(BenchError.EnvelopeViolation(
                "BF-ENVELOPE-CURR",
                $"Current limit on '{resourceId}' must be finite and strictly positive.",
                resourceId,
                "Set a positive current compliance limit before energizing."));
        }
        if (MaxCurrent is { } maxI && a > maxI.Amperes)
        {
            return BenchResult.Fail(BenchError.EnvelopeViolation(
                "BF-ENVELOPE-CURR",
                $"Requested current limit {a:G6} A on '{resourceId}' exceeds flight ceiling {maxI.Amperes:G6} A.",
                resourceId,
                $"Reduce current limit on '{resourceId}' to <= {maxI.Amperes:G6} A."));
        }
        return BenchResult.Ok();
    }
}

/// <summary>
/// Decorator wrapping any <see cref="IBenchSourceMeasureUnit"/> (physical or mock) to enforce a
/// <see cref="FlightSafeEnvelope"/> before every voltage write, current-limit write, and output enable.
/// </summary>
public sealed class FlightEnvelopedSmuProxy : IBenchSourceMeasureUnit
{
    private readonly IBenchSourceMeasureUnit _inner;
    private readonly FlightSafeEnvelope _envelope;
    private readonly Lock _gate = new();
    private bool _complianceExplicitlySet;
    private ElectricCurrent? _lastCurrentLimit;

    /// <summary>Wrap an SMU implementation with hard flight-hardware envelope enforcement.</summary>
    /// <param name="inner">Underlying physical or mock SMU.</param>
    /// <param name="envelope">Validated safe operating envelope.</param>
    public FlightEnvelopedSmuProxy(IBenchSourceMeasureUnit inner, FlightSafeEnvelope envelope)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        _envelope.Validate();
    }

    /// <summary>Underlying safe operating envelope enforced by this proxy.</summary>
    public FlightSafeEnvelope Envelope => _envelope;

    /// <inheritdoc/>
    public string Role => _inner.Role;

    /// <inheritdoc/>
    public InstrumentIdentity Identity => _inner.Identity;

    /// <inheritdoc/>
    public InstrumentBackend Backend => _inner.Backend;

    /// <inheritdoc/>
    public InstrumentCalibrationInfo? Calibration => _inner.Calibration;

    /// <inheritdoc/>
    public BenchInstrumentDescriptor Describe() => _inner.Describe();

    /// <inheritdoc/>
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) =>
        _inner.SafeStateAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult> SetVoltageAsync(ElectricPotential voltage, int channel = 1, CancellationToken cancellationToken = default)
    {
        ElectricCurrent? cur;
        using (_gate.EnterScope()) cur = _lastCurrentLimit;
        var check = _envelope.CheckVoltage(Role, voltage, cur);
        if (check.IsFailure)
            return Task.FromResult(check);
        return _inner.SetVoltageAsync(voltage, channel, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.GetVoltageAsync(channel, cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetCurrentLimitAsync(ElectricCurrent currentLimit, int channel = 1, CancellationToken cancellationToken = default)
    {
        var check = _envelope.CheckCurrentLimit(Role, currentLimit);
        if (check.IsFailure)
            return check;
        var res = await _inner.SetCurrentLimitAsync(currentLimit, channel, cancellationToken).ConfigureAwait(false);
        if (res.IsSuccess)
        {
            using (_gate.EnterScope())
            {
                _complianceExplicitlySet = true;
                _lastCurrentLimit = currentLimit;
            }
        }
        return res;
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> GetCurrentLimitAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.GetCurrentLimitAsync(channel, cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.MeasureVoltageAsync(channel, cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.MeasureCurrentAsync(channel, cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default)
    {
        if (enabled && _envelope.RequireComplianceBeforeOutputOn)
        {
            bool set;
            using (_gate.EnterScope()) set = _complianceExplicitlySet;
            if (!set)
            {
                return Task.FromResult(BenchResult.Fail(BenchError.EnvelopeViolation(
                    "BF-ENVELOPE-COMPLIANCE-REQUIRED",
                    $"Cannot enable output on '{Role}' before an explicit current compliance limit is programmed.",
                    Role,
                    $"Call SetCurrentLimitAsync on '{Role}' before calling SetOutputEnabledAsync(true).")));
            }
        }
        return _inner.SetOutputEnabledAsync(enabled, channel, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<BenchResult<bool>> GetOutputEnabledAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.GetOutputEnabledAsync(channel, cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult<bool>> IsComplianceTrippedAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.IsComplianceTrippedAsync(channel, cancellationToken);
}

/// <summary>Telemetry record emitted for each monotonic step during a safe flight-hardware voltage ramp.</summary>
/// <param name="StepIndex">One-based ramp step number.</param>
/// <param name="ProgrammedVoltage">Commanded voltage at this step.</param>
/// <param name="MeasuredVoltage">Readback terminal voltage at this step.</param>
/// <param name="MeasuredCurrent">Readback load current at this step.</param>
public sealed record VoltageRampSample(
    int StepIndex,
    ElectricPotential ProgrammedVoltage,
    ElectricPotential MeasuredVoltage,
    ElectricCurrent MeasuredCurrent);

/// <summary>
/// Flight-hardware safe voltage ramp controller that steps monotonically in bounded increments, checks compliance
/// at every step, and automatically de-energizes the supply if compliance trips or cancellation fires.
/// </summary>
public static class SafeVoltageRampController
{
    /// <summary>
    /// Ramp <paramref name="smu"/> monotonically to <paramref name="targetVoltage"/> in steps of at most
    /// <paramref name="maxStep"/> while enforcing <paramref name="currentLimit"/> and aborting safely on compliance trip.
    /// </summary>
    /// <param name="smu">Target physical or mock SMU.</param>
    /// <param name="targetVoltage">Final target voltage.</param>
    /// <param name="currentLimit">Explicit current compliance ceiling programmed before output enable.</param>
    /// <param name="maxStep">Maximum voltage delta per step (must be strictly positive).</param>
    /// <param name="stepDelay">Optional dwell between steps (zero for deterministic unit tests).</param>
    /// <param name="abortOnComplianceTrip">When true, immediately executes <c>SafeStateAsync</c> and returns <see cref="BenchErrorKind.ComplianceTripped"/> on trip.</param>
    /// <param name="cancellationToken">Cancellation token; triggers safe de-energization if cancelled mid-ramp.</param>
    /// <returns>Railroad result containing all step readbacks, or a structured error after safe shutdown.</returns>
    public static async Task<BenchResult<IReadOnlyList<VoltageRampSample>>> RampToAsync(
        IBenchSourceMeasureUnit smu,
        ElectricPotential targetVoltage,
        ElectricCurrent currentLimit,
        ElectricPotential maxStep,
        TimeSpan? stepDelay = null,
        bool abortOnComplianceTrip = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(smu);
        if (!double.IsFinite(maxStep.Volts) || maxStep.Volts <= 0)
            return BenchResult<IReadOnlyList<VoltageRampSample>>.Fail(BenchError.Validation("BF-RAMP-STEP", "Max ramp step must be strictly positive.", smu.Role));

        var limitRes = await smu.SetCurrentLimitAsync(currentLimit, 1, cancellationToken).ConfigureAwait(false);
        if (limitRes.IsFailure)
            return BenchResult<IReadOnlyList<VoltageRampSample>>.Fail(limitRes.Error);

        var startRes = await smu.GetVoltageAsync(1, cancellationToken).ConfigureAwait(false);
        if (startRes.IsFailure)
            return BenchResult<IReadOnlyList<VoltageRampSample>>.Fail(startRes.Error);

        var enableRes = await smu.SetOutputEnabledAsync(true, 1, cancellationToken).ConfigureAwait(false);
        if (enableRes.IsFailure)
            return BenchResult<IReadOnlyList<VoltageRampSample>>.Fail(enableRes.Error);

        double currentV = startRes.Value.Volts;
        double targetV = targetVoltage.Volts;
        double delta = targetV - currentV;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / maxStep.Volts));
        var samples = new List<VoltageRampSample>(steps);

        for (int i = 1; i <= steps; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await smu.SafeStateAsync(CancellationToken.None).ConfigureAwait(false);
                return BenchResult<IReadOnlyList<VoltageRampSample>>.Fail(new BenchError(
                    "BF-RAMP-CANCELLED",
                    BenchErrorKind.UncertainPhysicalState,
                    $"Voltage ramp on '{smu.Role}' cancelled at step {i}/{steps}; safe-state shutdown issued.",
                    smu.Role,
                    Confidence: PhysicalStateConfidence.TransportOnly,
                    RequiresRecovery: true,
                    Remedy: $"Verify '{smu.Role}' terminals are at 0 V before resuming."));
            }

            double nextV = i == steps ? targetV : currentV + (delta * i / steps);
            var setRes = await smu.SetVoltageAsync(ElectricPotential.FromVolts(nextV), 1, cancellationToken).ConfigureAwait(false);
            if (setRes.IsFailure)
            {
                await smu.SafeStateAsync(CancellationToken.None).ConfigureAwait(false);
                return BenchResult<IReadOnlyList<VoltageRampSample>>.Fail(setRes.Error);
            }

            if (stepDelay is { } delay && delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            var measV = await smu.MeasureVoltageAsync(1, cancellationToken).ConfigureAwait(false);
            var measI = await smu.MeasureCurrentAsync(1, cancellationToken).ConfigureAwait(false);
            var tripped = await smu.IsComplianceTrippedAsync(1, cancellationToken).ConfigureAwait(false);

            if (measV.IsFailure || measI.IsFailure || tripped.IsFailure)
            {
                await smu.SafeStateAsync(CancellationToken.None).ConfigureAwait(false);
                var err = measV.IsFailure ? measV.Error : measI.IsFailure ? measI.Error : tripped.Error;
                return BenchResult<IReadOnlyList<VoltageRampSample>>.Fail(err);
            }

            samples.Add(new VoltageRampSample(i, ElectricPotential.FromVolts(nextV), measV.Value, measI.Value));

            if (abortOnComplianceTrip && tripped.Value)
            {
                await smu.SafeStateAsync(CancellationToken.None).ConfigureAwait(false);
                return BenchResult<IReadOnlyList<VoltageRampSample>>.Fail(new BenchError(
                    "BF-COMPLIANCE-TRIP",
                    BenchErrorKind.ComplianceTripped,
                    $"Compliance tripped on '{smu.Role}' at ramp step {i}/{steps} ({nextV:G4} V, measured {measI.Value.Amperes:G4} A); output safely disabled.",
                    smu.Role,
                    Confidence: PhysicalStateConfidence.TransportOnly,
                    RequiresRecovery: true,
                    Remedy: "Inspect unit-under-test for short circuit or excessive inrush current before re-energizing."));
            }
        }

        return BenchResult<IReadOnlyList<VoltageRampSample>>.Ok(samples.AsReadOnly());
    }
}

/// <summary>One recorded pass/fail assertion or measurement step inside a sealed flight evidence package.</summary>
/// <param name="StepName">Human- and machine-readable test step name.</param>
/// <param name="Passed">True when the step met all flight limits.</param>
/// <param name="Summary">Measured value, units, or structured error summary.</param>
/// <param name="Error">Optional structured <see cref="BenchError"/> when <paramref name="Passed"/> is false.</param>
/// <param name="RecordedUtc">UTC timestamp of step completion.</param>
public sealed record FlightTestStepRecord(
    string StepName,
    bool Passed,
    string Summary,
    BenchError? Error,
    DateTimeOffset RecordedUtc);

/// <summary>
/// Immutable, SHA-256-sealed as-run flight-hardware test evidence package suitable for QA review and archival.
/// </summary>
/// <param name="PackageId">Unique evidence package identifier.</param>
/// <param name="UnitUnderTestSerial">Flight hardware serial number under test.</param>
/// <param name="TestName">Qualified test procedure or suite name.</param>
/// <param name="OperatorPrincipal">Authenticated operator or automation principal who ran the test.</param>
/// <param name="ProcedureFingerprint">Optional SHA-256 procedure fingerprint.</param>
/// <param name="StartedUtc">UTC start timestamp.</param>
/// <param name="FinishedUtc">UTC completion timestamp.</param>
/// <param name="OverallPassed">True only when every recorded step passed.</param>
/// <param name="Instruments">Snapshot of all bench instruments, provenance, and calibration status.</param>
/// <param name="Steps">Ordered as-run test step records.</param>
/// <param name="AuditChainHeadSha256">Optional latest SHA-256 hash from the host security audit log.</param>
/// <param name="ManifestSha256">Uppercase SHA-256 seal computed over the canonical package contents.</param>
public sealed record FlightEvidencePackage(
    Guid PackageId,
    string UnitUnderTestSerial,
    string TestName,
    string OperatorPrincipal,
    string? ProcedureFingerprint,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    bool OverallPassed,
    IReadOnlyList<BenchInstrumentDescriptor> Instruments,
    IReadOnlyList<FlightTestStepRecord> Steps,
    string? AuditChainHeadSha256,
    string ManifestSha256)
{
    /// <summary>Create and cryptographically seal an immutable <see cref="FlightEvidencePackage"/>.</summary>
    /// <param name="unitUnderTestSerial">Flight hardware serial identifier.</param>
    /// <param name="testName">Test procedure name.</param>
    /// <param name="operatorPrincipal">Operator or automation identity.</param>
    /// <param name="startedUtc">UTC start timestamp.</param>
    /// <param name="finishedUtc">UTC finish timestamp.</param>
    /// <param name="instruments">Bench instrument descriptors.</param>
    /// <param name="steps">As-run step records.</param>
    /// <param name="procedureFingerprint">Optional Starlark procedure fingerprint.</param>
    /// <param name="auditChainHeadSha256">Optional security audit chain head digest.</param>
    /// <returns>Sealed <see cref="FlightEvidencePackage"/> whose <see cref="ManifestSha256"/> can be verified offline.</returns>
    public static FlightEvidencePackage Seal(
        string unitUnderTestSerial,
        string testName,
        string operatorPrincipal,
        DateTimeOffset startedUtc,
        DateTimeOffset finishedUtc,
        IReadOnlyList<BenchInstrumentDescriptor> instruments,
        IReadOnlyList<FlightTestStepRecord> steps,
        string? procedureFingerprint = null,
        string? auditChainHeadSha256 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitUnderTestSerial);
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorPrincipal);
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(steps);

        var draft = new FlightEvidencePackage(
            Guid.NewGuid(),
            unitUnderTestSerial,
            testName,
            operatorPrincipal,
            procedureFingerprint,
            startedUtc,
            finishedUtc,
            steps.Count > 0 && steps.All(s => s.Passed),
            instruments.ToArray(),
            steps.ToArray(),
            auditChainHeadSha256,
            string.Empty);

        return draft with { ManifestSha256 = ComputeSeal(draft) };
    }

    /// <summary>Verify that this evidence package has not been modified since it was sealed.</summary>
    /// <returns>True when the recomputed SHA-256 matches <see cref="ManifestSha256"/>.</returns>
    public bool VerifySeal() =>
        !string.IsNullOrWhiteSpace(ManifestSha256) &&
        string.Equals(ComputeSeal(this), ManifestSha256, StringComparison.Ordinal);

    private static string ComputeSeal(FlightEvidencePackage package)
    {
        var unsealed = package with { ManifestSha256 = string.Empty };
        var json = JsonSerializer.Serialize(unsealed);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
