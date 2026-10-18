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

    /// <summary>Removing nonexistent models is an idempotent configuration operation.</summary>
    [Fact] public void RemovingAbsentModelsDoesNotRevise()
    {
        var e=Engine();var revision=e.Snapshot().Revision;
        e.SetBehavior("voltage",null);e.SetAsyncBehavior("voltage",null);
        Assert.Equal(revision,e.Snapshot().Revision);
    }

    /// <summary>Repeating an existing finite override preserves the revision.</summary>
    [Fact] public void IdenticalOverridesDoNotRevise()
    {
        var e=Engine();e.SetReading("voltage",2);var revision=e.Snapshot().Revision;
        e.SetReading("voltage",2);Assert.Equal(revision,e.Snapshot().Revision);
        e.SetReading("voltage",3);Assert.Equal(revision+1,e.Snapshot().Revision);
    }

    /// <summary>Excess fault injection fails without changing the accepted queue or revision.</summary>
    [Fact] public void FaultQueueIsBounded()
    {
        var e=Engine();for(var i=0;i<256;i++) e.QueueFault("voltage",new IOException());
        var revision=e.Snapshot().Revision;
        Assert.Throws<InvalidOperationException>(()=>e.QueueFault("voltage",new IOException()));
        Assert.Equal(revision,e.Snapshot().Revision);Assert.Equal(256,e.ClearFaults("voltage"));
    }

    /// <summary>Opposite finite extremes interpolate without overflow in their difference.</summary>
    [Fact] public void ExtremeRampsRemainFinite()
    {
        var ramp=StatefulMockBehaviors.Ramp(TimeSpan.Zero,TimeSpan.FromSeconds(2),double.MaxValue,-double.MaxValue);
        Assert.Equal(0,ramp(new("voltage","voltage",TimeSpan.FromSeconds(1),0,null)));
    }

    /// <summary>Capability mode metadata cannot alter a parsed definition after validation.</summary>
    [Fact] public void ImportedModesCannotBeMutated()
    {
        var definition=ImportedInstrumentDefinition.Parse(Definition);
        var modes=Assert.IsAssignableFrom<IList<string>>(definition.Capability("voltage").Modes);
        Assert.Throws<NotSupportedException>(()=>modes[0]="other");
        Assert.Equal("voltage",Assert.Single(definition.Capability("voltage").Modes));
    }

    /// <summary>Driver, mode and capability identifiers must match the entire JSON string.</summary>
    [Fact] public void DefinitionIdentifiersRejectTrailingNewlines()
    {
        foreach(var replacement in new[]{Definition.Replace("BatchMeter","BatchMeter\\n"),Definition.Replace("\"id\":\"voltage\"","\"id\":\"voltage\\n\""),Definition.Replace("\"name\":\"voltage\"","\"name\":\"voltage\\n\"")})
            Assert.Throws<InvalidDataException>(()=>ImportedInstrumentDefinition.Parse(replacement));
    }

    /// <summary>Oversized definitions fail before parsing or materializing metadata.</summary>
    [Fact] public void ImportedDefinitionIsBounded()
    {
        Assert.Throws<InvalidDataException>(()=>ImportedInstrumentDefinition.Parse(new string(' ',1024*1024+1)));
    }

    /// <summary>Repeated fields cannot silently replace validated identity or default values.</summary>
    [Fact] public void DefinitionDuplicateKeysAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>ImportedInstrumentDefinition.Parse(Definition.Replace("\"schemaVersion\":2","\"schemaVersion\":1,\"schemaVersion\":2")));
        Assert.Throws<InvalidDataException>(()=>ImportedInstrumentDefinition.Parse(Definition.Replace("\"mockDefault\":0.25","\"mockDefault\":9,\"mockDefault\":0.25")));
    }

    /// <summary>Distinct mode IDs cannot use the same case-insensitive SCPI selection command.</summary>
    [Fact] public void ModesCannotShareSelectionCommands()
    {
        var json=Definition.Replace("\"selectionCommand\":\"CONF:VOLT\"}","\"selectionCommand\":\"CONF:VOLT\"},{\"id\":\"other\",\"selectionCommand\":\"conf:volt\"}");
        Assert.Throws<InvalidDataException>(()=>ImportedInstrumentDefinition.Parse(json));
    }

    /// <summary>Two capabilities in the same mode cannot claim the same SCPI query.</summary>
    [Fact] public void QueriesAreUnambiguousWithinModes()
    {
        using var doc=JsonDocument.Parse(Definition);
        var capability=doc.RootElement.GetProperty("capabilities")[0].GetRawText();
        var json=Definition.Replace(capability,capability+","+capability.Replace("\"name\":\"voltage\"","\"name\":\"other\""));
        Assert.Throws<InvalidDataException>(()=>ImportedInstrumentDefinition.Parse(json));
    }

    /// <summary>Repeated mode references are rejected rather than retained as ambiguous metadata.</summary>
    [Fact] public void RepeatedCapabilityModesAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>ImportedInstrumentDefinition.Parse(Definition.Replace("\"modes\":[\"voltage\"]","\"modes\":[\"voltage\",\"voltage\"]")));
    }

    /// <summary>A string or boolean default cannot silently become an absent reading.</summary>
    [Fact] public void DefaultsCannotSilentlyLoseTheirType()
    {
        foreach(var value in new[]{"\"bad\"","true"})
            Assert.Throws<InvalidDataException>(()=>ImportedInstrumentDefinition.Parse(Definition.Replace("\"mockDefault\":0.25","\"mockDefault\":"+value)));
        Assert.Null(ImportedInstrumentDefinition.Parse(Definition.Replace("\"mockDefault\":0.25","\"mockDefault\":null")).Capability("voltage").MockDefault);
    }

    /// <summary>Case-insensitive rule deserialization cannot hide duplicate fields.</summary>
    [Fact] public void RuleJsonRejectsDuplicateFields()
    {
        Assert.Throws<ArgumentException>(()=>AutoMockRuleFiles.Parse("{\"version\":1,\"Version\":1,\"rules\":[]}"));
        Assert.Throws<ArgumentException>(()=>AutoMockRuleFiles.Parse("{\"version\":1,\"rules\":[{\"name\":\"r\",\"action\":\"FixedCurrent\",\"value\":1,\"Value\":0}]}"));
    }

    /// <summary>An omitted action value cannot silently configure zero current.</summary>
    [Fact] public void RuleValuesMustBeExplicit()
    {
        Assert.Throws<JsonException>(()=>AutoMockRuleFiles.Parse("{\"version\":1,\"rules\":[{\"name\":\"r\",\"action\":\"FixedCurrent\"}]}"));
    }

    /// <summary>Rule documents use reviewed symbolic actions rather than numeric enum aliases.</summary>
    [Fact] public void NumericRuleActionNamesAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>AutoMockRuleFiles.Parse("{\"version\":1,\"rules\":[{\"name\":\"r\",\"action\":\"0\",\"value\":1}]}"));
        Assert.Single(AutoMockRuleFiles.Parse("{\"version\":1,\"rules\":[{\"name\":\"r\",\"action\":\"fixedcurrent\",\"value\":1}]}"));
    }

    /// <summary>Fluent and JSON rule sets have the same finite count limit.</summary>
    [Fact] public void FluentRuleSetsAreBounded()
    {
        var builder=new AutoMockLoadRulesBuilder();
        for(var i=0;i<256;i++) builder.ScaleBaseline("r"+i,1);
        Assert.Throws<InvalidOperationException>(()=>builder.ScaleBaseline("extra",1));
        Assert.Equal(256,builder.Build().Count);
    }

    private sealed class ConstantLoad : ILoadBehavior
    {
        public ElectricCurrent CurrentAt(ElectricPotential voltage,TimeSpan elapsed)=>ElectricCurrent.FromAmperes(1);
    }
    /// <summary>Null rule entries are explicit argument failures in builders, hashes and decorators.</summary>
    [Fact] public void NullRulesFailAtTheBoundary()
    {
        Assert.Throws<ArgumentNullException>(()=>new AutoMockLoadRulesBuilder().AddRule(null!));
        Assert.Throws<ArgumentNullException>(()=>AutoMockRuleFiles.Sha256(new AutoMockLoadRule[]{null!}));
        Assert.Throws<ArgumentNullException>(()=>new RuleBasedLoadBehavior(new ConstantLoad(),new AutoMockLoadRule[]{null!}));
    }

// CASES
}
