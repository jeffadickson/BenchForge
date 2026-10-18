using BenchDouble.Generated;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>Run-scoped integration of sequence state, canonical clocks and injected faults.</summary>
public sealed class SequenceCompositionTests
{
    private const string Metadata="""
        {"schemaVersion":2,"identity":{"driverName":"Meter"},"modes":[{"id":"v","selectionCommand":"CONF:V"}],
         "capabilities":[{"name":"v","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"V?","modes":["v"],"mockDefault":0}]}
        """;
    private static ExperimentDefinition Definition()=>new ExperimentDefinitionBuilder().Simulated<StatefulMockEngine>("meter",sp=>
    {
        var engine=new StatefulMockEngine(ImportedInstrumentDefinition.Parse(Metadata),sp.GetRequiredService<ISimulationClock>());
        engine.SetSequence("v",[1,2]); return engine;
    }).Build("sequence run");

    /// <summary>Two runs use independent sequence cursors and their own canonical clocks.</summary>
    /// <returns>Completion after independent state and timestamp checks.</returns>
    [Fact] public async Task SequenceCursorsAndClocksAreRunScoped()
    {
        var definition=Definition(); await using var first=await definition.StartSimulatedAsync(); await using var second=await definition.StartSimulatedAsync();
        var a=first.Instrument<StatefulMockEngine>("meter"); var b=second.Instrument<StatefulMockEngine>("meter");
        await a.ConfigureModeAsync("v"); await b.ConfigureModeAsync("v"); Assert.Equal(1,await a.MeasureAsync("v")); Assert.Equal(2,await a.MeasureAsync("v"));
        Assert.Equal(0,b.SequenceStatus("v")!.Position); Assert.Equal(1,await b.MeasureAsync("v"));
        first.Clock.AdvanceBy(TimeSpan.FromSeconds(2)); Assert.Equal(TimeSpan.FromSeconds(2),a.Snapshot().Elapsed); Assert.Equal(TimeSpan.Zero,b.Snapshot().Elapsed);
    }

    /// <summary>A fault in one run never advances its cursor or affects another run's first reading.</summary>
    /// <returns>Completion after isolated fault and sequence measurements.</returns>
    [Fact] public async Task SequenceFaultsRemainRunScoped()
    {
        var definition=Definition(); await using var first=await definition.StartSimulatedAsync(); await using var second=await definition.StartSimulatedAsync();
        var a=first.Instrument<StatefulMockEngine>("meter"); var b=second.Instrument<StatefulMockEngine>("meter"); await a.ConfigureModeAsync("v"); await b.ConfigureModeAsync("v");
        var fault=new IOException("run one"); a.QueueFault("v",fault); Assert.Same(fault,await Assert.ThrowsAsync<IOException>(async()=>await a.MeasureAsync("v")));
        Assert.Equal(1,await b.MeasureAsync("v")); Assert.Equal(1,await a.MeasureAsync("v"));
    }
}
