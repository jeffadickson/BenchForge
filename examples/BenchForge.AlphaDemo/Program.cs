using System;
using System.IO;
using System.Text.Json;
using BenchDouble.Oscilloscopes;
using UnitsNet;

namespace BenchForge.AlphaDemo;

/// <summary>Hardware-free shared-signal integration smoke test for the alpha milestone.</summary>
internal static class Program
{
    private static async Task<int> Main()
    {
        const int samples = 128;
        const double amplitudeVolts = 1.0;
        const double frequencyHz = 15625.0;
        const double sampleIntervalSeconds = 1e-6;
        var session = new SimulatedOscilloscopeSession(channels: 2, samples: samples);
        var coordinator = new OscilloscopeSessionCoordinator(session);
        var scope = new CoordinatedOscilloscope(coordinator);
        var capture = await scope.AcquireAsync(1, Guid.NewGuid());
        // Current simulator has a 64-sample period at a 1-us interval.
        var values = Enumerable.Range(0, capture.SampleCount).Select(i => new
        {
            timeSeconds = capture.TimeAt(i).Seconds,
            voltageVolts = capture.VoltageAt(i).Volts
        }).ToArray();
        double rms = Math.Sqrt(values.Average(p => p.voltageVolts * p.voltageVolts));
        double expectedRms = amplitudeVolts / Math.Sqrt(2);
        if (Math.Abs(rms - expectedRms) > 0.002)
            throw new InvalidOperationException($"RMS mismatch: {rms} vs {expectedRms}");
        var report = new
        {
            source = "SimulatedOscilloscopeSession",
            generator = new { frequencyHz, amplitudeVolts },
            scope = new { capture.AcquisitionId, capture.Channel, capture.SampleCount, sampleIntervalSeconds },
            dmm = new { rmsVolts = rms, quantity = "ElectricPotential", basis = "RMS" },
            waveform = values
        };
        Directory.CreateDirectory("alpha-demo-output");
        var path = Path.Combine("alpha-demo-output", "capture.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Hardware-free waveform/RMS integration PASS: {rms:F6} Vrms; wrote {path}");
        return 0;
    }
}
