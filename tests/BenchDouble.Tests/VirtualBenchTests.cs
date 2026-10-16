using BenchDouble.Simulation;
using BenchDouble.Oscilloscopes;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Cross-instrument consistency tests for a shared virtual circuit.</summary>
public sealed class VirtualBenchTests
{
    /// <summary>A scope waveform and meter observe the same configured sine source.</summary>
    [Fact]
    public async Task ScopeAndMeterAgreeOnRms()
    {
        var bus = new VirtualSignalBus();
        bus.Configure(Frequency.FromHertz(1_000), ElectricPotential.FromVolts(2),
            ElectricPotential.Zero, ElectricResistance.FromOhms(1_000),
            ElectricCurrent.FromMilliamperes(10));
        bus.SetOutput(true);
        var bench = new VirtualBench(bus);
        var waveform = bench.Capture(1, 1_000, Duration.FromMicroseconds(1));
        var rms = (await WaveformProcessingPipeline.CalculateRmsAsync(new[] { waveform }))[0];
        var meter = bench.MeasureVoltage(samples: 1_000);
        Assert.Equal(1_000, waveform.SampleCount);
        Assert.InRange(Math.Abs(rms.RmsVolts - meter.Rms.Volts), 0, 0.001);
        Assert.InRange(Math.Abs(meter.Dc.Volts), 0, 1e-9);
    }

    /// <summary>Disabling the source changes both instrument readings to zero.</summary>
    [Fact]
    public void OutputOffProducesZeroCaptureAndMeterReading()
    {
        var bus = new VirtualSignalBus();
        bus.SetOutput(false);
        var bench = new VirtualBench(bus);
        Assert.Equal(0, bench.MeasureVoltage().Rms.Volts);
        var waveform = bench.Capture(1, 8, Duration.FromMicroseconds(1));
        Assert.All(Enumerable.Range(0, waveform.SampleCount),
            i => Assert.Equal(0, waveform.VoltageAt(i).Volts));
    }
}
