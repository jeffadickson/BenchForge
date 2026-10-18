using BenchDouble.Simulation;
using System.Collections.ObjectModel;
using Xunit;
namespace BenchDouble.Tests;

/// <summary>SCPI metadata, ambiguity, cancellation and snapshot regressions for batch 200.</summary>
public sealed class ScpiBatch200Tests
{
    private static MockScpiParameter Parameter(string name="voltage", string quantity="ElectricPotential", string unit="Volt", string? query="V{channel}?", string? setter="V{channel} {value}", double initial=1, double? minimum=0, double? maximum=10) => new(name,quantity,unit,query,setter,initial,minimum,maximum);
    private static DefinitionDrivenScpiMockSession Session() => new(2,[Parameter()]);

    /// <summary>Reject control characters in parameter names.</summary>
    [Fact] public void ControlName()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(name:"v\n")]));
    }

    /// <summary>Reject oversized parameter names.</summary>
    [Fact] public void LongName()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(name:new string('v',129))]));
    }

    /// <summary>Accept the parameter name length boundary.</summary>
    [Fact] public void MaxName()
    {
        var s=new DefinitionDrivenScpiMockSession(1,[Parameter(name:new string('v',128))]); Assert.Equal(128,s.GetReadingsSnapshot()[0].Parameter.Length);
    }

    /// <summary>Reject control characters in quantity identities.</summary>
    [Fact] public void ControlQuantity()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(quantity:"V\t")]));
    }

    /// <summary>Reject oversized quantity identities.</summary>
    [Fact] public void LongQuantity()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(quantity:new string('v',129))]));
    }

    /// <summary>Accept the quantity length boundary.</summary>
    [Fact] public void MaxQuantity()
    {
        var s=new DefinitionDrivenScpiMockSession(1,[Parameter(quantity:new string('v',128))]); Assert.Equal(128,s.GetReadingsSnapshot()[0].Quantity.Length);
    }

    /// <summary>Reject control characters in unit identities.</summary>
    [Fact] public void ControlUnit()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(unit:"V\0")]));
    }

    /// <summary>Reject oversized unit identities.</summary>
    [Fact] public void LongUnit()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(unit:new string('v',81))]));
    }

    /// <summary>Accept the unit length boundary.</summary>
    [Fact] public void MaxUnit()
    {
        var s=new DefinitionDrivenScpiMockSession(1,[Parameter(unit:new string('v',80))]); Assert.Equal(80,s.GetReadingsSnapshot()[0].Unit.Length);
    }

    /// <summary>Reject literal and channel-template query ambiguity.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task OverlappingQueries()
    {
        var s=new DefinitionDrivenScpiMockSession(2,[Parameter(setter:null),Parameter(name:"other",query:"V1?",setter:null)]); await Assert.ThrowsAsync<InvalidOperationException>(async()=>await s.QueryAsync("V1?")); Assert.Empty(s.GetTranscriptSnapshot());
    }

    /// <summary>Reject literal and channel-template setter ambiguity.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task OverlappingSetters()
    {
        var s=new DefinitionDrivenScpiMockSession(2,[Parameter(query:null),Parameter(name:"other",query:null,setter:"V1 {value}")]); await Assert.ThrowsAsync<InvalidOperationException>(async()=>await s.WriteAsync("V1 5")); Assert.All(s.GetReadingsSnapshot(),r=>Assert.Equal(1,r.Value)); Assert.Empty(s.GetTranscriptSnapshot());
    }

    /// <summary>Allow unambiguous channels beside overlapping templates.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task UnambiguousSibling()
    {
        var s=new DefinitionDrivenScpiMockSession(2,[Parameter(setter:null),Parameter(name:"other",query:"V1?",setter:null)]); Assert.Equal("1",await s.QueryAsync("V2?")); Assert.Single(s.GetTranscriptSnapshot());
    }

    /// <summary>History snapshots do not change after later commands.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task DetachedHistory()
    {
        var s=Session(); await s.WriteAsync("V1 2"); var snap=s.GetTranscriptSnapshot(); await s.QueryAsync("V1?"); Assert.Single(snap); Assert.Equal(2,s.GetTranscriptSnapshot().Count);
    }

    /// <summary>History snapshots reject caller mutation.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task ReadOnlyHistory()
    {
        var s=Session(); await s.QueryAsync("V1?"); var snap=Assert.IsAssignableFrom<IList<string>>(s.GetTranscriptSnapshot()); Assert.Throws<NotSupportedException>(()=>snap[0]="bad");
    }

    /// <summary>History clear preserves previously captured history.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task HistoryClearDetaches()
    {
        var s=Session(); await s.QueryAsync("V1?"); var snap=s.GetTranscriptSnapshot(); await s.ClearTranscriptAsync(); Assert.Single(snap); Assert.Empty(s.GetTranscriptSnapshot());
    }
}
