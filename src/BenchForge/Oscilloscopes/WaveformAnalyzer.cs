using System.Numerics;
using BenchForge.Outcomes;

namespace BenchForge.Oscilloscopes;

/// <summary>Statistical and signal-integrity metrics computed from a captured voltage waveform.</summary>
/// <param name="SampleCount">Total number of finite samples analyzed.</param>
/// <param name="Min">Minimum sample value in volts.</param>
/// <param name="Max">Maximum sample value in volts.</param>
/// <param name="PeakToPeak">Peak-to-peak excursion (<c>Max - Min</c>) in volts.</param>
/// <param name="Mean">Arithmetic DC mean in volts.</param>
/// <param name="Rms">Total root-mean-square value in volts.</param>
/// <param name="AcRms">AC-coupled RMS ripple around <see cref="Mean"/> in volts.</param>
/// <param name="CrestFactor">Ratio <c>Max(|Min|, |Max|) / Rms</c> (0 when <see cref="Rms"/> is zero).</param>
/// <param name="SimdAccelerated">True when hardware SIMD (<see cref="Vector{T}"/>) accelerated the inner loop.</param>
public sealed record WaveformMetrics(
    int SampleCount,
    double Min,
    double Max,
    double PeakToPeak,
    double Mean,
    double Rms,
    double AcRms,
    double CrestFactor,
    bool SimdAccelerated);

/// <summary>
/// Zero-dependency .NET 11 SIMD (<see cref="Vector{T}"/>) waveform analyzer producing railroad <see cref="BenchResult{T}"/>
/// metrics and flight-hardware power-rail ripple / signal-integrity checks.
/// </summary>
public static class WaveformAnalyzer
{
    /// <summary>Compute min, max, peak-to-peak, DC mean, RMS, AC ripple RMS, and crest factor over <paramref name="samples"/>.</summary>
    /// <param name="samples">Decoded voltage samples in volts.</param>
    /// <param name="resourceId">Optional oscilloscope or channel role for structured error reporting.</param>
    /// <returns>Railroad result containing <see cref="WaveformMetrics"/> or a validation error for empty/non-finite data.</returns>
    public static BenchResult<WaveformMetrics> Analyze(ReadOnlySpan<double> samples, string? resourceId = null)
    {
        if (samples.IsEmpty)
        {
            return BenchResult<WaveformMetrics>.Fail(BenchError.Validation(
                "BF-WAVE-EMPTY",
                "Cannot analyze an empty waveform.",
                resourceId,
                "Acquire at least one sample before calling WaveformAnalyzer.Analyze."));
        }

        // Validate finiteness first so NaN/Infinity never silently corrupts SIMD reductions.
        for (int i = 0; i < samples.Length; i++)
        {
            if (!double.IsFinite(samples[i]))
            {
                return BenchResult<WaveformMetrics>.Fail(BenchError.Validation(
                    "BF-WAVE-NONFINITE",
                    $"Waveform contains a non-finite sample at index {i}.",
                    resourceId,
                    "Verify oscilloscope scaling preamble and ADC overflow flags."));
            }
        }

        int n = samples.Length;
        int width = Vector<double>.Count;
        bool useSimd = Vector.IsHardwareAccelerated && n >= width * 2;

        double min = samples[0];
        double max = samples[0];
        double sum = 0.0;
        double sumSq = 0.0;
        int idx = 0;

        if (useSimd)
        {
            var vMin = new Vector<double>(samples);
            var vMax = vMin;
            var vSum = Vector<double>.Zero;
            var vSumSq = Vector<double>.Zero;

            for (; idx <= n - width; idx += width)
            {
                var v = new Vector<double>(samples.Slice(idx, width));
                vMin = Vector.Min(vMin, v);
                vMax = Vector.Max(vMax, v);
                vSum += v;
                vSumSq += v * v;
            }

            min = vMin[0];
            max = vMax[0];
            for (int lane = 0; lane < width; lane++)
            {
                if (vMin[lane] < min) min = vMin[lane];
                if (vMax[lane] > max) max = vMax[lane];
                sum += vSum[lane];
                sumSq += vSumSq[lane];
            }
        }

        for (; idx < n; idx++)
        {
            double s = samples[idx];
            if (s < min) min = s;
            if (s > max) max = s;
            sum += s;
            sumSq += s * s;
        }

        double mean = sum / n;
        double rms = Math.Sqrt(Math.Max(0.0, sumSq / n));

        // Second pass for numerically stable AC ripple RMS around the DC mean.
        double acSq = 0.0;
        idx = 0;
        if (useSimd)
        {
            var vMean = new Vector<double>(mean);
            var vAcSq = Vector<double>.Zero;
            for (; idx <= n - width; idx += width)
            {
                var diff = new Vector<double>(samples.Slice(idx, width)) - vMean;
                vAcSq += diff * diff;
            }
            for (int lane = 0; lane < width; lane++)
                acSq += vAcSq[lane];
        }
        for (; idx < n; idx++)
        {
            double d = samples[idx] - mean;
            acSq += d * d;
        }

        double acRms = Math.Sqrt(Math.Max(0.0, acSq / n));
        double peakAbs = Math.Max(Math.Abs(min), Math.Abs(max));
        double crest = rms > 0.0 ? peakAbs / rms : 0.0;

        return BenchResult<WaveformMetrics>.Ok(new WaveformMetrics(
            n,
            min,
            max,
            max - min,
            mean,
            rms,
            acRms,
            crest,
            useSimd));
    }

    /// <summary>Analyze a waveform and verify DC mean and AC ripple against flight-hardware power-rail limits.</summary>
    /// <param name="samples">Decoded voltage samples in volts.</param>
    /// <param name="minMeanVolts">Minimum allowed DC rail voltage in volts.</param>
    /// <param name="maxMeanVolts">Maximum allowed DC rail voltage in volts.</param>
    /// <param name="maxPeakToPeakVolts">Maximum allowed peak-to-peak ripple in volts.</param>
    /// <param name="maxAcRmsVolts">Optional maximum allowed AC RMS ripple in volts.</param>
    /// <param name="resourceId">Optional scope role for structured diagnostics.</param>
    /// <returns>Railroad result passing with <see cref="WaveformMetrics"/> or failing with <see cref="BenchErrorKind.LimitExceeded"/>.</returns>
    public static BenchResult<WaveformMetrics> VerifyPowerRailRipple(
        ReadOnlySpan<double> samples,
        double minMeanVolts,
        double maxMeanVolts,
        double maxPeakToPeakVolts,
        double? maxAcRmsVolts = null,
        string? resourceId = null)
    {
        return Analyze(samples, resourceId)
            .Ensure(
                m => m.Mean >= minMeanVolts && m.Mean <= maxMeanVolts,
                m => new BenchError(
                    "BF-WAVE-DC-OUT-OF-LIMIT",
                    BenchErrorKind.LimitExceeded,
                    $"DC rail mean {m.Mean:F4} V is outside [{minMeanVolts:F4}, {maxMeanVolts:F4}] V.",
                    resourceId,
                    Remedy: "Inspect regulator trim, load current, and sense wiring."))
            .Ensure(
                m => m.PeakToPeak <= maxPeakToPeakVolts,
                m => new BenchError(
                    "BF-WAVE-RIPPLE-VPP",
                    BenchErrorKind.LimitExceeded,
                    $"Peak-to-peak ripple {m.PeakToPeak * 1000.0:F2} mVpp exceeds limit {maxPeakToPeakVolts * 1000.0:F2} mVpp.",
                    resourceId,
                    Remedy: "Inspect flight power-rail decoupling capacitors and switching noise."))
            .Ensure(
                m => maxAcRmsVolts is null || m.AcRms <= maxAcRmsVolts.Value,
                m => new BenchError(
                    "BF-WAVE-RIPPLE-RMS",
                    BenchErrorKind.LimitExceeded,
                    $"AC RMS ripple {m.AcRms * 1000.0:F2} mVrms exceeds limit {maxAcRmsVolts!.Value * 1000.0:F2} mVrms.",
                    resourceId,
                    Remedy: "Inspect load transients and ground-loop coupling."));
    }
}
