using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Hardware-free shared-signal physics and state-transition regression tests.</summary>
public sealed class VirtualSignalBusTests
{
    /// <summary>Generator configuration controls the same waveform observed by a simulated meter.</summary>
    [Fact]
    public void SourceAndMeterObserveOneConsistentSignal()
    {
        var bus = new VirtualSignalBus();
        bus.Configure(Frequency.FromHertz(1_000), ElectricPotential.FromVolts(2),
            ElectricPotential.Zero, ElectricResistance.FromOhms(1_000),
            ElectricCurrent.FromMilliamperes(10));
        Assert.Equal(0, bus.Snapshot().VoltageAt(Duration.FromMicroseconds(250)).Volts);
        bus.SetOutput(true);
        var state = bus.Snapshot();
        Assert.Equal(2, state.VoltageAt(Duration.FromMicroseconds(250)).Volts, 9);
        Assert.Equal(0.002, state.CurrentAt(Duration.FromMicroseconds(250)).Amperes, 9);
        var reading = state.MeasureVoltage();
        Assert.InRange(Math.Abs(reading.Dc.Volts), 0, 1e-9);
        Assert.Equal(Math.Sqrt(2), reading.Rms.Volts, 9);
    }

    /// <summary>Compliance clips voltage and output-off state returns zero.</summary>
    [Fact]
    public void ComplianceAndDisableAreDeterministic()
    {
        var bus = new VirtualSignalBus();
        bus.Configure(Frequency.FromHertz(1_000), ElectricPotential.FromVolts(5),
            ElectricPotential.Zero, ElectricResistance.FromOhms(100),
            ElectricCurrent.FromMilliamperes(10));
        bus.SetOutput(true);
        Assert.Equal(1, bus.Snapshot().VoltageAt(Duration.FromMicroseconds(250)).Volts, 9);
        bus.SetOutput(false);
        Assert.Equal(0, bus.Snapshot().MeasureVoltage().Rms.Volts);
    }

    /// <summary>Invalid physical parameters never change the prior state.</summary>
    [Fact]
    public void InvalidConfigurationDoesNotMutateState()
    {
        var bus = new VirtualSignalBus();
        var before = bus.Snapshot();
        Assert.Throws<ArgumentOutOfRangeException>(() => bus.Configure(
            Frequency.FromHertz(0), ElectricPotential.FromVolts(1), ElectricPotential.Zero,
            ElectricResistance.FromOhms(100), ElectricCurrent.FromAmperes(1)));
        Assert.Equal(before, bus.Snapshot());
    }
}
