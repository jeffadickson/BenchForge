using BenchDouble.Simulation;
using BenchDouble.Generated;
using Xunit;
namespace BenchDouble.Tests;
/// <summary>Mode-aware opt-in scalar latency, cancellation and state preservation tests.</summary>
public sealed class TimedMockBTests
{
    private const string Definition="""
    {"schemaVersion":2,"identity":{"driverName":"TimedB"},"modes":[{"id":"v","selectionCommand":"CONF:V"},{"id":"r","selectionCommand":"CONF:R"}],"capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"V?","modes":["v"],"mockDefault":1},{"name":"current","quantity":"ElectricCurrent","unit":"Ampere","access":"read","query":"I?","modes":["v"],"mockDefault":2},{"name":"resistance","quantity":"ElectricResistance","unit":"Ohm","access":"read","query":"R?","modes":["r"],"mockDefault":3}]}
    """;
    private sealed class Clock:ISimulationClock
    {
        internal TimeSpan Now;internal Action? OnRead;internal bool Fail;
        /// <inheritdoc/>
        public TimeSpan Elapsed{get{OnRead?.Invoke();if(Fail)throw new IOException();return Now;}}
    }
    private static StatefulMockEngine Engine(ISimulationClock? clock=null)=>new(ImportedInstrumentDefinition.Parse(Definition),clock);
    private static SimulationLatencyProfile Profile(long ticks=10)=>new(7,[new(SimulationLatencyPhase.Acquisition,TimeSpan.FromTicks(ticks),TimeSpan.Zero)]);

    /// <summary>Unprepared scalar reads preserve existing behavior.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task UnpreparedCompatibility()
    {
        var e=Engine();await e.ConfigureModeAsync("v");Assert.Equal(1,await e.MeasureAsync("voltage"));
    }

    /// <summary>Preparation requires an explicitly compatible operating mode.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task NoMode()
    {
        var e=Engine();await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(),0));Assert.Null(e.PreparedMeasurementStatus("voltage"));
    }

    /// <summary>Incompatible preparation leaves mock revision unchanged.</summary>
    /// <returns>Completion of the simulated state transaction checks.</returns>
    [Fact] public async Task WrongMode()
    {
        var e=Engine();await e.ConfigureModeAsync("r");var r=e.Snapshot().Revision;await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.PrepareMeasurementAsync("voltage",Profile(),0));Assert.Equal(r,e.Snapshot().Revision);
    }
}
