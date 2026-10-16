using BenchDouble.Generated;
using BenchDouble.Experiments;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>Checks run-scoped IoC composition of stateful mock instruments.</summary>
public sealed class StatefulMockCompositionTests
{
    private const string Definition = """
    {"schemaVersion":2,"identity":{"driverName":"MockMeter"},
     "modes":[{"id":"voltage","selectionCommand":"CONF:VOLT"}],
     "capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt",
       "access":"read","query":"MEAS:VOLT?","modes":["voltage"],"mockDefault":0.25}]}
    """;

    /// <summary>Independent runs get separate clocks and stateful mock instances.</summary>
    /// <returns>Completion of isolation checks.</returns>
    [Fact]
    public async Task TwoRunsDoNotShareMockState()
    {
        var definition = ImportedInstrumentDefinition.Parse(Definition);
        var experiment = new ExperimentDefinitionBuilder()
            .StatefulMock<ImportedInstrumentMock>("Meter",
                (clock, logger) => new ImportedInstrumentMock(definition, clock, logger))
            .Build("Isolated meters");
        await using var first = experiment.StartSimulated();
        await using var second = experiment.StartSimulated();
        var a = first.Instrument<ImportedInstrumentMock>("Meter");
        var b = second.Instrument<ImportedInstrumentMock>("Meter");
        Assert.NotSame(a, b);
        await a.ConfigureModeAsync("voltage");
        a.SetReading("voltage", 4.5);
        await b.ConfigureModeAsync("voltage");
        Assert.Equal(4.5, await a.MeasureAsync("voltage"));
        Assert.Equal(0.25, await b.MeasureAsync("voltage"));
        first.Clock.AdvanceBy(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2), a.Snapshot().Elapsed);
        Assert.Equal(TimeSpan.Zero, b.Snapshot().Elapsed);
    }

    /// <summary>Multiple instrument roles can share one physical model in the same run.</summary>
    /// <returns>Completion of model-identity checks.</returns>
    [Fact]
    public async Task TwoRolesShareOneInjectedModel()
    {
        var definition = ImportedInstrumentDefinition.Parse(Definition);
        var experiment = new ExperimentDefinitionBuilder()
            .Model<VirtualSignalBus>(_ => new VirtualSignalBus())
            .StatefulMock<ImportedInstrumentMock, VirtualSignalBus>("MeterA",
                (clock, logger, bus) => new ImportedInstrumentMock(definition, clock, logger))
            .StatefulMock<ImportedInstrumentMock, VirtualSignalBus>("MeterB",
                (clock, logger, bus) => new ImportedInstrumentMock(definition, clock, logger))
            .Build("Shared model");
        await using var run = experiment.StartSimulated();
        Assert.NotSame(run.Instrument<ImportedInstrumentMock>("MeterA"),
            run.Instrument<ImportedInstrumentMock>("MeterB"));
    }
}
