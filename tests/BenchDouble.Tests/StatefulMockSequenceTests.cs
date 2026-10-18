using BenchDouble.Generated;
using BenchDouble.Simulation;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Sequence output acceptance, cancellation and concurrency regressions for TR-043.</summary>
public sealed class StatefulMockSequenceTests
{
    private const string Definition = """
        {"schemaVersion":2,"identity":{"driverName":"SequenceMeter"},
         "modes":[{"id":"v","selectionCommand":"CONF:V"},{"id":"r","selectionCommand":"CONF:R"}],
         "capabilities":[
          {"name":"a","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"A?","modes":["v"],"mockDefault":0.5},
          {"name":"b","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"B?","modes":["v"],"mockDefault":0.25}]}
        """;
    private static StatefulMockEngine Engine(ISimulationClock? clock = null) => new(ImportedInstrumentDefinition.Parse(Definition),clock);
    private sealed class CountingClock : ISimulationClock
    {
        internal TimeSpan Now;
        internal int Reads;
        /// <inheritdoc/>
        public TimeSpan Elapsed { get { ++Reads; return Now; } }
    }

    /// <summary>A strict sequence returns each value once and never silently falls back after exhaustion.</summary>
    /// <returns>Completion after ordered output and explicit exhaustion.</returns>
    [Fact] public async Task StrictSequenceHasExplicitExhaustion()
    {
        var engine = Engine(); engine.SetSequence("a",[1,2,3]); await engine.ConfigureModeAsync("v");
        Assert.Equal(new[] { 1d,2d,3d },new[] { await engine.MeasureAsync("a"),await engine.MeasureAsync("a"),await engine.MeasureAsync("a") });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.MeasureAsync("a"));
        Assert.True(engine.SequenceStatus("a")!.Exhausted); Assert.Equal(0,engine.SequenceStatus("a")!.Remaining);
    }

    /// <summary>Hold-last mode stops changing cursor and revision after its initial values are consumed.</summary>
    /// <returns>Completion after repeated held readings.</returns>
    [Fact] public async Task RepeatLastDoesNotInventFurtherStateChanges()
    {
        var engine = Engine(); engine.SetSequence("a",[1,2],true); await engine.ConfigureModeAsync("v");
        Assert.Equal(1,await engine.MeasureAsync("a")); Assert.Equal(2,await engine.MeasureAsync("a")); var revision = engine.Snapshot().Revision;
        for (int i=0;i<8;i++) Assert.Equal(2,await engine.MeasureAsync("a"));
        Assert.Equal(revision,engine.Snapshot().Revision); Assert.Equal(2,engine.SequenceStatus("a")!.Position);
    }

    /// <summary>Sequence input arrays and old cursor snapshots remain independent of later mutation and reads.</summary>
    /// <returns>Completion after snapshot and input isolation checks.</returns>
    [Fact] public async Task SequenceInputsAndSnapshotsAreDetached()
    {
        var values = new[] { 1d,2d }; var engine = Engine(); engine.SetSequence("a",values); values[0]=99;
        var snapshot = engine.Snapshot(); await engine.ConfigureModeAsync("v"); Assert.Equal(1,await engine.MeasureAsync("a"));
        Assert.Equal(0,snapshot.SequencePositions["a"]); Assert.Equal(1,engine.Snapshot().SequencePositions["a"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string,int>)snapshot.SequencePositions).Add("other",5));
    }

    /// <summary>One-shot faults take precedence without discarding a sequence value.</summary>
    /// <returns>Completion after fault delivery and the first valid sequence reading.</returns>
    [Fact] public async Task FaultDeliveryDoesNotAdvanceSequence()
    {
        var engine = Engine(); engine.SetSequence("a",[7]); await engine.ConfigureModeAsync("v"); var fault = new IOException("one-shot"); engine.QueueFault("a",fault);
        Assert.Same(fault,await Assert.ThrowsAsync<IOException>(async () => await engine.MeasureAsync("a")));
        Assert.Equal(0,engine.SequenceStatus("a")!.Position); Assert.Equal(7,await engine.MeasureAsync("a"));
    }

    /// <summary>Concurrent callers consume each sequence element exactly once through the canonical gate.</summary>
    /// <returns>Completion after parallel reading and cursor checks.</returns>
    [Fact] public async Task ParallelReadsConsumeExactlyOnce()
    {
        var engine = Engine(); engine.SetSequence("a",Enumerable.Range(0,64).Select(i=>(double)i)); await engine.ConfigureModeAsync("v");
        var before = engine.Snapshot().Revision;
        var values = await Task.WhenAll(Enumerable.Range(0,64).Select(_ => Task.Run(async () => await engine.MeasureAsync("a"))));
        Assert.Equal(Enumerable.Range(0,64).Select(i=>(double)i),values.Order());
        Assert.Equal(64,engine.SequenceStatus("a")!.Position); Assert.Equal(before+64,engine.Snapshot().Revision);
    }

    /// <summary>Missing and incompatible modes cannot consume the first sequence element.</summary>
    /// <returns>Completion after explicit compatible-mode recovery.</returns>
    [Fact] public async Task InvalidModesPreserveCursor()
    {
        var engine=Engine(); engine.SetSequence("a",[7]);
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await engine.MeasureAsync("a"));
        await engine.ConfigureModeAsync("r"); await Assert.ThrowsAsync<InvalidOperationException>(async()=>await engine.MeasureAsync("a"));
        Assert.Equal(0,engine.SequenceStatus("a")!.Position); await engine.ConfigureModeAsync("v"); Assert.Equal(7,await engine.MeasureAsync("a"));
    }

    /// <summary>Pre-cancelled scalar requests leave sequence cursor and revision intact.</summary>
    /// <returns>Completion after cancellation and a valid retry.</returns>
    [Fact] public async Task PreCancellationPreservesCursor()
    {
        var engine=Engine(); engine.SetSequence("a",[7]); await engine.ConfigureModeAsync("v"); var revision=engine.Snapshot().Revision;
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await engine.MeasureAsync("a",cancellation.Token));
        Assert.Equal(revision,engine.Snapshot().Revision); Assert.Equal(0,engine.SequenceStatus("a")!.Position); Assert.Equal(7,await engine.MeasureAsync("a"));
    }

    /// <summary>A reader cancelled behind another capability's model cannot consume a sequence value.</summary>
    /// <returns>Completion after the admitted model finishes and a valid retry succeeds.</returns>
    [Fact] public async Task WaitingCancellationPreservesCursor()
    {
        var engine=Engine(); var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.SetAsyncBehavior("a",async(_,ct)=>{entered.TrySetResult(); await release.Task.WaitAsync(ct); return 11;});
        engine.SetSequence("b",[7]); await engine.ConfigureModeAsync("v"); var admitted=engine.MeasureAsync("a").AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var cancellation=new CancellationTokenSource(); var waiting=engine.MeasureAsync("b",cancellation.Token).AsTask(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await waiting);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(11,await admitted); Assert.Equal(0,engine.SequenceStatus("b")!.Position); Assert.Equal(7,await engine.MeasureAsync("b"));
    }

    /// <summary>Clock rejection leaves a finite sequence available after the clock is repaired.</summary>
    /// <returns>Completion after invalid time and recovery.</returns>
    [Fact] public async Task InvalidClockPreservesCursor()
    {
        var clock=new CountingClock{Now=TimeSpan.FromTicks(-1)}; var engine=Engine(clock); engine.SetSequence("a",[7]); await engine.ConfigureModeAsync("v");
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await engine.MeasureAsync("a")); Assert.Equal(0,engine.SequenceStatus("a")!.Position);
        clock.Now=TimeSpan.Zero; Assert.Equal(7,await engine.MeasureAsync("a"));
    }

    /// <summary>Cached cursor inspection never invokes the injected clock, even when its time is invalid.</summary>
    [Fact] public void CursorInspectionDoesNotReadClock()
    {
        var clock=new CountingClock{Now=TimeSpan.FromTicks(-1)}; var engine=Engine(clock); engine.SetSequence("a",[1,2]);
        var status=engine.SequenceStatus("a")!; Assert.Equal(2,status.Remaining); Assert.False(status.Exhausted); Assert.Equal(0,clock.Reads);
        Assert.Null(engine.SequenceStatus("b")); Assert.Equal(0,clock.Reads);
    }

    /// <summary>Clearing an exhausted sequence restores an explicit scalar override.</summary>
    /// <returns>Completion after sequence removal and fallback measurement.</returns>
    [Fact] public async Task ClearingSequenceRestoresOverride()
    {
        var engine=Engine(); engine.SetReading("a",4); engine.SetSequence("a",[7]); await engine.ConfigureModeAsync("v");
        Assert.Equal(7,await engine.MeasureAsync("a")); Assert.True(engine.ClearSequence("a")); Assert.Equal(4,await engine.MeasureAsync("a"));
    }

    /// <summary>Removing absent sequence state is idempotent and does not advance revision.</summary>
    [Fact] public void ClearingAbsentSequencePreservesRevision()
    {
        var engine=Engine(); var initial=engine.Snapshot().Revision; Assert.False(engine.ClearSequence("a")); Assert.Equal(initial,engine.Snapshot().Revision);
        engine.SetSequence("a",[1]); Assert.True(engine.ClearSequence("a")); var revision=engine.Snapshot().Revision;
        Assert.False(engine.ClearSequence("a")); Assert.Equal(revision,engine.Snapshot().Revision);
    }

    /// <summary>Reset removes every sequence and restores definition defaults after explicit mode selection.</summary>
    /// <returns>Completion after reset and default readings.</returns>
    [Fact] public async Task ResetRemovesAllSequences()
    {
        var engine=Engine(); engine.SetSequence("a",[1]); engine.SetSequence("b",[2]); await engine.ConfigureModeAsync("v"); engine.Reset();
        Assert.Null(engine.Snapshot().Mode); Assert.Empty(engine.Snapshot().SequencePositions); Assert.Null(engine.SequenceStatus("a")); Assert.Null(engine.SequenceStatus("b"));
        await engine.ConfigureModeAsync("v"); Assert.Equal(0.5,await engine.MeasureAsync("a")); Assert.Equal(0.25,await engine.MeasureAsync("b"));
    }

    /// <summary>A synchronous model replaces a sequence without reviving it when the model is removed.</summary>
    /// <returns>Completion after model replacement and fallback.</returns>
    [Fact] public async Task SynchronousModelReplacesSequence()
    {
        var engine=Engine(); engine.SetSequence("a",[1,2]); await engine.ConfigureModeAsync("v"); engine.SetBehavior("a",_=>9);
        Assert.Null(engine.SequenceStatus("a")); Assert.Equal(9,await engine.MeasureAsync("a")); engine.SetBehavior("a",null); Assert.Equal(0.5,await engine.MeasureAsync("a"));
    }

    /// <summary>An async model replaces the same capability's sequence authority.</summary>
    /// <returns>Completion after async model evaluation.</returns>
    [Fact] public async Task AsyncModelReplacesSequence()
    {
        var engine=Engine(); engine.SetSequence("a",[1]); await engine.ConfigureModeAsync("v"); engine.SetAsyncBehavior("a",(_,_)=>ValueTask.FromResult(17d));
        Assert.Null(engine.SequenceStatus("a")); Assert.Equal(17,await engine.MeasureAsync("a"));
    }

    /// <summary>Removing synchronous behavior also removes a sequence while preserving other capabilities.</summary>
    /// <returns>Completion after independent fallback checks.</returns>
    [Fact] public async Task RemovingBehaviorClearsOnlyItsSequence()
    {
        var engine=Engine(); engine.SetSequence("a",[1]); engine.SetSequence("b",[2]); await engine.ConfigureModeAsync("v"); engine.SetBehavior("a",null);
        Assert.Null(engine.SequenceStatus("a")); Assert.Equal(0.5,await engine.MeasureAsync("a")); Assert.Equal(2,await engine.MeasureAsync("b"));
    }

    /// <summary>Removing async behavior restores fallback instead of retaining a hidden sequence.</summary>
    /// <returns>Completion after explicit behavior removal.</returns>
    [Fact] public async Task RemovingAsyncBehaviorClearsSequence()
    {
        var engine=Engine(); engine.SetSequence("a",[1]); await engine.ConfigureModeAsync("v"); engine.SetAsyncBehavior("a",null);
        Assert.Null(engine.SequenceStatus("a")); Assert.Equal(0.5,await engine.MeasureAsync("a"));
    }

    /// <summary>Explicit reinstallation restarts the cursor, preserving previously captured snapshots.</summary>
    /// <returns>Completion after restarted sequence consumption.</returns>
    [Fact] public async Task ReinstallationRestartsCursor()
    {
        var engine=Engine(); engine.SetSequence("a",[1,2]); await engine.ConfigureModeAsync("v"); await engine.MeasureAsync("a"); var old=engine.Snapshot();
        engine.SetSequence("a",[1,2]); Assert.Equal(0,engine.SequenceStatus("a")!.Position); Assert.Equal(old.Revision+1,engine.Snapshot().Revision);
        Assert.Equal(1,old.SequencePositions["a"]); Assert.Equal(1,await engine.MeasureAsync("a"));
    }

    /// <summary>Capabilities own independent cursors and exhaustion outcomes.</summary>
    /// <returns>Completion after interleaved capability readings.</returns>
    [Fact] public async Task CapabilityCursorsAreIndependent()
    {
        var engine=Engine(); engine.SetSequence("a",[1,2]); engine.SetSequence("b",[3]); await engine.ConfigureModeAsync("v");
        Assert.Equal(1,await engine.MeasureAsync("a")); Assert.Equal(3,await engine.MeasureAsync("b"));
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await engine.MeasureAsync("b")); Assert.Equal(2,await engine.MeasureAsync("a"));
        Assert.Equal(2,engine.SequenceStatus("a")!.Position); Assert.Equal(1,engine.SequenceStatus("b")!.Position);
    }

    /// <summary>Two engine instances never share mutable sequence cursors.</summary>
    /// <returns>Completion after independent measurements.</returns>
    [Fact] public async Task EnginesDoNotShareSequenceState()
    {
        var first=Engine(); var second=Engine(); double[] values=[1,2]; first.SetSequence("a",values); second.SetSequence("a",values);
        await first.ConfigureModeAsync("v"); await second.ConfigureModeAsync("v"); await first.MeasureAsync("a");
        Assert.Equal(0,second.SequenceStatus("a")!.Position); Assert.Equal(1,await second.MeasureAsync("a")); Assert.Equal(2,await first.MeasureAsync("a"));
    }

    /// <summary>Rejected sequence replacements preserve the old cursor, mode and revision.</summary>
    /// <param name="kind">Invalid sequence input category.</param>
    /// <returns>Completion after rejection and continued old-sequence reading.</returns>
    [Theory] [InlineData("null")] [InlineData("empty")] [InlineData("oversized")] [InlineData("nan")] [InlineData("positive-infinity")] [InlineData("negative-infinity")]
    public async Task InvalidReplacementIsAtomic(string kind)
    {
        var engine=Engine(); engine.SetSequence("a",[1,2]); await engine.ConfigureModeAsync("v"); Assert.Equal(1,await engine.MeasureAsync("a")); var snapshot=engine.Snapshot();
        IEnumerable<double> values=kind switch { "null"=>null!, "empty"=>[], "oversized"=>Enumerable.Repeat(1d,4097), "nan"=>[double.NaN], "positive-infinity"=>[double.PositiveInfinity], _=>[double.NegativeInfinity] };
        Assert.ThrowsAny<ArgumentException>(()=>engine.SetSequence("a",values)); Assert.Equal(snapshot.Revision,engine.Snapshot().Revision);
        Assert.Equal(1,engine.SequenceStatus("a")!.Position); Assert.Equal(2,await engine.MeasureAsync("a"));
    }

    /// <summary>Oversized lazy sources are stopped at the boundary rather than fully enumerated.</summary>
    [Fact] public void SequenceEnumerationIsBounded()
    {
        var engine=Engine(); Assert.Throws<ArgumentException>(()=>engine.SetSequence("a",Enumerable.Range(0,4098).Select(i=>i==4097?throw new IOException("over-enumerated"):(double)i)));
        Assert.Null(engine.SequenceStatus("a")); Assert.Equal(0,engine.Snapshot().Revision);
    }

    /// <summary>A cancelled request consumes neither the queued failure nor the first sequence value.</summary>
    /// <returns>Completion after cancellation, fault delivery and a successful reading.</returns>
    [Fact] public async Task CancellationPreservesFaultAndSequence()
    {
        var engine=Engine(); engine.SetSequence("a",[7]); await engine.ConfigureModeAsync("v"); var fault=new IOException(); engine.QueueFault("a",fault);
        using var cancel=new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await engine.MeasureAsync("a",cancel.Token));
        Assert.Same(fault,await Assert.ThrowsAsync<IOException>(async()=>await engine.MeasureAsync("a"))); Assert.Equal(7,await engine.MeasureAsync("a"));
    }

    /// <summary>One excess concurrent caller gets exhaustion without duplicating any successful value.</summary>
    /// <returns>Completion after concurrent success and exhaustion accounting.</returns>
    [Fact] public async Task ExcessParallelReaderGetsOnlyExhaustion()
    {
        var engine=Engine(); engine.SetSequence("a",Enumerable.Range(0,32).Select(i=>(double)i)); await engine.ConfigureModeAsync("v");
        var values=new System.Collections.Concurrent.ConcurrentBag<double>(); var failures=0;
        await Task.WhenAll(Enumerable.Range(0,33).Select(_=>Task.Run(async()=>{try{values.Add(await engine.MeasureAsync("a"));}catch(InvalidOperationException){Interlocked.Increment(ref failures);}})));
        Assert.Equal(1,failures); Assert.Equal(Enumerable.Range(0,32).Select(i=>(double)i),values.Order()); Assert.Equal(32,engine.SequenceStatus("a")!.Position);
    }

    /// <summary>Captured source acquisitions do not consume unrelated live scalar sequence positions.</summary>
    /// <returns>Completion after captured and scalar reads.</returns>
    [Fact] public async Task CapturedReadingDoesNotConsumeSequence()
    {
        var engine=Engine(); engine.SetSequence("a",[7]); await engine.ConfigureModeAsync("v");
        var source=new VirtualSourceMeasureUnit(new VirtualSignalBus(),new ManualSimulationClock());
        var captured=source.AcquireCoherently(1,4,UnitsNet.Duration.FromMilliseconds(1));
        Assert.Equal(0,(await engine.ReadCapturedAsync("a",captured)).Value); Assert.Equal(0,engine.SequenceStatus("a")!.Position); Assert.Equal(7,await engine.MeasureAsync("a"));
    }

    /// <summary>The imported-instrument facade shares the same sequence, snapshot and fallback authority.</summary>
    /// <returns>Completion after facade installation, status, clearing and measurement.</returns>
    [Fact] public async Task ImportedFacadeUsesCanonicalSequenceState()
    {
        var mock=new ImportedInstrumentMock(ImportedInstrumentDefinition.Parse(Definition)); mock.SetSequence("a",[7,8]); await mock.ConfigureModeAsync("v");
        Assert.Equal(7,await mock.MeasureAsync("a")); Assert.Equal(1,mock.SequenceStatus("a")!.Position); Assert.Equal(1,mock.Snapshot().SequencePositions["a"]);
        Assert.True(mock.ClearSequence("a")); Assert.Null(mock.SequenceStatus("a")); Assert.Equal(0.5,await mock.MeasureAsync("a"));
    }

    /// <summary>The maximum supported length is accepted rather than confused with the rejection sentinel.</summary>
    /// <returns>Completion after installing and consuming a boundary-size sequence.</returns>
    [Fact] public async Task MaximumLengthSequenceIsAccepted()
    {
        var engine=Engine(); engine.SetSequence("a",Enumerable.Repeat(double.MaxValue,4096)); await engine.ConfigureModeAsync("v");
        Assert.Equal(4096,engine.SequenceStatus("a")!.Length); Assert.Equal(double.MaxValue,await engine.MeasureAsync("a")); Assert.Equal(4095,engine.SequenceStatus("a")!.Remaining);
    }

    /// <summary>Failure while enumerating replacement data preserves the exact failure and old sequence state.</summary>
    /// <returns>Completion after a failed replacement and continued reading.</returns>
    [Fact] public async Task SourceEnumerationFailurePreservesOldSequence()
    {
        var engine=Engine(); engine.SetSequence("a",[7,8]); await engine.ConfigureModeAsync("v"); var before=engine.Snapshot(); var original=new IOException("source");
        IEnumerable<double> Values() { yield return 1; throw original; }
        Assert.Same(original,Assert.Throws<IOException>(()=>engine.SetSequence("a",Values()))); Assert.Equal(before.Revision,engine.Snapshot().Revision); Assert.Equal(7,await engine.MeasureAsync("a"));
    }

    /// <summary>Adding cursor metadata preserves the existing four-field snapshot deconstruction contract.</summary>
    [Fact] public void SnapshotDeconstructionRemainsCompatible()
    {
        var engine=Engine(); engine.SetSequence("a",[7]); var snapshot=engine.Snapshot(); var (mode,revision,elapsed,overrides)=snapshot;
        Assert.Null(mode); Assert.Equal(snapshot.Revision,revision); Assert.Equal(TimeSpan.Zero,elapsed); Assert.Empty(overrides); Assert.Equal(0,snapshot.SequencePositions["a"]);
    }
}
