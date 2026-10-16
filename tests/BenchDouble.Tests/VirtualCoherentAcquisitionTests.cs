using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>End-to-end virtual source, meter and oscilloscope transaction tests.</summary>
public sealed class VirtualCoherentAcquisitionTests
{
    /// <summary>Compliance, waveform, meter readings and virtual timestamp agree.</summary>
    /// <returns>Completion of the coherent capture verification.</returns>
    [Fact]
    public void CaptureHasOneIdentityRevisionAndTime()
    {
        var clock = new ManualSimulationClock();
        var smu = new VirtualSourceMeasureUnit(new VirtualSignalBus(), clock);
        smu.SetLoad(ElectricResistance.FromOhms(100));
        smu.SourceVoltage(ElectricPotential.FromVolts(10), ElectricCurrent.FromMilliamperes(20));
        smu.SetOutput(true);
        clock.AdvanceBy(TimeSpan.FromSeconds(7));
        var id = Guid.NewGuid();
        var acquired = new VirtualCoherentAcquisitionService(smu).Acquire(
            1, 16, Duration.FromMilliseconds(1), 0.001, id);
        Assert.Equal(id, acquired.Waveform.AcquisitionId);
        Assert.Equal(id, acquired.AcquisitionId);
        Assert.Equal(smu.Snapshot().Revision, acquired.SourceRevision);
        Assert.Equal(7, acquired.Waveform.TimeAt(0).Seconds, 8);
        Assert.True(acquired.Measurement.ComplianceActive);
        Assert.Equal(2, acquired.Voltmeter.Volts, 8);
        Assert.Equal(0.02, acquired.Ammeter.Amperes, 8);
        smu.SetOutput(false);
        Assert.Equal(2, acquired.Waveform.VoltageAt(15).Volts, 8);
        Assert.Equal(0, smu.Measure().Voltage.Volts, 8);
    }

    /// <summary>Invalid requests do not consume pending one-shot faults.</summary>
    [Fact]
    public void InvalidCapturePreservesFault()
    {
        var smu = new VirtualSourceMeasureUnit(new VirtualSignalBus(), new ManualSimulationClock());
        smu.QueueMeasurementFault(new TimeoutException("synthetic"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            smu.AcquireCoherently(0, 8, Duration.FromMilliseconds(1)));
        Assert.Throws<TimeoutException>(() =>
            smu.AcquireCoherently(1, 8, Duration.FromMilliseconds(1)));
        Assert.NotEqual(Guid.Empty, smu.AcquireCoherently(1, 8, Duration.FromMilliseconds(1)).AcquisitionId);
    }

    /// <summary>Concurrent source writes cannot tear an acquired waveform.</summary>
    /// <returns>Completion of concurrent acquisition verification.</returns>
    [Fact]
    public async Task ConcurrentOutputTransitionsRemainCoherent()
    {
        var smu = new VirtualSourceMeasureUnit(new VirtualSignalBus(), new ManualSimulationClock());
        smu.SourceVoltage(ElectricPotential.FromVolts(3), ElectricCurrent.FromAmperes(1));
        smu.SetOutput(true);
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++) smu.SetOutput(i % 2 == 0);
        });
        var reader = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                var acquired = smu.AcquireCoherently(1, 8, Duration.FromMilliseconds(1));
                double volts = acquired.Voltmeter.Volts;
                Assert.True(volts == 0 || volts == 3);
                Assert.Equal(volts / 1000, acquired.Ammeter.Amperes, 8);
                for (int j = 0; j < acquired.Waveform.SampleCount; j++)
                    Assert.Equal(volts, acquired.Waveform.VoltageAt(j).Volts, 8);
            }
        });
        await Task.WhenAll(writer, reader);
    }
}
