using BenchDouble.Experiments;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;
using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>Verify virtual SMU and oscilloscope acquisition through run-scoped IoC.</summary>
public sealed class VirtualSmuCompositionTests
{
    private static ExperimentDefinition CreateDefinition() => new ExperimentDefinitionBuilder()
        .Model<VirtualSignalBus>(_ => new VirtualSignalBus())
        .Model<VirtualSourceMeasureUnit>(sp => new VirtualSourceMeasureUnit(
            sp.GetRequiredService<VirtualSignalBus>(), sp.GetRequiredService<ISimulationClock>()))
        .Simulated<VirtualSourceMeasureUnit>("SMU", sp => sp.GetRequiredService<VirtualSourceMeasureUnit>())
        .Simulated<VirtualCoherentAcquisitionService>("Scope", sp =>
            new VirtualCoherentAcquisitionService(sp.GetRequiredService<VirtualSourceMeasureUnit>()))
        .Build("Virtual coherent bench");

    /// <summary>Separate experiments have independent source state and clocks.</summary>
    /// <returns>Completion of run-isolation verification.</returns>
    [Fact]
    public async Task SourceAndAcquisitionAreRunScoped()
    {
        var definition = CreateDefinition();
        await using var first = definition.StartSimulated();
        await using var second = definition.StartSimulated();
        var smu = first.Instrument<VirtualSourceMeasureUnit>("SMU");
        smu.SourceVoltage(ElectricPotential.FromVolts(5), ElectricCurrent.FromAmperes(1));
        smu.SetOutput(true);
        first.Clock.AdvanceBy(TimeSpan.FromSeconds(4));
        var a = first.Instrument<VirtualCoherentAcquisitionService>("Scope")
            .Acquire(1, 4, Duration.FromMilliseconds(1));
        var b = second.Instrument<VirtualCoherentAcquisitionService>("Scope")
            .Acquire(1, 4, Duration.FromMilliseconds(1));
        Assert.Equal(5, a.Voltmeter.Volts, 8);
        Assert.Equal(0, b.Voltmeter.Volts, 8);
        Assert.Equal(4, a.Waveform.TimeAt(0).Seconds, 8);
        Assert.Equal(0, b.Waveform.TimeAt(0).Seconds, 8);
        Assert.NotEqual(a.AcquisitionId, b.AcquisitionId);
    }
}
