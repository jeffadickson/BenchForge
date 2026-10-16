using BenchDouble.Oscilloscopes;
using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>End-to-end tests for a shared simulated source and SCPI oscilloscope.</summary>
public sealed class SharedSignalScopeTests
{
    /// <returns>Completion of the asynchronous verification.</returns>
    /// <summary>Changing the source changes the subsequent acquisition, not an in-flight snapshot.</summary>
    [Fact]
    public async Task SingleAcquisitionUsesSourceSnapshot()
    {
        var bus = new VirtualSignalBus();
        bus.Configure(Frequency.FromHertz(1_000), ElectricPotential.FromVolts(2),
            ElectricPotential.FromVolts(0), ElectricResistance.FromOhms(1_000),
            ElectricCurrent.FromMilliamperes(10));
        bus.SetOutput(true);
        var session = new SimulatedOscilloscopeSession(samples: 1_000, signal: bus);
        var scope = new AsyncOscilloscope(session);
        await scope.SingleAsync();
        bus.SetOutput(false);
        var first = await scope.ReadWaveformAsync(1, Guid.NewGuid());
        Assert.Equal(2, first.VoltageAt(250).Volts, 9);
        await scope.SingleAsync();
        var second = await scope.ReadWaveformAsync(1, Guid.NewGuid());
        Assert.Equal(0, second.VoltageAt(250).Volts);
    }
}
