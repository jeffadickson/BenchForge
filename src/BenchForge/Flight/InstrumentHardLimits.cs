using System.Globalization;
using System.Text.RegularExpressions;
using BenchForge.Async;
using BenchForge.Contracts;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using BenchForge.Simulation;
using UnitsNet;

namespace BenchForge.Flight;

/// <summary>
/// Final fail-safe hard operating maximums and minimums registered on an instrument role.
/// Once registered on an instrument, limits may only be monotonically tightened (never relaxed or removed)
/// and are enforced before any command reaches the instrument transport.
/// </summary>
/// <param name="MaxVoltage">Maximum permitted voltage or amplitude (inclusive).</param>
/// <param name="MinVoltage">Minimum permitted voltage (inclusive; defaults to 0 V when <paramref name="MaxVoltage"/> is set unless explicitly negative).</param>
/// <param name="MaxCurrent">Maximum permitted current limit or sink current (inclusive).</param>
/// <param name="MinCurrent">Optional minimum permitted current (inclusive).</param>
/// <param name="MaxPower">Maximum permitted power (applies to direct RF/DC power setpoints and worst-case <c>|V * I|</c>).</param>
/// <param name="MinFrequency">Optional minimum permitted frequency (inclusive).</param>
/// <param name="MaxFrequency">Optional maximum permitted frequency (inclusive).</param>
/// <param name="MinTemperature">Optional minimum permitted temperature setpoint (inclusive).</param>
/// <param name="MaxTemperature">Optional maximum permitted temperature setpoint (inclusive).</param>
/// <param name="MinResistance">Optional minimum permitted load resistance (inclusive; prevents shorting flight power buses).</param>
/// <param name="MaxResistance">Optional maximum permitted load resistance (inclusive).</param>
/// <param name="RequireComplianceBeforeOutputOn">When true, blocks energizing PSU/SMU outputs unless a current limit was explicitly set first.</param>
/// <param name="ProgramHardwareProtection">When true, requests programming instrument-resident OVP/OCP/OPP hardware protection registers.</param>
/// <param name="HardwareProtectionMarginFactor">Headroom multiplier applied when programming hardware OVP/OCP/OPP registers (1.0 to 1.25; default 1.0).</param>
/// <param name="AutoSafeOnViolation">When true, automatically triggers <c>SafeStateAsync</c> if a caller attempts a limit-violating write.</param>
/// <param name="PolicyNote">Optional engineering rationale or flight ICD reference for audit trails.</param>
public sealed record InstrumentHardLimits(
    ElectricPotential? MaxVoltage = null,
    ElectricPotential? MinVoltage = null,
    ElectricCurrent? MaxCurrent = null,
    ElectricCurrent? MinCurrent = null,
    Power? MaxPower = null,
    Frequency? MinFrequency = null,
    Frequency? MaxFrequency = null,
    Temperature? MinTemperature = null,
    Temperature? MaxTemperature = null,
    ElectricResistance? MinResistance = null,
    ElectricResistance? MaxResistance = null,
    bool RequireComplianceBeforeOutputOn = true,
    bool ProgramHardwareProtection = true,
    double HardwareProtectionMarginFactor = 1.0,
    bool AutoSafeOnViolation = false,
    string? PolicyNote = null)
{
    /// <summary>Validate that all declared limits are finite, positive where required, and non-inverted.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any bound is non-finite, inverted, or out of valid domain.</exception>
    public void Validate()
    {
        if (MaxVoltage is { } maxV)
        {
            double minV = (MinVoltage ?? ElectricPotential.Zero).Volts;
            if (!double.IsFinite(maxV.Volts) || !double.IsFinite(minV) || minV > maxV.Volts)
                throw new ArgumentOutOfRangeException(nameof(MaxVoltage), "Voltage bounds must be finite with MinVoltage <= MaxVoltage.");
        }
        else if (MinVoltage is { } onlyMinV && !double.IsFinite(onlyMinV.Volts))
        {
            throw new ArgumentOutOfRangeException(nameof(MinVoltage), "MinVoltage must be finite.");
        }

        if (MaxCurrent is { } maxI && (!double.IsFinite(maxI.Amperes) || maxI.Amperes <= 0))
            throw new ArgumentOutOfRangeException(nameof(MaxCurrent), "MaxCurrent must be finite and positive.");
        if (MinCurrent is { } minI && !double.IsFinite(minI.Amperes))
            throw new ArgumentOutOfRangeException(nameof(MinCurrent), "MinCurrent must be finite.");
        if (MinCurrent is { } loI && MaxCurrent is { } hiI && loI.Amperes > hiI.Amperes)
            throw new ArgumentOutOfRangeException(nameof(MinCurrent), "MinCurrent must be <= MaxCurrent.");

        if (MaxPower is { } maxP && maxP.Watts <= 0m)
            throw new ArgumentOutOfRangeException(nameof(MaxPower), "MaxPower must be positive.");

        if (MinFrequency is { } minF && (!double.IsFinite(minF.Hertz) || minF.Hertz < 0))
            throw new ArgumentOutOfRangeException(nameof(MinFrequency), "MinFrequency must be finite and non-negative.");
        if (MaxFrequency is { } maxF && (!double.IsFinite(maxF.Hertz) || maxF.Hertz <= 0))
            throw new ArgumentOutOfRangeException(nameof(MaxFrequency), "MaxFrequency must be finite and positive.");
        if (MinFrequency is { } loF && MaxFrequency is { } hiF && loF.Hertz > hiF.Hertz)
            throw new ArgumentOutOfRangeException(nameof(MinFrequency), "MinFrequency must be <= MaxFrequency.");

        if (MinTemperature is { } minT && !double.IsFinite(minT.DegreesCelsius))
            throw new ArgumentOutOfRangeException(nameof(MinTemperature), "MinTemperature must be finite.");
        if (MaxTemperature is { } maxT && !double.IsFinite(maxT.DegreesCelsius))
            throw new ArgumentOutOfRangeException(nameof(MaxTemperature), "MaxTemperature must be finite.");
        if (MinTemperature is { } loT && MaxTemperature is { } hiT && loT.DegreesCelsius > hiT.DegreesCelsius)
            throw new ArgumentOutOfRangeException(nameof(MinTemperature), "MinTemperature must be <= MaxTemperature.");

        if (MinResistance is { } minR && (!double.IsFinite(minR.Ohms) || minR.Ohms < 0))
            throw new ArgumentOutOfRangeException(nameof(MinResistance), "MinResistance must be finite and non-negative.");
        if (MaxResistance is { } maxR && (!double.IsFinite(maxR.Ohms) || maxR.Ohms <= 0))
            throw new ArgumentOutOfRangeException(nameof(MaxResistance), "MaxResistance must be finite and positive.");
        if (MinResistance is { } loR && MaxResistance is { } hiR && loR.Ohms > hiR.Ohms)
            throw new ArgumentOutOfRangeException(nameof(MinResistance), "MinResistance must be <= MaxResistance.");

        if (!double.IsFinite(HardwareProtectionMarginFactor) || HardwareProtectionMarginFactor < 1.0 || HardwareProtectionMarginFactor > 1.25)
            throw new ArgumentOutOfRangeException(nameof(HardwareProtectionMarginFactor), "HardwareProtectionMarginFactor must lie within [1.0, 1.25].");
    }

    /// <summary>Determine whether <paramref name="candidate"/> would relax or remove any limit enforced by this policy.</summary>
    /// <param name="candidate">Proposed replacement or update limit policy.</param>
    /// <returns>True if any ceiling is raised, any floor is lowered, or any safety lock is weakened.</returns>
    public bool WouldBeRelaxedBy(InstrumentHardLimits candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (MaxVoltage is { } oldMaxV && (candidate.MaxVoltage is null || candidate.MaxVoltage.Value.Volts > oldMaxV.Volts))
            return true;
        ElectricPotential? effectiveOldMinV = MinVoltage ?? (MaxVoltage is not null ? ElectricPotential.Zero : null);
        ElectricPotential? effectiveNewMinV = candidate.MinVoltage ?? (candidate.MaxVoltage is not null ? ElectricPotential.Zero : null);
        if (effectiveOldMinV is { } oldMinV && (effectiveNewMinV is null || effectiveNewMinV.Value.Volts < oldMinV.Volts))
            return true;

        if (MaxCurrent is { } oldMaxI && (candidate.MaxCurrent is null || candidate.MaxCurrent.Value.Amperes > oldMaxI.Amperes))
            return true;
        if (MinCurrent is { } oldMinI && (candidate.MinCurrent is null || candidate.MinCurrent.Value.Amperes < oldMinI.Amperes))
            return true;

        if (MaxPower is { } oldMaxP && (candidate.MaxPower is null || candidate.MaxPower.Value.Watts > oldMaxP.Watts))
            return true;

        if (MaxFrequency is { } oldMaxF && (candidate.MaxFrequency is null || candidate.MaxFrequency.Value.Hertz > oldMaxF.Hertz))
            return true;
        if (MinFrequency is { } oldMinF && (candidate.MinFrequency is null || candidate.MinFrequency.Value.Hertz < oldMinF.Hertz))
            return true;

        if (MaxTemperature is { } oldMaxT && (candidate.MaxTemperature is null || candidate.MaxTemperature.Value.DegreesCelsius > oldMaxT.DegreesCelsius))
            return true;
        if (MinTemperature is { } oldMinT && (candidate.MinTemperature is null || candidate.MinTemperature.Value.DegreesCelsius < oldMinT.DegreesCelsius))
            return true;

        if (MaxResistance is { } oldMaxR && (candidate.MaxResistance is null || candidate.MaxResistance.Value.Ohms > oldMaxR.Ohms))
            return true;
        if (MinResistance is { } oldMinR && (candidate.MinResistance is null || candidate.MinResistance.Value.Ohms < oldMinR.Ohms))
            return true;

        if (RequireComplianceBeforeOutputOn && !candidate.RequireComplianceBeforeOutputOn)
            return true;
        if (ProgramHardwareProtection && !candidate.ProgramHardwareProtection)
            return true;
        if (candidate.HardwareProtectionMarginFactor > HardwareProtectionMarginFactor)
            return true;
        if (AutoSafeOnViolation && !candidate.AutoSafeOnViolation)
            return true;

        return false;
    }

    /// <summary>Compute the strictly tighter intersection of this limit policy and <paramref name="additional"/>.</summary>
    /// <param name="additional">Additional constraints to intersect monotonically.</param>
    /// <returns>New validated <see cref="InstrumentHardLimits"/> that is at least as strict on every dimension.</returns>
    public InstrumentHardLimits TightenWith(InstrumentHardLimits additional)
    {
        ArgumentNullException.ThrowIfNull(additional);
        Validate();
        additional.Validate();

        ElectricPotential? minV = MaxNullable(
            MinVoltage ?? (MaxVoltage is not null ? ElectricPotential.Zero : null),
            additional.MinVoltage ?? (additional.MaxVoltage is not null ? ElectricPotential.Zero : null),
            x => x.Volts);

        var tightened = new InstrumentHardLimits(
            MaxVoltage: MinNullable(MaxVoltage, additional.MaxVoltage, x => x.Volts),
            MinVoltage: minV,
            MaxCurrent: MinNullable(MaxCurrent, additional.MaxCurrent, x => x.Amperes),
            MinCurrent: MaxNullable(MinCurrent, additional.MinCurrent, x => x.Amperes),
            MaxPower: MinNullable(MaxPower, additional.MaxPower, x => (double)x.Watts),
            MinFrequency: MaxNullable(MinFrequency, additional.MinFrequency, x => x.Hertz),
            MaxFrequency: MinNullable(MaxFrequency, additional.MaxFrequency, x => x.Hertz),
            MinTemperature: MaxNullable(MinTemperature, additional.MinTemperature, x => x.DegreesCelsius),
            MaxTemperature: MinNullable(MaxTemperature, additional.MaxTemperature, x => x.DegreesCelsius),
            MinResistance: MaxNullable(MinResistance, additional.MinResistance, x => x.Ohms),
            MaxResistance: MinNullable(MaxResistance, additional.MaxResistance, x => x.Ohms),
            RequireComplianceBeforeOutputOn: RequireComplianceBeforeOutputOn || additional.RequireComplianceBeforeOutputOn,
            ProgramHardwareProtection: ProgramHardwareProtection || additional.ProgramHardwareProtection,
            HardwareProtectionMarginFactor: Math.Min(HardwareProtectionMarginFactor, additional.HardwareProtectionMarginFactor),
            AutoSafeOnViolation: AutoSafeOnViolation || additional.AutoSafeOnViolation,
            PolicyNote: additional.PolicyNote ?? PolicyNote);
        tightened.Validate();
        return tightened;
    }

    /// <summary>Verify that a requested voltage setpoint lies within the registered hard voltage and power ceilings.</summary>
    /// <param name="role">Target instrument role.</param>
    /// <param name="requested">Requested voltage setpoint.</param>
    /// <param name="activeCurrentLimit">Optional active current limit for worst-case <c>|V * I|</c> power validation.</param>
    /// <returns>Success or a structured <see cref="BenchErrorKind.EnvelopeViolation"/> result.</returns>
    public BenchResult CheckVoltage(string role, ElectricPotential requested, ElectricCurrent? activeCurrentLimit = null)
    {
        double v = requested.Volts;
        if (!double.IsFinite(v))
            return Fail(role, "BF-HARDLIMIT-VOLT", $"Non-finite voltage rejected on '{role}'.", "Supply a finite voltage.");

        if (MaxVoltage is { } maxV)
        {
            double minV = (MinVoltage ?? ElectricPotential.Zero).Volts;
            if (v < minV || v > maxV.Volts)
            {
                return Fail(role, "BF-HARDLIMIT-VOLT",
                    $"Requested voltage {v:G6} V on '{role}' violates registered hard limit [{minV:G6}, {maxV.Volts:G6}] V.",
                    $"Keep '{role}' voltage within [{minV:G6}, {maxV.Volts:G6}] V.");
            }
        }
        else if (MinVoltage is { } onlyMinV && v < onlyMinV.Volts)
        {
            return Fail(role, "BF-HARDLIMIT-VOLT",
                $"Requested voltage {v:G6} V on '{role}' is below registered hard minimum {onlyMinV.Volts:G6} V.",
                $"Keep '{role}' voltage >= {onlyMinV.Volts:G6} V.");
        }

        if (MaxPower is { } maxP && activeCurrentLimit is { } cur)
        {
            double worstCaseW = Math.Abs(v * cur.Amperes);
            if (worstCaseW > (double)maxP.Watts)
            {
                return Fail(role, "BF-HARDLIMIT-POWER",
                    $"Requested {v:G6} V * {cur.Amperes:G6} A = {worstCaseW:G6} W exceeds registered hard power ceiling {maxP.Watts:G6} W on '{role}'.",
                    $"Reduce voltage or current limit on '{role}' so |V*I| <= {maxP.Watts:G6} W.");
            }
        }

        return BenchResult.Ok();
    }

    /// <summary>Verify that a requested current setpoint or limit lies within registered hard current and power ceilings.</summary>
    /// <param name="role">Target instrument role.</param>
    /// <param name="requested">Requested current.</param>
    /// <param name="activeVoltage">Optional active voltage for worst-case <c>|V * I|</c> power validation.</param>
    /// <returns>Success or a structured <see cref="BenchErrorKind.EnvelopeViolation"/> result.</returns>
    public BenchResult CheckCurrent(string role, ElectricCurrent requested, ElectricPotential? activeVoltage = null)
    {
        double a = requested.Amperes;
        if (!double.IsFinite(a))
            return Fail(role, "BF-HARDLIMIT-CURR", $"Non-finite current rejected on '{role}'.", "Supply a finite current.");
        if (MinCurrent is { } minI && a < minI.Amperes)
        {
            return Fail(role, "BF-HARDLIMIT-CURR",
                $"Requested current {a:G6} A on '{role}' is below registered hard minimum {minI.Amperes:G6} A.",
                $"Keep '{role}' current >= {minI.Amperes:G6} A.");
        }
        if (MaxCurrent is { } maxI && a > maxI.Amperes)
        {
            return Fail(role, "BF-HARDLIMIT-CURR",
                $"Requested current {a:G6} A on '{role}' exceeds registered hard maximum {maxI.Amperes:G6} A.",
                $"Keep '{role}' current <= {maxI.Amperes:G6} A.");
        }
        if (MaxPower is { } maxP && activeVoltage is { } volt)
        {
            double worstCaseW = Math.Abs(volt.Volts * a);
            if (worstCaseW > (double)maxP.Watts)
            {
                return Fail(role, "BF-HARDLIMIT-POWER",
                    $"Requested {volt.Volts:G6} V * {a:G6} A = {worstCaseW:G6} W exceeds registered hard power ceiling {maxP.Watts:G6} W on '{role}'.",
                    $"Reduce current or voltage on '{role}' so |V*I| <= {maxP.Watts:G6} W.");
            }
        }
        return BenchResult.Ok();
    }

    /// <summary>Verify that a requested power setpoint (RF or DC) lies within the registered hard power maximum.</summary>
    /// <param name="role">Target instrument role.</param>
    /// <param name="requested">Requested power.</param>
    /// <returns>Success or a structured <see cref="BenchErrorKind.EnvelopeViolation"/> result.</returns>
    public BenchResult CheckPower(string role, Power requested)
    {
        if (requested.Watts < 0m)
            return Fail(role, "BF-HARDLIMIT-POWER", $"Negative power {requested.Watts:G6} W rejected on '{role}'.", "Provide a non-negative power level.");
        if (MaxPower is { } maxP && requested.Watts > maxP.Watts)
        {
            return Fail(role, "BF-HARDLIMIT-POWER",
                $"Requested power {requested.Watts:G6} W on '{role}' exceeds registered hard maximum {maxP.Watts:G6} W.",
                $"Keep '{role}' power <= {maxP.Watts:G6} W.");
        }
        return BenchResult.Ok();
    }

    /// <summary>Verify that a requested frequency lies within the registered hard frequency bounds.</summary>
    /// <param name="role">Target instrument role.</param>
    /// <param name="requested">Requested frequency.</param>
    /// <returns>Success or a structured <see cref="BenchErrorKind.EnvelopeViolation"/> result.</returns>
    public BenchResult CheckFrequency(string role, Frequency requested)
    {
        double hz = requested.Hertz;
        if (!double.IsFinite(hz) || hz < 0)
            return Fail(role, "BF-HARDLIMIT-FREQ", $"Invalid frequency rejected on '{role}'.", "Supply a finite non-negative frequency.");
        if (MinFrequency is { } minF && hz < minF.Hertz)
        {
            return Fail(role, "BF-HARDLIMIT-FREQ",
                $"Requested frequency {hz:G6} Hz on '{role}' is below registered hard minimum {minF.Hertz:G6} Hz.",
                $"Keep '{role}' frequency >= {minF.Hertz:G6} Hz.");
        }
        if (MaxFrequency is { } maxF && hz > maxF.Hertz)
        {
            return Fail(role, "BF-HARDLIMIT-FREQ",
                $"Requested frequency {hz:G6} Hz on '{role}' exceeds registered hard maximum {maxF.Hertz:G6} Hz.",
                $"Keep '{role}' frequency <= {maxF.Hertz:G6} Hz.");
        }
        return BenchResult.Ok();
    }

    /// <summary>Verify that a requested thermal setpoint lies within the registered hard temperature limits.</summary>
    /// <param name="role">Target instrument role.</param>
    /// <param name="requested">Requested temperature setpoint.</param>
    /// <returns>Success or a structured <see cref="BenchErrorKind.EnvelopeViolation"/> result.</returns>
    public BenchResult CheckTemperature(string role, Temperature requested)
    {
        double c = requested.DegreesCelsius;
        if (!double.IsFinite(c))
            return Fail(role, "BF-HARDLIMIT-TEMP", $"Non-finite temperature rejected on '{role}'.", "Supply a finite temperature.");
        if (MinTemperature is { } minT && c < minT.DegreesCelsius)
        {
            return Fail(role, "BF-HARDLIMIT-TEMP",
                $"Requested temperature {c:G6} C on '{role}' is below registered hard minimum {minT.DegreesCelsius:G6} C.",
                $"Keep '{role}' temperature >= {minT.DegreesCelsius:G6} C.");
        }
        if (MaxTemperature is { } maxT && c > maxT.DegreesCelsius)
        {
            return Fail(role, "BF-HARDLIMIT-TEMP",
                $"Requested temperature {c:G6} C on '{role}' exceeds registered hard maximum {maxT.DegreesCelsius:G6} C.",
                $"Keep '{role}' temperature <= {maxT.DegreesCelsius:G6} C.");
        }
        return BenchResult.Ok();
    }

    /// <summary>Verify that a requested load resistance lies within registered hard resistance bounds.</summary>
    /// <param name="role">Target instrument role.</param>
    /// <param name="requested">Requested resistance.</param>
    /// <returns>Success or a structured <see cref="BenchErrorKind.EnvelopeViolation"/> result.</returns>
    public BenchResult CheckResistance(string role, ElectricResistance requested)
    {
        double ohm = requested.Ohms;
        if (!double.IsFinite(ohm) || ohm < 0)
            return Fail(role, "BF-HARDLIMIT-RES", $"Invalid resistance rejected on '{role}'.", "Supply a finite non-negative resistance.");
        if (MinResistance is { } minR && ohm < minR.Ohms)
        {
            return Fail(role, "BF-HARDLIMIT-RES",
                $"Requested resistance {ohm:G6} Ohm on '{role}' is below registered hard minimum {minR.Ohms:G6} Ohm (short-circuit protection).",
                $"Keep '{role}' resistance >= {minR.Ohms:G6} Ohm.");
        }
        if (MaxResistance is { } maxR && ohm > maxR.Ohms)
        {
            return Fail(role, "BF-HARDLIMIT-RES",
                $"Requested resistance {ohm:G6} Ohm on '{role}' exceeds registered hard maximum {maxR.Ohms:G6} Ohm.",
                $"Keep '{role}' resistance <= {maxR.Ohms:G6} Ohm.");
        }
        return BenchResult.Ok();
    }

    /// <summary>Convert a legacy <see cref="FlightSafeEnvelope"/> into a universal <see cref="InstrumentHardLimits"/> policy.</summary>
    /// <param name="envelope">Flight safe operating envelope.</param>
    /// <returns>Equivalent <see cref="InstrumentHardLimits"/>.</returns>
    public static InstrumentHardLimits FromFlightEnvelope(FlightSafeEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        envelope.Validate();
        return new InstrumentHardLimits(
            MaxVoltage: envelope.MaxVoltage,
            MinVoltage: envelope.MinVoltage ?? ElectricPotential.Zero,
            MaxCurrent: envelope.MaxCurrent,
            MaxPower: envelope.MaxPower,
            RequireComplianceBeforeOutputOn: envelope.RequireComplianceBeforeOutputOn);
    }

    private static BenchResult Fail(string role, string code, string message, string remedy) =>
        BenchResult.Fail(BenchError.EnvelopeViolation(code, message, role, remedy));

    private static T? MinNullable<T>(T? a, T? b, Func<T, double> selector) where T : struct =>
        a is null ? b : b is null ? a : (selector(a.Value) <= selector(b.Value) ? a : b);

    private static T? MaxNullable<T>(T? a, T? b, Func<T, double> selector) where T : struct =>
        a is null ? b : b is null ? a : (selector(a.Value) >= selector(b.Value) ? a : b);
}

/// <summary>
/// Honest provenance report of instrument-resident hardware protection programming (OVP/OCP/OPP).
/// Distinguishes software ceilings from independent hardware crowbar/latch protection and never claims
/// software shutdown executes after host process or power loss (<c>BD-RES-002</c>).
/// </summary>
/// <param name="Role">Target instrument role.</param>
/// <param name="HardwareProtectionSupported">True when the instrument accepted at least one hardware protection register write.</param>
/// <param name="OvpProgrammed">True when hardware over-voltage protection was programmed.</param>
/// <param name="OvpThreshold">Programmed hardware OVP threshold, if applicable.</param>
/// <param name="OcpProgrammed">True when hardware over-current protection was programmed.</param>
/// <param name="OcpThreshold">Programmed hardware OCP threshold, if applicable.</param>
/// <param name="OppProgrammed">True when hardware over-power protection was programmed.</param>
/// <param name="OppThreshold">Programmed hardware OPP threshold, if applicable.</param>
/// <param name="HardwareProtectionVerifiedOnKillTest">True only after physical kill-host qualification evidence exists; defaults to false.</param>
/// <param name="Notes">Honest operator summary including any unavailable protection registers.</param>
public sealed record HardwareProtectionReport(
    string Role,
    bool HardwareProtectionSupported,
    bool OvpProgrammed,
    ElectricPotential? OvpThreshold,
    bool OcpProgrammed,
    ElectricCurrent? OcpThreshold,
    bool OppProgrammed,
    Power? OppThreshold,
    bool HardwareProtectionVerifiedOnKillTest,
    string Notes);

/// <summary>
/// Optional capability implemented by instruments or adapters that can program hardware-resident
/// over-voltage, over-current, or over-power protection registers directly inside the instrument.
/// </summary>
public interface IHardwareLimitProgrammable
{
    /// <summary>Program registered hard maximums into the instrument's internal hardware protection registers.</summary>
    /// <param name="limits">Registered final fail-safe limits.</param>
    /// <param name="channel">One-based instrument channel index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing an honest <see cref="HardwareProtectionReport"/>.</returns>
    Task<BenchResult<HardwareProtectionReport>> ProgramHardwareLimitsAsync(
        InstrumentHardLimits limits,
        int channel = 1,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Standard SCPI helper that programs instrument-resident OVP/OCP/OPP hardware protection registers
/// and reports truthfully when a transport or mock does not expose hardware protection.
/// </summary>
public static class ScpiHardwareLimitProgrammer
{
    /// <summary>Write OVP/OCP/OPP hardware protection thresholds to a SCPI session and return an honest protection report.</summary>
    /// <param name="session">Caller-owned SCPI session.</param>
    /// <param name="role">Logical bench role identifier.</param>
    /// <param name="limits">Registered hard limits to program into hardware registers.</param>
    /// <param name="channel">One-based instrument channel index.</param>
    /// <param name="useChannelPrefix">True to emit <c>SOUR{channel}:*</c>; false for single-channel <c>SOUR:*</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing <see cref="HardwareProtectionReport"/>.</returns>
    public static async Task<BenchResult<HardwareProtectionReport>> ProgramAsync(
        IAsyncScpiSession session,
        string role,
        InstrumentHardLimits limits,
        int channel = 1,
        bool useChannelPrefix = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        if (session is DefinitionDrivenScpiMockSession)
        {
            return BenchResult<HardwareProtectionReport>.Ok(new HardwareProtectionReport(
                role,
                HardwareProtectionSupported: false,
                OvpProgrammed: false,
                OvpThreshold: null,
                OcpProgrammed: false,
                OcpThreshold: null,
                OppProgrammed: false,
                OppThreshold: null,
                HardwareProtectionVerifiedOnKillTest: false,
                Notes: "Definition-driven simulation mock has no independent hardware crowbar/latch protection; software hard limits enforced only while process runs."));
        }

        string prefix = useChannelPrefix ? $"SOUR{channel}" : "SOUR";
        bool ovp = false, ocp = false, opp = false;
        ElectricPotential? ovpVal = null;
        ElectricCurrent? ocpVal = null;
        Power? oppVal = null;
        var notes = new List<string>();

        try
        {
            if (limits.MaxVoltage is { } maxV)
            {
                double threshold = maxV.Volts * limits.HardwareProtectionMarginFactor;
                await session.WriteAsync($"{prefix}:VOLT:PROT {threshold.ToString("G17", CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
                ovp = true;
                ovpVal = ElectricPotential.FromVolts(threshold);
                notes.Add($"OVP={threshold:G6}V");
            }
            if (limits.MaxCurrent is { } maxI)
            {
                double threshold = maxI.Amperes * limits.HardwareProtectionMarginFactor;
                await session.WriteAsync($"{prefix}:CURR:PROT {threshold.ToString("G17", CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
                ocp = true;
                ocpVal = ElectricCurrent.FromAmperes(threshold);
                notes.Add($"OCP={threshold:G6}A");
            }
            if (limits.MaxPower is { } maxP)
            {
                double threshold = (double)maxP.Watts * limits.HardwareProtectionMarginFactor;
                await session.WriteAsync($"{prefix}:POW:PROT {threshold.ToString("G17", CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
                opp = true;
                oppVal = Power.FromWatts((decimal)threshold);
                notes.Add($"OPP={threshold:G6}W");
            }

            bool supported = ovp || ocp || opp;
            return BenchResult<HardwareProtectionReport>.Ok(new HardwareProtectionReport(
                role,
                HardwareProtectionSupported: supported,
                OvpProgrammed: ovp,
                OvpThreshold: ovpVal,
                OcpProgrammed: ocp,
                OcpThreshold: ocpVal,
                OppProgrammed: opp,
                OppThreshold: oppVal,
                HardwareProtectionVerifiedOnKillTest: false,
                Notes: supported
                    ? $"Programmed SCPI hardware protection ({string.Join(", ", notes)}). Kill-host physical verification still required."
                    : "No electrical protection ceilings declared in InstrumentHardLimits."));
        }
        catch (Exception ex)
        {
            return BenchResult<HardwareProtectionReport>.Fail(new BenchError(
                "BF-HWPROT-FAIL",
                BenchErrorKind.TransportFault,
                $"Failed to program hardware protection registers on '{role}': {ex.Message}",
                role,
                Confidence: PhysicalStateConfidence.UnknownRequiresRecovery,
                RequiresRecovery: true,
                Remedy: $"Verify OVP/OCP/OPP status directly on '{role}' before energizing any output."));
        }
    }
}

/// <summary>
/// Thread-safe, monotonically tightening registry of final fail-safe hard maximums registered per instrument role.
/// Once limits are registered on an instrument (or the registry is sealed), ceilings can only be tightened—never
/// raised, widened, or deleted—protecting flight hardware from bugs or runaway automation.
/// </summary>
public sealed class HardLimitRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, InstrumentHardLimits> _limits = new(StringComparer.OrdinalIgnoreCase);
    private bool _sealed;

    /// <summary>True once <see cref="Seal"/> has locked this registry against any new removals or relaxations.</summary>
    public bool IsSealed
    {
        get { using (_gate.EnterScope()) return _sealed; }
    }

    /// <summary>Permanently seal the registry; existing limits may still be tightened but never relaxed or cleared.</summary>
    public void Seal()
    {
        using (_gate.EnterScope())
            _sealed = true;
    }

    /// <summary>Register or monotonically tighten hard limits for an instrument role.</summary>
    /// <param name="role">Logical bench role or canonical resource identifier.</param>
    /// <param name="limits">Validated hard operating limits.</param>
    /// <returns>Railroad result containing the effective active limits, or <see cref="BenchErrorKind.Forbidden"/> if attempting to relax existing limits.</returns>
    public BenchResult<InstrumentHardLimits> TryRegister(string role, InstrumentHardLimits limits)
    {
        if (string.IsNullOrWhiteSpace(role))
            return BenchResult<InstrumentHardLimits>.Fail(BenchError.Validation("BF-HARDLIMIT-ROLE", "Instrument role must be non-empty."));
        if (limits is null)
            return BenchResult<InstrumentHardLimits>.Fail(BenchError.Validation("BF-HARDLIMIT-NULL", "InstrumentHardLimits must not be null.", role));

        try
        {
            limits.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BenchResult<InstrumentHardLimits>.Fail(BenchError.Validation("BF-HARDLIMIT-INVALID", ex.Message, role));
        }

        using (_gate.EnterScope())
        {
            if (_limits.TryGetValue(role, out var existing))
            {
                if (existing.WouldBeRelaxedBy(limits))
                {
                    return BenchResult<InstrumentHardLimits>.Fail(new BenchError(
                        "BF-HARDLIMIT-RELAX-DENIED",
                        BenchErrorKind.Forbidden,
                        $"Attempted to relax or remove already-registered hard limits on '{role}'. Registered maximums can only be tightened.",
                        role,
                        Remedy: "Create a new session under operator supervision to alter flight-hardware ceilings, or supply strictly tighter bounds."));
                }
                var tightened = existing.TightenWith(limits);
                _limits[role] = tightened;
                return BenchResult<InstrumentHardLimits>.Ok(tightened);
            }

            _limits[role] = limits;
            return BenchResult<InstrumentHardLimits>.Ok(limits);
        }
    }

    /// <summary>Monotonically tighten registered hard limits on <paramref name="role"/> by intersecting with <paramref name="stricter"/>.</summary>
    /// <param name="role">Logical bench role or canonical resource identifier.</param>
    /// <param name="stricter">Additional tighter constraints.</param>
    /// <returns>Railroad result containing the new tightened limits.</returns>
    public BenchResult<InstrumentHardLimits> TryTighten(string role, InstrumentHardLimits stricter)
    {
        if (string.IsNullOrWhiteSpace(role))
            return BenchResult<InstrumentHardLimits>.Fail(BenchError.Validation("BF-HARDLIMIT-ROLE", "Instrument role must be non-empty."));
        if (stricter is null)
            return BenchResult<InstrumentHardLimits>.Fail(BenchError.Validation("BF-HARDLIMIT-NULL", "InstrumentHardLimits must not be null.", role));

        try
        {
            stricter.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BenchResult<InstrumentHardLimits>.Fail(BenchError.Validation("BF-HARDLIMIT-INVALID", ex.Message, role));
        }

        using (_gate.EnterScope())
        {
            var effective = _limits.TryGetValue(role, out var existing)
                ? existing.TightenWith(stricter)
                : stricter;
            _limits[role] = effective;
            return BenchResult<InstrumentHardLimits>.Ok(effective);
        }
    }

    /// <summary>Look up the currently registered hard limits for <paramref name="role"/>, if any.</summary>
    /// <param name="role">Logical bench role or canonical resource identifier.</param>
    /// <returns>Registered <see cref="InstrumentHardLimits"/> or null when none are registered.</returns>
    public InstrumentHardLimits? TryGet(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        using (_gate.EnterScope())
            return _limits.TryGetValue(role, out var limits) ? limits : null;
    }

    /// <summary>Return a detached read-only snapshot of all registered hard limits.</summary>
    /// <returns>Dictionary mapping instrument role to active <see cref="InstrumentHardLimits"/>.</returns>
    public IReadOnlyDictionary<string, InstrumentHardLimits> Snapshot()
    {
        using (_gate.EnterScope())
            return new Dictionary<string, InstrumentHardLimits>(_limits, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Create an <see cref="IInstrumentOperationGuard"/> that enforces this registry on every SCPI write.</summary>
    /// <param name="enforceOnSimulation">True to enforce registered maximums on simulation sessions as well as physical hardware.</param>
    /// <param name="requireRegisteredLimitsForPhysical">When true, blocks physical writes/output-control unless limits are registered for that resource.</param>
    /// <returns>Configured <see cref="HardLimitScpiGuard"/>.</returns>
    public HardLimitScpiGuard ToScpiGuard(bool enforceOnSimulation = true, bool requireRegisteredLimitsForPhysical = false) =>
        new(this, enforceOnSimulation, requireRegisteredLimitsForPhysical);
}

/// <summary>
/// Pre-dispatch <see cref="IInstrumentOperationGuard"/> that intercepts SCPI setpoint writes (direct, generated,
/// or remote) and blocks any command violating the instrument's registered <see cref="InstrumentHardLimits"/> before
/// any bytes reach the transport.
/// </summary>
public sealed class HardLimitScpiGuard : IInstrumentOperationGuard
{
    private static readonly Regex TrailingNumberRegex = new(
        @"(?<num>[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*$",
        RegexOptions.Compiled);

    private readonly HardLimitRegistry _registry;
    private readonly bool _enforceOnSimulation;
    private readonly bool _requireRegisteredLimitsForPhysical;

    /// <summary>Create a pre-dispatch SCPI hard-limit guard backed by <paramref name="registry"/>.</summary>
    /// <param name="registry">Authoritative registered hard-limit registry.</param>
    /// <param name="enforceOnSimulation">True to enforce limits on simulated instruments as well as physical hardware.</param>
    /// <param name="requireRegisteredLimitsForPhysical">When true, fails closed on physical actuation if no hard limits are registered for the resource.</param>
    public HardLimitScpiGuard(
        HardLimitRegistry registry,
        bool enforceOnSimulation = true,
        bool requireRegisteredLimitsForPhysical = false)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _enforceOnSimulation = enforceOnSimulation;
        _requireRegisteredLimitsForPhysical = requireRegisteredLimitsForPhysical;
    }

    /// <inheritdoc/>
    public ValueTask<OperationGuardDecision> EvaluateAsync(
        InstrumentOperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Kind is InstrumentOperationKind.Query or InstrumentOperationKind.Recovery)
            return ValueTask.FromResult(OperationGuardDecision.Allow("BF-HARDLIMIT-SCPI"));

        if (!context.IsPhysicalHardware && !_enforceOnSimulation)
            return ValueTask.FromResult(OperationGuardDecision.Allow("BF-HARDLIMIT-SCPI"));

        var limits = _registry.TryGet(context.InstrumentId);
        if (limits is null)
        {
            if (context.IsPhysicalHardware && _requireRegisteredLimitsForPhysical)
            {
                return ValueTask.FromResult(OperationGuardDecision.Deny(
                    "BF-HARDLIMIT-UNREGISTERED",
                    $"Physical actuation on '{context.InstrumentId}' is blocked because no fail-safe InstrumentHardLimits are registered for this instrument."));
            }
            return ValueTask.FromResult(OperationGuardDecision.Allow("BF-HARDLIMIT-SCPI"));
        }

        string cmd = context.OperationName.Trim();
        if (cmd.EndsWith('?'))
            return ValueTask.FromResult(OperationGuardDecision.Allow("BF-HARDLIMIT-SCPI"));

        var match = TrailingNumberRegex.Match(cmd);
        if (!match.Success || !double.TryParse(match.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return ValueTask.FromResult(OperationGuardDecision.Allow("BF-HARDLIMIT-SCPI"));

        string upper = cmd.ToUpperInvariant();
        bool isProtection = upper.Contains(":PROT", StringComparison.Ordinal);
        double scale = isProtection ? limits.HardwareProtectionMarginFactor : 1.0;

        BenchResult check = BenchResult.Ok();
        if (upper.Contains("VOLT", StringComparison.Ordinal) || upper.Contains("SLVL", StringComparison.Ordinal))
        {
            if (isProtection && limits.MaxVoltage is { } maxV)
            {
                double maxProt = maxV.Volts * scale;
                if (value > maxProt)
                    check = BenchResult.Fail(BenchError.EnvelopeViolation("BF-HARDLIMIT-OVP", $"Hardware OVP {value:G6} V exceeds allowed ceiling {maxProt:G6} V on '{context.InstrumentId}'.", context.InstrumentId, $"Keep hardware OVP <= {maxProt:G6} V on '{context.InstrumentId}'."));
            }
            else
            {
                check = limits.CheckVoltage(context.InstrumentId, ElectricPotential.FromVolts(value));
            }
        }
        else if (upper.Contains("CURR", StringComparison.Ordinal))
        {
            if (isProtection && limits.MaxCurrent is { } maxI)
            {
                double maxProt = maxI.Amperes * scale;
                if (value > maxProt)
                    check = BenchResult.Fail(BenchError.EnvelopeViolation("BF-HARDLIMIT-OCP", $"Hardware OCP {value:G6} A exceeds allowed ceiling {maxProt:G6} A on '{context.InstrumentId}'.", context.InstrumentId, $"Keep hardware OCP <= {maxProt:G6} A on '{context.InstrumentId}'."));
            }
            else
            {
                check = limits.CheckCurrent(context.InstrumentId, ElectricCurrent.FromAmperes(value));
            }
        }
        else if (upper.Contains("POW", StringComparison.Ordinal))
        {
            if (value < 0)
                check = BenchResult.Fail(BenchError.EnvelopeViolation("BF-HARDLIMIT-POWER", $"Negative power {value:G6} W rejected on '{context.InstrumentId}'.", context.InstrumentId, "Supply a non-negative power level."));
            else if (isProtection && limits.MaxPower is { } maxP)
            {
                double maxProt = (double)maxP.Watts * scale;
                if (value > maxProt)
                    check = BenchResult.Fail(BenchError.EnvelopeViolation("BF-HARDLIMIT-OPP", $"Hardware OPP {value:G6} W exceeds allowed ceiling {maxProt:G6} W on '{context.InstrumentId}'.", context.InstrumentId, $"Keep hardware OPP <= {maxProt:G6} W on '{context.InstrumentId}'."));
            }
            else
            {
                check = limits.CheckPower(context.InstrumentId, Power.FromWatts((decimal)value));
            }
        }
        else if (upper.Contains("FREQ", StringComparison.Ordinal))
        {
            check = limits.CheckFrequency(context.InstrumentId, Frequency.FromHertz(value));
        }
        else if (upper.Contains("TEMP", StringComparison.Ordinal) || upper.StartsWith("SETP", StringComparison.Ordinal))
        {
            check = limits.CheckTemperature(context.InstrumentId, Temperature.FromDegreesCelsius(value));
        }
        else if (upper.Contains("RES", StringComparison.Ordinal) && !upper.Contains("BAND:RES", StringComparison.Ordinal))
        {
            check = limits.CheckResistance(context.InstrumentId, ElectricResistance.FromOhms(value));
        }

        return ValueTask.FromResult(check.IsSuccess
            ? OperationGuardDecision.Allow("BF-HARDLIMIT-SCPI")
            : OperationGuardDecision.Deny(check.Error.Code, check.Error.Message));
    }
}

/// <summary>
/// Universal decorator factory wrapping any output-capable <see cref="IBenchInstrument"/> with
/// live, monotonically-tightening <see cref="InstrumentHardLimits"/> enforcement.
/// </summary>
public static class InstrumentHardLimitWrapper
{
    /// <summary>Wrap <paramref name="instrument"/> in the appropriate hard-limited proxy for its canonical interface.</summary>
    /// <param name="instrument">Target physical, generated, official-IVI, or mock instrument.</param>
    /// <param name="limits">Registered final fail-safe limits.</param>
    /// <param name="registry">Optional live <see cref="HardLimitRegistry"/> so subsequent runtime tightenings apply immediately.</param>
    /// <returns>Wrapped instrument enforcing <paramref name="limits"/> before every write or output enable.</returns>
    public static IBenchInstrument Wrap(
        IBenchInstrument instrument,
        InstrumentHardLimits limits,
        HardLimitRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        Func<InstrumentHardLimits> resolve = () => registry?.TryGet(instrument.Role) ?? limits;

        return instrument switch
        {
            IBenchSourceMeasureUnit smu => new HardLimitedSourceMeasureUnit(smu, resolve),
            IBenchPowerSupply psu => new HardLimitedPowerSupply(psu, resolve),
            IBenchThermalController thermal => new HardLimitedThermalController(thermal, resolve),
            IBenchFunctionGenerator fgen => new HardLimitedFunctionGenerator(fgen, resolve),
            IBenchElectronicLoad eload => new HardLimitedElectronicLoad(eload, resolve),
            IBenchRfSignalGenerator rfsg => new HardLimitedRfSignalGenerator(rfsg, resolve),
            IBenchLcrMeter lcr => new HardLimitedLcrMeter(lcr, resolve),
            _ => instrument
        };
    }

    internal static async Task<BenchResult> RejectAndMaybeSafeAsync(
        IBenchInstrument inner,
        InstrumentHardLimits limits,
        BenchResult failure,
        CancellationToken cancellationToken)
    {
        if (limits.AutoSafeOnViolation)
            await inner.SafeStateAsync(cancellationToken).ConfigureAwait(false);
        return failure;
    }
}

/// <summary>Final fail-safe proxy wrapping any <see cref="IBenchPowerSupply"/> with hard voltage, current, and power ceilings.</summary>
public class HardLimitedPowerSupply : IBenchPowerSupply, IHardwareLimitProgrammable
{
    private readonly IBenchPowerSupply _inner;
    private readonly Func<InstrumentHardLimits> _limits;
    private readonly Lock _gate = new();
    private bool _complianceSet;
    private ElectricCurrent? _lastCurrentLimit;
    private ElectricPotential? _lastVoltage;

    /// <summary>Wrap a power supply with hard fail-safe limit enforcement.</summary>
    /// <param name="inner">Underlying physical, generated, IVI, or mock power supply.</param>
    /// <param name="limitsAccessor">Delegate returning the active monotonically-tightened limits.</param>
    public HardLimitedPowerSupply(IBenchPowerSupply inner, Func<InstrumentHardLimits> limitsAccessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limits = limitsAccessor ?? throw new ArgumentNullException(nameof(limitsAccessor));
    }

    /// <summary>Active hard limits currently enforced on this power supply.</summary>
    public InstrumentHardLimits ActiveLimits => _limits();
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
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => _inner.SafeStateAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetVoltageAsync(ElectricPotential voltage, int channel = 1, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        ElectricCurrent? cur;
        using (_gate.EnterScope()) cur = _lastCurrentLimit;
        var check = limits.CheckVoltage(Role, voltage, cur);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);

        var res = await _inner.SetVoltageAsync(voltage, channel, cancellationToken).ConfigureAwait(false);
        if (res.IsSuccess)
            using (_gate.EnterScope()) _lastVoltage = voltage;
        return res;
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.GetVoltageAsync(channel, cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetCurrentLimitAsync(ElectricCurrent currentLimit, int channel = 1, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        ElectricPotential? volt;
        using (_gate.EnterScope()) volt = _lastVoltage;
        var check = limits.CheckCurrent(Role, currentLimit, volt);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);

        var res = await _inner.SetCurrentLimitAsync(currentLimit, channel, cancellationToken).ConfigureAwait(false);
        if (res.IsSuccess)
        {
            using (_gate.EnterScope())
            {
                _complianceSet = true;
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
        var limits = _limits();
        if (enabled && limits.RequireComplianceBeforeOutputOn)
        {
            bool set;
            using (_gate.EnterScope()) set = _complianceSet;
            if (!set)
            {
                return Task.FromResult(BenchResult.Fail(BenchError.EnvelopeViolation(
                    "BF-HARDLIMIT-NO-COMPLIANCE",
                    $"Refusing to energize '{Role}' before an explicit current limit is programmed.",
                    Role,
                    $"Call SetCurrentLimitAsync(...) within registered limits before SetOutputEnabledAsync(true) on '{Role}'.")));
            }
        }
        return _inner.SetOutputEnabledAsync(enabled, channel, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<BenchResult<bool>> GetOutputEnabledAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.GetOutputEnabledAsync(channel, cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult<HardwareProtectionReport>> ProgramHardwareLimitsAsync(
        InstrumentHardLimits limits,
        int channel = 1,
        CancellationToken cancellationToken = default)
    {
        if (_inner is IHardwareLimitProgrammable programmable)
            return programmable.ProgramHardwareLimitsAsync(limits, channel, cancellationToken);

        return Task.FromResult(BenchResult<HardwareProtectionReport>.Ok(new HardwareProtectionReport(
            Role,
            HardwareProtectionSupported: false,
            OvpProgrammed: false,
            OvpThreshold: null,
            OcpProgrammed: false,
            OcpThreshold: null,
            OppProgrammed: false,
            OppThreshold: null,
            HardwareProtectionVerifiedOnKillTest: false,
            Notes: $"Underlying {Backend} power supply '{Identity.Model}' does not expose IHardwareLimitProgrammable; software fail-safe ceilings remain active.")));
    }
}

/// <summary>Final fail-safe proxy wrapping any <see cref="IBenchSourceMeasureUnit"/> with hard voltage, current, and power ceilings.</summary>
public sealed class HardLimitedSourceMeasureUnit : HardLimitedPowerSupply, IBenchSourceMeasureUnit
{
    private readonly IBenchSourceMeasureUnit _innerSmu;

    /// <summary>Wrap an SMU with hard fail-safe limit enforcement.</summary>
    /// <param name="inner">Underlying SMU.</param>
    /// <param name="limitsAccessor">Delegate returning active monotonically-tightened limits.</param>
    public HardLimitedSourceMeasureUnit(IBenchSourceMeasureUnit inner, Func<InstrumentHardLimits> limitsAccessor)
        : base(inner, limitsAccessor)
    {
        _innerSmu = inner;
    }

    /// <inheritdoc/>
    public Task<BenchResult<bool>> IsComplianceTrippedAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _innerSmu.IsComplianceTrippedAsync(channel, cancellationToken);
}

/// <summary>Final fail-safe proxy wrapping any <see cref="IBenchThermalController"/> with hard temperature bounds.</summary>
public sealed class HardLimitedThermalController : IBenchThermalController
{
    private readonly IBenchThermalController _inner;
    private readonly Func<InstrumentHardLimits> _limits;

    /// <summary>Wrap a thermal controller with hard temperature limits.</summary>
    /// <param name="inner">Underlying thermal controller.</param>
    /// <param name="limitsAccessor">Delegate returning active monotonically-tightened limits.</param>
    public HardLimitedThermalController(IBenchThermalController inner, Func<InstrumentHardLimits> limitsAccessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limits = limitsAccessor ?? throw new ArgumentNullException(nameof(limitsAccessor));
    }

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
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => _inner.SafeStateAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetSetpointAsync(Temperature target, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        var check = limits.CheckTemperature(Role, target);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);
        return await _inner.SetSetpointAsync(target, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<BenchResult<Temperature>> MeasureTemperatureAsync(CancellationToken cancellationToken = default) =>
        _inner.MeasureTemperatureAsync(cancellationToken);
}

/// <summary>Final fail-safe proxy wrapping any <see cref="IBenchFunctionGenerator"/> with hard frequency and amplitude ceilings.</summary>
public sealed class HardLimitedFunctionGenerator : IBenchFunctionGenerator
{
    private readonly IBenchFunctionGenerator _inner;
    private readonly Func<InstrumentHardLimits> _limits;

    /// <summary>Wrap a function generator with hard frequency and amplitude limits.</summary>
    /// <param name="inner">Underlying function generator.</param>
    /// <param name="limitsAccessor">Delegate returning active monotonically-tightened limits.</param>
    public HardLimitedFunctionGenerator(IBenchFunctionGenerator inner, Func<InstrumentHardLimits> limitsAccessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limits = limitsAccessor ?? throw new ArgumentNullException(nameof(limitsAccessor));
    }

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
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => _inner.SafeStateAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetFrequencyAsync(Frequency frequency, int channel = 1, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        var check = limits.CheckFrequency(Role, frequency);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);
        return await _inner.SetFrequencyAsync(frequency, channel, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetFrequencyAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.GetFrequencyAsync(channel, cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetAmplitudeAsync(ElectricPotential amplitude, int channel = 1, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        var check = limits.CheckVoltage(Role, amplitude);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);
        return await _inner.SetAmplitudeAsync(amplitude, channel, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> GetAmplitudeAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.GetAmplitudeAsync(channel, cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult> SetOutputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.SetOutputEnabledAsync(enabled, channel, cancellationToken);
}

/// <summary>Final fail-safe proxy wrapping any <see cref="IBenchElectronicLoad"/> with hard current and resistance bounds.</summary>
public sealed class HardLimitedElectronicLoad : IBenchElectronicLoad
{
    private readonly IBenchElectronicLoad _inner;
    private readonly Func<InstrumentHardLimits> _limits;

    /// <summary>Wrap an electronic load with hard current and resistance limits.</summary>
    /// <param name="inner">Underlying electronic load.</param>
    /// <param name="limitsAccessor">Delegate returning active monotonically-tightened limits.</param>
    public HardLimitedElectronicLoad(IBenchElectronicLoad inner, Func<InstrumentHardLimits> limitsAccessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limits = limitsAccessor ?? throw new ArgumentNullException(nameof(limitsAccessor));
    }

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
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => _inner.SafeStateAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetCurrentSinkAsync(ElectricCurrent current, int channel = 1, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        var check = limits.CheckCurrent(Role, current);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);
        return await _inner.SetCurrentSinkAsync(current, channel, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BenchResult> SetResistanceLoadAsync(ElectricResistance resistance, int channel = 1, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        var check = limits.CheckResistance(Role, resistance);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);
        return await _inner.SetResistanceLoadAsync(resistance, channel, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricPotential>> MeasureVoltageAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.MeasureVoltageAsync(channel, cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<ElectricCurrent>> MeasureCurrentAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.MeasureCurrentAsync(channel, cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult<Power>> MeasurePowerAsync(int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.MeasurePowerAsync(channel, cancellationToken);
    /// <inheritdoc/>
    public Task<BenchResult> SetInputEnabledAsync(bool enabled, int channel = 1, CancellationToken cancellationToken = default) =>
        _inner.SetInputEnabledAsync(enabled, channel, cancellationToken);
}

/// <summary>Final fail-safe proxy wrapping any <see cref="IBenchRfSignalGenerator"/> with hard RF frequency and power ceilings.</summary>
public sealed class HardLimitedRfSignalGenerator : IBenchRfSignalGenerator
{
    private readonly IBenchRfSignalGenerator _inner;
    private readonly Func<InstrumentHardLimits> _limits;

    /// <summary>Wrap an RF signal generator with hard carrier frequency and power limits.</summary>
    /// <param name="inner">Underlying RF signal generator.</param>
    /// <param name="limitsAccessor">Delegate returning active monotonically-tightened limits.</param>
    public HardLimitedRfSignalGenerator(IBenchRfSignalGenerator inner, Func<InstrumentHardLimits> limitsAccessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limits = limitsAccessor ?? throw new ArgumentNullException(nameof(limitsAccessor));
    }

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
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => _inner.SafeStateAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        var check = limits.CheckFrequency(Role, frequency);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);
        return await _inner.SetFrequencyAsync(frequency, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<BenchResult<Frequency>> GetFrequencyAsync(CancellationToken cancellationToken = default) =>
        _inner.GetFrequencyAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetOutputPowerAsync(Power power, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        var check = limits.CheckPower(Role, power);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);
        return await _inner.SetOutputPowerAsync(power, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<BenchResult<Power>> GetOutputPowerAsync(CancellationToken cancellationToken = default) =>
        _inner.GetOutputPowerAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult> SetRfOutputEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        _inner.SetRfOutputEnabledAsync(enabled, cancellationToken);
}

/// <summary>Final fail-safe proxy wrapping any <see cref="IBenchLcrMeter"/> with hard AC stimulus frequency and voltage ceilings.</summary>
public sealed class HardLimitedLcrMeter : IBenchLcrMeter
{
    private readonly IBenchLcrMeter _inner;
    private readonly Func<InstrumentHardLimits> _limits;

    /// <summary>Wrap an LCR meter with hard stimulus frequency and voltage limits.</summary>
    /// <param name="inner">Underlying LCR meter.</param>
    /// <param name="limitsAccessor">Delegate returning active monotonically-tightened limits.</param>
    public HardLimitedLcrMeter(IBenchLcrMeter inner, Func<InstrumentHardLimits> limitsAccessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limits = limitsAccessor ?? throw new ArgumentNullException(nameof(limitsAccessor));
    }

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
    public Task<BenchResult> SafeStateAsync(CancellationToken cancellationToken = default) => _inner.SafeStateAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<BenchResult> SetTestFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        var check = limits.CheckFrequency(Role, frequency);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);
        return await _inner.SetTestFrequencyAsync(frequency, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BenchResult> SetTestVoltageAsync(ElectricPotential voltage, CancellationToken cancellationToken = default)
    {
        var limits = _limits();
        var check = limits.CheckVoltage(Role, voltage);
        if (check.IsFailure)
            return await InstrumentHardLimitWrapper.RejectAndMaybeSafeAsync(_inner, limits, check, cancellationToken).ConfigureAwait(false);
        return await _inner.SetTestVoltageAsync(voltage, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<BenchResult<ElectricCapacitance>> MeasureCapacitanceAsync(CancellationToken cancellationToken = default) =>
        _inner.MeasureCapacitanceAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<BenchResult<ElectricResistance>> MeasureResistanceAsync(CancellationToken cancellationToken = default) =>
        _inner.MeasureResistanceAsync(cancellationToken);
}
