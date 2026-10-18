using BenchDouble.Generated;
using BenchDouble.Simulation;
using Xunit;
namespace BenchDouble.Tests;
/// <summary>Mock diagnostics and bulk-state regression tests for batch 200.</summary>
public sealed class StatefulBatch200Tests
{
    private const string Definition = """
    {"schemaVersion":2,"identity":{"driverName":"Batch200"},"modes":[{"id":"voltage","selectionCommand":"CONF:VOLT"},{"id":"resistance","selectionCommand":"CONF:RES"}],"capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"V?","modes":["voltage"],"mockDefault":1},{"name":"resistance","quantity":"ElectricResistance","unit":"Ohm","access":"read","query":"R?","modes":["resistance"],"mockDefault":2}]}
    """;
    private static StatefulMockEngine Engine(ISimulationClock? clock=null)=>new(ImportedInstrumentDefinition.Parse(Definition),clock);
    private sealed class CallbackClock(Func<TimeSpan> read):ISimulationClock
    {
        /// <inheritdoc/>
        public TimeSpan Elapsed=>read();
    }

    /// <summary>New engines report zero pending faults.</summary>
    [Fact] public void InitialFaultCount()
    {
        var e=Engine(); Assert.Equal(0,e.FaultCount("voltage")); Assert.Empty(e.Snapshot().PendingFaultCounts);
    }
}
