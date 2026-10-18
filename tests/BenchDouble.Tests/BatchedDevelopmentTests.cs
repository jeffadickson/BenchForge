using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using BenchDouble.Simulation;
using BenchDouble.Generated;
using UnitsNet;

namespace BenchDouble.Tests;

/// <summary>Regression cases accumulated between ten-iteration test checkpoints.</summary>
public sealed class BatchedDevelopmentTests
{
    private const string Definition = """
    {"schemaVersion":2,"identity":{"driverName":"BatchMeter"},
     "modes":[{"id":"voltage","selectionCommand":"CONF:VOLT"}],
     "capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"MEAS:VOLT?","modes":["voltage"],"mockDefault":0.25}]}
    """;
    private static StatefulMockEngine Engine() => new(ImportedInstrumentDefinition.Parse(Definition));

    private sealed class Sink : ILogger<StatefulMockEngine>
    {
        public bool Throw;
        public readonly List<Exception?> Errors=[];
        public readonly List<string> Messages=[];
        public IDisposable? BeginScope<T>(T state) where T:notnull => null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<T>(LogLevel level, EventId id, T state, Exception? error, Func<T,Exception?,string> formatter)
        {
            if(Throw) throw new Exception("sink");
            Errors.Add(error); Messages.Add(formatter(state,error));
        }
    }
    /// <summary>Sink failures do not replace mode, fault or reset outcomes.</summary>
    /// <returns>Completion of mocked logging isolation checks.</returns>
    [Fact] public async Task SinkFailuresPreserveMockState()
    {
        var e=new StatefulMockEngine(ImportedInstrumentDefinition.Parse(Definition),logger:new Sink{Throw=true});
        await e.ConfigureModeAsync("voltage");
        var failure=new TimeoutException("original");e.QueueFault("voltage",failure);
        Assert.Same(failure,await Assert.ThrowsAsync<TimeoutException>(async()=>await e.MeasureAsync("voltage")));
        e.Reset();Assert.Null(e.Snapshot().Mode);Assert.Equal(3,e.LoggingFailureCount);
    }

// CASES
}
