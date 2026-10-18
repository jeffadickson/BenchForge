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

    /// <summary>Injected exception payloads stay with callers and do not enter diagnostic sinks.</summary>
    /// <returns>Completion after observing a redacted fault event.</returns>
    [Fact] public async Task MockFaultMessagesArePrivate()
    {
        var sink=new Sink();var e=new StatefulMockEngine(ImportedInstrumentDefinition.Parse(Definition),logger:sink);
        await e.ConfigureModeAsync("voltage");e.QueueFault("voltage",new IOException("SECRET"));
        await Assert.ThrowsAsync<IOException>(async()=>await e.MeasureAsync("voltage"));
        Assert.All(sink.Errors,Assert.Null);Assert.All(sink.Messages,m=>Assert.DoesNotContain("SECRET",m));
    }

    /// <summary>Cancellation during a cooperative model cannot return a successful scalar afterward.</summary>
    /// <returns>Completion after the cancelled model exits.</returns>
    [Fact] public async Task CancelledModelCannotReturnSuccess()
    {
        var e=Engine();await e.ConfigureModeAsync("voltage");using var ct=new CancellationTokenSource();
        e.SetAsyncBehavior("voltage",(_,_)=>{ct.Cancel();return ValueTask.FromResult(1d);});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await e.MeasureAsync("voltage",ct.Token));
    }

    /// <summary>Consuming a queued fault is a visible state transition.</summary>
    /// <returns>Completion after fault delivery changes the snapshot revision.</returns>
    [Fact] public async Task FaultDeliveryAdvancesRevision()
    {
        var e=Engine();await e.ConfigureModeAsync("voltage");e.QueueFault("voltage",new IOException());
        var before=e.Snapshot().Revision;
        await Assert.ThrowsAsync<IOException>(async()=>await e.MeasureAsync("voltage"));
        Assert.Equal(before+1,e.Snapshot().Revision);
    }

    /// <summary>Fault clearing retains overrides and mode and is idempotent.</summary>
    /// <returns>Completion after explicit fault clearing.</returns>
    [Fact] public async Task FaultClearingPreservesOtherState()
    {
        var e=Engine();await e.ConfigureModeAsync("voltage");e.SetReading("voltage",3);
        e.QueueFault("voltage",new IOException());e.QueueFault("voltage",new IOException());
        Assert.Equal(2,e.ClearFaults("voltage"));var revision=e.Snapshot().Revision;
        Assert.Equal(0,e.ClearFaults("voltage"));Assert.Equal(revision,e.Snapshot().Revision);
        Assert.Equal(3,await e.MeasureAsync("voltage"));
    }

// CASES
}
