using System.Net;
using System.Text;
using BenchForge.Simulation;

namespace BenchForge.Flight;

/// <summary>
/// Generates a zero-dependency, self-contained HTML Flight Acceptance Evidence Report from a sealed
/// <see cref="FlightEvidencePackage"/>, registered <see cref="HardLimitRegistry"/> ceilings,
/// <see cref="HardwareProtectionReport"/>s, simulation clock status, and optional <see cref="FlightTraceBundle"/>.
/// </summary>
public static class FlightAcceptanceReportGenerator
{
    /// <summary>Render a standalone HTML Flight Acceptance Evidence Report with all dynamic values HTML-escaped.</summary>
    /// <param name="evidence">Sealed flight evidence package.</param>
    /// <param name="hardLimits">Sealed registered instrument fail-safe limits.</param>
    /// <param name="hardwareProtection">Optional hardware protection programming reports (<c>BD-RES-002</c>).</param>
    /// <param name="clockStatus">Optional simulation clock status snapshot.</param>
    /// <param name="trace">Optional hash-chained flight trace bundle.</param>
    /// <returns>Complete standalone HTML5 document string.</returns>
    public static string RenderHtml(
        FlightEvidencePackage evidence,
        HardLimitRegistry hardLimits,
        IReadOnlyList<HardwareProtectionReport>? hardwareProtection = null,
        SimulationClockStatus? clockStatus = null,
        FlightTraceBundle? trace = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(hardLimits);

        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine($"<title>BenchForge Flight Acceptance Report — {E(evidence.UnitUnderTestSerial)}</title>");
        sb.AppendLine("<style>body{font-family:system-ui,-apple-system,sans-serif;background:#0f172a;color:#e2e8f0;margin:0;padding:24px;line-height:1.5}main{max-width:1100px;margin:0 auto}h1,h2{color:#f8fafc;margin-top:24px}.card{background:#1e293b;border:1px solid #334155;border-radius:10px;padding:16px 20px;margin-bottom:18px}.badge{display:inline-block;padding:3px 10px;border-radius:999px;font-weight:600;font-size:.85rem}.pass{background:#14532d;color:#86efac}.fail{background:#7f1d1d;color:#fca5a5}.warn{background:#78350f;color:#fde68a}table{width:100%;border-collapse:collapse;margin-top:10px;font-size:.9rem}th,td{text-align:left;padding:8px 10px;border-bottom:1px solid #334155}th{color:#94a3b8;font-weight:600}code{background:#0f172a;padding:2px 6px;border-radius:4px;font-size:.85rem;color:#38bdf8}</style>");
        sb.AppendLine("</head><body><main>");
        sb.AppendLine($"<h1>Flight Hardware Acceptance Report</h1>");
        sb.AppendLine("<div class=\"card\">");
        sb.AppendLine($"<p><strong>Status:</strong> <span class=\"badge {(evidence.OverallPassed ? "pass" : "fail")}\">{(evidence.OverallPassed ? "PASSED" : "FAILED")}</span> &nbsp; <strong>Seal Valid:</strong> <span class=\"badge {(evidence.VerifySeal() ? "pass" : "fail")}\">{(evidence.VerifySeal() ? "VERIFIED SHA-256" : "INVALID")}</span></p>");
        sb.AppendLine($"<p><strong>UUT Serial:</strong> <code>{E(evidence.UnitUnderTestSerial)}</code> &nbsp; <strong>Bench / Test:</strong> <code>{E(evidence.TestName)}</code> &nbsp; <strong>Operator:</strong> <code>{E(evidence.OperatorPrincipal)}</code></p>");
        sb.AppendLine($"<p><strong>Window (UTC):</strong> {E(evidence.StartedUtc.ToString("O"))} &rarr; {E(evidence.FinishedUtc.ToString("O"))}</p>");
        sb.AppendLine($"<p><strong>Package Seal (SHA-256):</strong> <code>{E(evidence.ManifestSha256)}</code></p>");
        if (clockStatus is not null)
            sb.AppendLine($"<p><strong>Simulation Clock:</strong> mode=<code>{E(clockStatus.Mode.ToString())}</code>, speed=<code>{clockStatus.Speed:G4}x</code>, elapsed=<code>{E(clockStatus.Elapsed.ToString())}</code></p>");
        if (trace is not null)
            sb.AppendLine($"<p><strong>Flight Trace:</strong> <code>{trace.Events.Count}</code> event(s), head=<code>{E(trace.HeadSha256)}</code></p>");
        sb.AppendLine("</div>");

        // Registered Fail-Safe Hard Limits
        sb.AppendLine("<div class=\"card\"><h2>Registered Final Fail-Safe Hard Limits (Sealed, Tighten-Only)</h2>");
        var snap = hardLimits.Snapshot();
        if (snap.Count == 0)
        {
            sb.AppendLine("<p><span class=\"badge warn\">No hard limits registered</span></p>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th>Role</th><th>Voltage (V)</th><th>Current (A)</th><th>Power (W)</th><th>Temp (C)</th><th>Freq (Hz)</th><th>Min R (Ohm)</th><th>Policy Note</th></tr></thead><tbody>");
            foreach (var (role, lim) in snap.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string vStr = lim.MaxVoltage is { } maxV ? $"[{(lim.MinVoltage?.Volts ?? 0):G4}, {maxV.Volts:G4}]" : "-";
                string iStr = lim.MaxCurrent is { } maxI ? $"<= {maxI.Amperes:G4}" : "-";
                string pStr = lim.MaxPower is { } maxP ? $"<= {maxP.Watts:G4}" : "-";
                string tStr = (lim.MinTemperature is not null || lim.MaxTemperature is not null)
                    ? $"[{lim.MinTemperature?.DegreesCelsius.ToString("G4") ?? "-inf"}, {lim.MaxTemperature?.DegreesCelsius.ToString("G4") ?? "+inf"}]" : "-";
                string fStr = (lim.MinFrequency is not null || lim.MaxFrequency is not null)
                    ? $"[{lim.MinFrequency?.Hertz.ToString("G4") ?? "0"}, {lim.MaxFrequency?.Hertz.ToString("G4") ?? "+inf"}]" : "-";
                string rStr = lim.MinResistance is { } minR ? $">= {minR.Ohms:G4}" : "-";
                sb.AppendLine($"<tr><td><code>{E(role)}</code></td><td>{E(vStr)}</td><td>{E(iStr)}</td><td>{E(pStr)}</td><td>{E(tStr)}</td><td>{E(fStr)}</td><td>{E(rStr)}</td><td>{E(lim.PolicyNote)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</div>");

        // Hardware Protection Boundary (BD-RES-002)
        if (hardwareProtection is { Count: > 0 })
        {
            sb.AppendLine("<div class=\"card\"><h2>External Hardware Protection Boundary (BD-RES-002)</h2>");
            sb.AppendLine("<table><thead><tr><th>Role</th><th>HW Protection Supported</th><th>OVP / OCP / OPP</th><th>Kill-Host Verified</th><th>Notes</th></tr></thead><tbody>");
            foreach (var hp in hardwareProtection)
            {
                string flags = $"{(hp.OvpProgrammed ? "OVP" : "-")}/{(hp.OcpProgrammed ? "OCP" : "-")}/{(hp.OppProgrammed ? "OPP" : "-")}";
                sb.AppendLine($"<tr><td><code>{E(hp.Role)}</code></td><td>{(hp.HardwareProtectionSupported ? "Yes" : "No (Simulation/Unavailable)")}</td><td><code>{E(flags)}</code></td><td>{(hp.HardwareProtectionVerifiedOnKillTest ? "Verified" : "Unverified")}</td><td>{E(hp.Notes)}</td></tr>");
            }
            sb.AppendLine("</tbody></table></div>");
        }

        // Instruments & Provenance
        sb.AppendLine("<div class=\"card\"><h2>Instrument Inventory &amp; Provenance</h2>");
        sb.AppendLine("<table><thead><tr><th>Role</th><th>Class</th><th>Backend</th><th>Model</th><th>Calibration</th></tr></thead><tbody>");
        foreach (var inst in evidence.Instruments)
        {
            string cal = inst.Calibration is { } c ? $"{c.CertificateId} (due {c.DueUtc:yyyy-MM-dd})" : "None";
            sb.AppendLine($"<tr><td><code>{E(inst.Role)}</code></td><td>{E(inst.InstrumentClass)}</td><td><code>{E(inst.Backend.ToString())}</code></td><td>{E(inst.Identity.Manufacturer)} {E(inst.Identity.Model)}</td><td>{E(cal)}</td></tr>");
        }
        sb.AppendLine("</tbody></table></div>");

        // Executed Steps
        sb.AppendLine("<div class=\"card\"><h2>As-Run Verification Steps</h2>");
        sb.AppendLine("<table><thead><tr><th>#</th><th>Step</th><th>Result</th><th>Summary</th><th>Timestamp (UTC)</th></tr></thead><tbody>");
        for (int i = 0; i < evidence.Steps.Count; i++)
        {
            var st = evidence.Steps[i];
            sb.AppendLine($"<tr><td>{i + 1}</td><td>{E(st.StepName)}</td><td><span class=\"badge {(st.Passed ? "pass" : "fail")}\">{(st.Passed ? "PASS" : "FAIL")}</span></td><td>{E(st.Summary)}</td><td><code>{E(st.RecordedUtc.ToString("O"))}</code></td></tr>");
        }
        sb.AppendLine("</tbody></table></div>");

        sb.AppendLine("</main></body></html>");
        return sb.ToString();
    }
}
