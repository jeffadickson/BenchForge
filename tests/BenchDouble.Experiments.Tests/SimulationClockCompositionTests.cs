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
}
