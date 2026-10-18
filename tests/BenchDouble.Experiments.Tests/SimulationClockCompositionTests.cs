using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>Canonical simulation clock selection, default compatibility and failed-start cleanup.</summary>
public sealed class SimulationClockCompositionTests
{
    private sealed record Consumer(ISimulationClock Clock);
    private sealed class Owned : IAsyncDisposable
    {
        internal int Disposals;
        /// <inheritdoc/>
        public ValueTask DisposeAsync() { ++Disposals; return ValueTask.CompletedTask; }
    }
    /// <summary>The existing manual property and injected interface refer to the same default run clock.</summary>
    /// <returns>Completion after default clock identity checks.</returns>
    [Fact] public async Task DefaultManualClockRemainsCanonical()
    {
        var definition=new ExperimentDefinitionBuilder().Simulated<Consumer>("probe",sp=>new(sp.GetRequiredService<ISimulationClock>())).Build("manual");
        await using var run=await definition.StartSimulatedAsync(); Assert.Same(run.Clock,run.SimulationTime); Assert.Same(run.SimulationTime,run.Instrument<Consumer>("probe").Clock);
        run.Clock.AdvanceBy(TimeSpan.FromSeconds(2)); Assert.Equal(TimeSpan.FromSeconds(2),run.SimulationTime.Elapsed);
    }
    /// <summary>Adjustable selection removes the hidden manual authority and creates independent clocks for every run.</summary>
    /// <returns>Completion after canonical identity, compatibility and run-isolation checks.</returns>
    [Fact] public async Task AdjustableClockIsCanonicalAndRunScoped()
    {
        var definition=new ExperimentDefinitionBuilder().AdjustableClock(60).Simulated<Consumer>("probe",sp=>new(sp.GetRequiredService<ISimulationClock>())).Build("adjustable");
        await using var first=await definition.StartSimulatedAsync(); await using var second=await definition.StartSimulatedAsync();
        var clock=Assert.IsType<AdjustableSimulationClock>(first.SimulationTime); Assert.Same(clock,first.Instrument<Consumer>("probe").Clock); Assert.NotSame(clock,second.SimulationTime);
        Assert.Throws<InvalidOperationException>(()=>first.Clock); clock.AdvanceBy(TimeSpan.FromSeconds(2)); Assert.Equal(TimeSpan.Zero,second.SimulationTime.Elapsed);
    }
    /// <summary>A null clock fails eager startup before roles run and disposes dependencies resolved by its factory.</summary>
    /// <returns>Completion after failed clock construction and cleanup.</returns>
    [Fact] public async Task NullClockFactoryFailsBeforeRolesAndCleansDependencies()
    {
        var owned=new Owned(); var calls=0;
        var definition=new ExperimentDefinitionBuilder().Model<Owned>(_=>owned).UseSimulationClock(sp=>{sp.GetRequiredService<Owned>();return null!;})
            .Simulated<Consumer>("probe",sp=>{++calls;return new(sp.GetRequiredService<ISimulationClock>());}).Build("bad clock");
        var error=await Assert.ThrowsAsync<InvalidOperationException>(async()=>await definition.StartSimulatedAsync());
        Assert.Contains("clock",error.Message); Assert.Equal(0,calls); Assert.Equal(1,owned.Disposals);
    }

    /// <summary>Composition never invokes factories and rejects a second clock policy.</summary>
    [Fact] public void DuplicateSelectionDoesNotInvokeFactories()
    {
        var calls=0; var builder=new ExperimentDefinitionBuilder().UseSimulationClock(_=>{++calls;return new ManualSimulationClock();});
        Assert.Throws<InvalidOperationException>(()=>builder.AdjustableClock()); Assert.Throws<InvalidOperationException>(()=>builder.UseSimulationClock(_=>new ManualSimulationClock()));
        builder.Build("single clock"); Assert.Equal(0,calls);
    }

    /// <summary>The manual clock cannot be reintroduced as an independent model after canonical selection.</summary>
    [Fact] public void ReservedClockModelIsRejected()
    {
        var builder=new ExperimentDefinitionBuilder().AdjustableClock();
        Assert.Throws<InvalidOperationException>(()=>builder.Model<ManualSimulationClock>(_=>new())); builder.Build("reserved");
    }

    /// <summary>Disposed sessions reject both canonical and legacy clock access.</summary>
    /// <returns>Completion after disposing the session.</returns>
    [Fact] public async Task DisposedClockAccessIsRejected()
    {
        var run=await new ExperimentDefinitionBuilder().Build("disposed").StartSimulatedAsync(); await run.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(()=>run.SimulationTime); Assert.Throws<ObjectDisposedException>(()=>run.Clock);
    }

    /// <summary>Eager startup resolves one clock before roles and shares it throughout the run.</summary>
    /// <returns>Completion after canonical resolution and disposal.</returns>
    [Fact] public async Task ClockFactoryRunsOnceBeforeRoles()
    {
        var calls=0; var definition=new ExperimentDefinitionBuilder().UseSimulationClock(_=>{++calls;return new ManualSimulationClock();})
            .Simulated<Consumer>("first",sp=>{Assert.Equal(1,calls);Assert.Null(sp.GetService<ManualSimulationClock>());return new(sp.GetRequiredService<ISimulationClock>());})
            .Simulated<Consumer>("second",sp=>new(sp.GetRequiredService<ISimulationClock>())).Build("canonical");
        await using var run=await definition.StartSimulatedAsync(); Assert.Equal(1,calls); Assert.Same(run.SimulationTime,run.Clock);
        Assert.Same(run.Instrument<Consumer>("first").Clock,run.Instrument<Consumer>("second").Clock);
    }

    /// <summary>Rule callbacks cannot mutate the outer clock policy and leave selection available after rollback.</summary>
    [Fact] public void CallbackCannotSelectClock()
    {
        var builder=new ExperimentDefinitionBuilder();
        Assert.Throws<InvalidOperationException>(()=>builder.AutoMockLoad(_=>new ResistiveLoadBehavior(UnitsNet.ElectricResistance.FromOhms(10)),_=>builder.AdjustableClock()));
        builder.AdjustableClock().Build("retry");
    }

    /// <summary>A failed lazy factory leaves dependency cleanup under the caller-owned session.</summary>
    /// <returns>Completion after explicit lazy session disposal.</returns>
    [Fact] public async Task LazyFailureRetainsCallerOwnedCleanup()
    {
        var owned=new Owned(); var calls=0; var failure=new IOException("clock factory");
        var definition=new ExperimentDefinitionBuilder().Model<Owned>(_=>owned).UseSimulationClock(sp=>{++calls;sp.GetRequiredService<Owned>();throw failure;}).Build("lazy");
        var run=definition.StartSimulated(); Assert.Equal(0,calls); Assert.Same(failure,Assert.Throws<IOException>(()=>run.SimulationTime)); Assert.Equal(0,owned.Disposals);
        await run.DisposeAsync(); Assert.Equal(1,owned.Disposals);
    }

    /// <summary>Runs with no roles still validate their clock at eager startup.</summary>
    /// <returns>Completion after failed startup.</returns>
    [Fact] public async Task EmptyRunStillValidatesClock()
    {
        var calls=0; var definition=new ExperimentDefinitionBuilder().UseSimulationClock(_=>{++calls;return null!;}).Build("empty");
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await definition.StartSimulatedAsync()); Assert.Equal(1,calls);
    }

    private sealed class OwnedClock : ISimulationClock,IAsyncDisposable
    {
        internal int Disposals;
        /// <inheritdoc/>
        public TimeSpan Elapsed => TimeSpan.Zero;
        /// <inheritdoc/>
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }
    /// <summary>The run owns exactly one disposal of a returned disposable canonical clock.</summary>
    /// <returns>Completion after repeated concurrent session disposal.</returns>
    [Fact] public async Task CanonicalClockIsDisposedOnce()
    {
        var clock=new OwnedClock(); var run=await new ExperimentDefinitionBuilder().UseSimulationClock(_=>clock).Build("owned").StartSimulatedAsync();
        await Task.WhenAll(Enumerable.Range(0,8).Select(_=>run.DisposeAsync().AsTask())); Assert.Equal(1,clock.Disposals);
    }
}
