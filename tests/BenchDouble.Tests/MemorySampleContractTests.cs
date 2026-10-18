using BenchDouble.Experiments;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Atomic, detached and bounded in-memory experiment evidence regressions.</summary>
public sealed class MemorySampleContractTests
{
    private static ExperimentSample Sample(IReadOnlyDictionary<string,double>? values=null,IReadOnlyDictionary<string,string>? units=null)=>new(DateTimeOffset.UnixEpoch,values??new Dictionary<string,double>{{"V",1}},units);
    /// <summary>Mutating input dictionaries cannot rewrite stored readings or units.</summary>
    /// <returns>Completion after detached snapshot checks.</returns>
    [Fact] public async Task CallerEditsCannotRewriteSamples()
    {
        var values=new Dictionary<string,double>{{"V",1}};var units=new Dictionary<string,string>{{"V","V"}};var sink=new InMemoryExperimentResultSink();await sink.WriteAsync(Guid.NewGuid(),Sample(values,units),default);values["V"]=9;units["V"]="changed";
        var stored=Assert.Single(sink.Samples).Sample;Assert.Equal(1,stored.Values["V"]);Assert.Equal("V",stored.Units!["V"]);
        Assert.Throws<NotSupportedException>(()=>((IDictionary<string,double>)stored.Values)["V"]=7);Assert.Throws<NotSupportedException>(()=>((IDictionary<string,string>)stored.Units)["V"]="changed");
    }
    /// <summary>Invalid numeric results cannot append partial evidence.</summary>
    /// <param name="value">Invalid measurement.</param>
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)] public void NonfiniteValuesAreAtomic(double value)
    {
        var sink=new InMemoryExperimentResultSink();Assert.Throws<ArgumentException>(()=>sink.WriteAsync(Guid.NewGuid(),Sample(new Dictionary<string,double>{{"first",1},{"bad",value}}),default));Assert.Empty(sink.Samples);
    }
    /// <summary>Field limits accept 256 entries and reject 257 without an extra sample.</summary>
    /// <returns>Completion after boundary checks.</returns>
    [Fact] public async Task FieldCountIsBounded()
    {
        var sink=new InMemoryExperimentResultSink();var values=Enumerable.Range(0,256).ToDictionary(i=>"v"+i,i=>(double)i);await sink.WriteAsync(Guid.NewGuid(),Sample(values),default);values.Add("overflow",1);
        Assert.Throws<ArgumentException>(()=>sink.WriteAsync(Guid.NewGuid(),Sample(values),default));Assert.Single(sink.Samples);
    }
    /// <summary>Display text cannot include controls or blank identifiers.</summary>
    /// <param name="name">Invalid field name.</param>
    [Theory] [InlineData("")] [InlineData(" ")] [InlineData("v\n")] [InlineData("v\0")] public void InvalidFieldNamesAreRejected(string name)
    {
        var sink=new InMemoryExperimentResultSink();Assert.Throws<ArgumentException>(()=>sink.WriteAsync(Guid.NewGuid(),Sample(new Dictionary<string,double>{{name,1}}),default));Assert.Empty(sink.Samples);
    }
    /// <summary>Unit metadata must describe an existing field and valid bounded text.</summary>
    /// <param name="key">Unit field association.</param>
    /// <param name="unit">Invalid unit metadata.</param>
    [Theory] [InlineData("other","V")] [InlineData("V","")] [InlineData("V","V\n")] public void InvalidUnitsAreRejected(string key,string unit)
    {
        var sink=new InMemoryExperimentResultSink();Assert.Throws<ArgumentException>(()=>sink.WriteAsync(Guid.NewGuid(),Sample(units:new Dictionary<string,string>{{key,unit}}),default));Assert.Empty(sink.Samples);
    }
    /// <summary>Maximum text lengths are inclusive and oversized fields do not publish.</summary>
    /// <returns>Completion after valid and invalid boundary cases.</returns>
    [Fact] public async Task TextLengthBoundsAreInclusive()
    {
        var sink=new InMemoryExperimentResultSink();var name=new string('v',128);await sink.WriteAsync(Guid.NewGuid(),Sample(new Dictionary<string,double>{{name,1}},new Dictionary<string,string>{{name,new string('u',80)}}),default);
        Assert.Throws<ArgumentException>(()=>sink.WriteAsync(Guid.NewGuid(),Sample(new Dictionary<string,double>{{name+"v",1}}),default));Assert.Throws<ArgumentException>(()=>sink.WriteAsync(Guid.NewGuid(),Sample(units:new Dictionary<string,string>{{"V",new string('u',81)}}),default));Assert.Single(sink.Samples);
    }
    /// <summary>Samples require run identity and values; cancellation leaves the sink empty.</summary>
    [Fact] public void MissingIdentityAndCancellationAreAtomic()
    {
        var sink=new InMemoryExperimentResultSink();Assert.Throws<ArgumentException>(()=>sink.WriteAsync(Guid.Empty,Sample(),default));Assert.Throws<ArgumentNullException>(()=>sink.WriteAsync(Guid.NewGuid(),new(DateTimeOffset.UnixEpoch,null!),default));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>sink.WriteAsync(Guid.NewGuid(),Sample(),cancel.Token));Assert.Empty(sink.Samples);
    }
    /// <summary>Parallel publishers retain independent IDs and detached collection snapshots.</summary>
    /// <returns>Completion after concurrent publication.</returns>
    [Fact] public async Task ParallelPublicationPreservesEveryRun()
    {
        var sink=new InMemoryExperimentResultSink();var before=sink.Samples;var ids=Enumerable.Range(0,128).Select(_=>Guid.NewGuid()).ToArray();await Task.WhenAll(ids.Select(id=>Task.Run(async()=>await sink.WriteAsync(id,Sample(),default))));
        Assert.Empty(before);Assert.Equal(128,sink.Samples.Count);Assert.Equal(ids.Order(),sink.Samples.Select(x=>x.Id).Order());
    }
}
