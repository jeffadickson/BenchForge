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

    /// <summary>Reading snapshots preserve quantity unit and one-based channel.</summary>
    [Fact] public void ReadingMetadata()
    {
        var s=Session(); var r=s.GetReadingsSnapshot(); Assert.Equal(2,r.Count); Assert.Equal("voltage",r[0].Parameter); Assert.Equal("ElectricPotential",r[0].Quantity); Assert.Equal("Volt",r[0].Unit); Assert.Equal(1,r[0].Channel); Assert.Equal(2,r[1].Channel);
    }

    /// <summary>Reading snapshots remain detached from later writes.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task DetachedReadings()
    {
        var s=Session(); var before=s.GetReadingsSnapshot(); await s.WriteAsync("V1 7"); Assert.Equal(1,before[0].Value); Assert.Equal(7,s.GetReadingsSnapshot()[0].Value);
    }

    /// <summary>Reading snapshot lists reject mutation.</summary>
    [Fact] public void ReadOnlyReadings()
    {
        var s=Session(); var snap=Assert.IsAssignableFrom<IList<MockScpiReading>>(s.GetReadingsSnapshot()); Assert.Throws<NotSupportedException>(()=>snap.Clear());
    }

    /// <summary>Reading snapshots retain declaration and channel order.</summary>
    [Fact] public void ParameterOrder()
    {
        var s=new DefinitionDrivenScpiMockSession(2,[Parameter(name:"z",query:"Z?",setter:null),Parameter(name:"a",query:"A?",setter:null)]); Assert.Equal(new[]{"z1","z2","a1","a2"},s.GetReadingsSnapshot().Select(r=>r.Parameter+r.Channel));
    }

    /// <summary>Reset restores every channel default.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task ResetAllChannels()
    {
        var s=Session(); await s.WriteAsync("V1 3"); await s.WriteAsync("V2 4"); await s.ResetAsync(); Assert.All(s.GetReadingsSnapshot(),r=>Assert.Equal(1,r.Value)); Assert.Empty(s.GetTranscriptSnapshot());
    }

    /// <summary>Reset uses declared defaults rather than zero.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task ResetCustomDefault()
    {
        var s=new DefinitionDrivenScpiMockSession(1,[Parameter(initial:7)]); await s.WriteAsync("V1 2"); await s.ResetAsync(); Assert.Equal("7",await s.QueryAsync("V1?"));
    }

    /// <summary>Reset restores independent parameter defaults.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task ResetMultipleParameters()
    {
        var s=new DefinitionDrivenScpiMockSession(1,[Parameter(name:"a",query:"A?",setter:"A {value}",initial:2),Parameter(name:"b",query:"B?",setter:"B {value}",initial:3)]); await s.WriteAsync("A 9"); await s.WriteAsync("B 8"); await s.ResetAsync(); Assert.Equal(new[]{2d,3d},s.GetReadingsSnapshot().Select(r=>r.Value));
    }

    /// <summary>Cancelled reset preserves values and transcript.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task ResetCancellation()
    {
        var s=Session(); await s.WriteAsync("V1 6"); using var c=new CancellationTokenSource(); c.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await s.ResetAsync(c.Token)); Assert.Equal(6,s.GetReadingsSnapshot()[0].Value); Assert.Single(s.GetTranscriptSnapshot());
    }

    /// <summary>Cancelled history clear preserves history.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task ClearCancellation()
    {
        var s=Session(); await s.QueryAsync("V1?"); using var c=new CancellationTokenSource(); c.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await s.ClearTranscriptAsync(c.Token)); Assert.Single(s.GetTranscriptSnapshot());
    }

    /// <summary>Cancelled writes preserve state and history.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task WriteCancellation()
    {
        var s=Session(); using var c=new CancellationTokenSource(); c.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await s.WriteAsync("V1 9",c.Token)); Assert.All(s.GetReadingsSnapshot(),r=>Assert.Equal(1,r.Value)); Assert.Empty(s.GetTranscriptSnapshot());
    }

    /// <summary>Cancelled queries leave history unchanged.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task QueryCancellation()
    {
        var s=Session(); using var c=new CancellationTokenSource(); c.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await s.QueryAsync("V1?",c.Token)); Assert.Empty(s.GetTranscriptSnapshot());
    }

    /// <summary>History clear never resets reading state.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task ClearPreservesValues()
    {
        var s=Session(); await s.WriteAsync("V1 8"); await s.ClearTranscriptAsync(); Assert.Equal(8,s.GetReadingsSnapshot()[0].Value); Assert.Empty(s.GetTranscriptSnapshot());
    }

    /// <summary>Repeated reset remains coherent and empty.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task ResetIdempotence()
    {
        var s=Session(); await s.ResetAsync(); await s.ResetAsync(); Assert.Empty(s.GetTranscriptSnapshot()); Assert.All(s.GetReadingsSnapshot(),r=>Assert.Equal(1,r.Value));
    }

    /// <summary>Reset does not affect another mock session.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task IndependentSessions()
    {
        var a=Session(); var b=Session(); await a.WriteAsync("V1 2"); await b.WriteAsync("V1 3"); await a.ResetAsync(); Assert.Equal(3,b.GetReadingsSnapshot()[0].Value); Assert.Single(b.GetTranscriptSnapshot());
    }

    /// <summary>Nonfinite writes preserve snapshot state.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task NonfiniteWrite()
    {
        var s=Session(); await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async()=>await s.WriteAsync("V1 NaN")); Assert.All(s.GetReadingsSnapshot(),r=>Assert.Equal(1,r.Value)); Assert.Empty(s.GetTranscriptSnapshot());
    }

    /// <summary>Below-range writes preserve state.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task BelowMinimum()
    {
        var s=Session(); await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async()=>await s.WriteAsync("V1 -1")); Assert.Equal(1,s.GetReadingsSnapshot()[0].Value);
    }

    /// <summary>Above-range writes preserve state.</summary>
    /// <returns>Completion of hardware-free state verification.</returns>
    [Fact] public async Task AboveMaximum()
    {
        var s=Session(); await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async()=>await s.WriteAsync("V1 11")); Assert.Equal(1,s.GetReadingsSnapshot()[0].Value);
    }
}
