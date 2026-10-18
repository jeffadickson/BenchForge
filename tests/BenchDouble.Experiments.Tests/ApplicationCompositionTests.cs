using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using UnitsNet;
using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>Composition rollback, cancellation, disposal and concurrent run-isolation regressions.</summary>
public sealed class ApplicationCompositionTests
{
    private sealed class Owned : IAsyncDisposable
    {
        internal int Disposals;
        /// <inheritdoc/>
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }
    private sealed record Stamp(ISimulationClock Clock);
    private static ILoadBehavior Baseline() => new ResistiveLoadBehavior(ElectricResistance.FromOhms(10));
    private static MockPowerSupply Supply(IServiceProvider services) => new(services.GetRequiredService<ILoadBehavior>(), services.GetRequiredService<ISimulationClock>());

    /// <summary>A rule callback cannot freeze a definition before its outer registration is committed.</summary>
    [Fact] public void CallbackCannotBuildOuterDefinition()
    {
        var builder = new ExperimentDefinitionBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.AutoMockLoad(_ => Baseline(), _ => builder.Build("nested")));
        builder.AutoMockLoad(_ => Baseline(), _ => { }).Simulated<MockPowerSupply>("supply", Supply).Build("valid retry");
    }

    /// <summary>Every registration path rejects outer-builder mutation from a rule callback.</summary>
    /// <param name="operation">Attempted reentrant composition operation.</param>
    [Theory] [InlineData("model")] [InlineData("instrument")] [InlineData("load")] [InlineData("keyed-load")]
    public void CallbackCannotRegisterServices(string operation)
    {
        var builder = new ExperimentDefinitionBuilder();
        void Mutate()
        {
            switch (operation)
            {
                case "model": builder.Model<Owned>(_ => new()); break;
                case "instrument": builder.Simulated<Owned>("nested", _ => new()); break;
                case "load": builder.AutoMockLoad(_ => Baseline(), _ => { }); break;
                default: builder.AutoMockLoadFor("nested", _ => Baseline(), _ => { }); break;
            }
        }
        Assert.Throws<InvalidOperationException>(() => builder.AutoMockLoad(_ => Baseline(), _ => Mutate()));
        builder.AutoMockLoad(_ => Baseline(), _ => { }).Simulated<MockPowerSupply>("supply", Supply).Build("retry");
    }

    /// <summary>A throwing rule callback leaves no partially registered unkeyed behavior.</summary>
    /// <returns>Completion after a valid retry starts and measures.</returns>
    [Fact] public async Task FailedUnkeyedCallbackCanBeRetried()
    {
        var builder = new ExperimentDefinitionBuilder(); var original = new IOException("callback");
        Assert.Same(original, Assert.Throws<IOException>(() => builder.AutoMockLoad(_ => Baseline(), _ => throw original)));
        builder.AutoMockLoad(_ => Baseline(), rules => rules.FixedCurrent("fixed", ElectricCurrent.FromAmperes(2))).Simulated<MockPowerSupply>("supply", Supply);
        await using var run = await builder.Build("retry").StartSimulatedAsync();
        var supply = run.Instrument<MockPowerSupply>("supply"); await supply.SetOutputAsync(true);
        Assert.Equal(2, (await supply.MeasureAsync()).Current.Amperes);
    }

    /// <summary>A failed keyed callback leaves its role and hash available for a reviewed retry.</summary>
    /// <returns>Completion after retrying the same role.</returns>
    [Fact] public async Task FailedKeyedCallbackCanBeRetried()
    {
        var builder = new ExperimentDefinitionBuilder();
        Assert.Throws<IOException>(() => builder.AutoMockLoadFor("supply", _ => Baseline(), _ => throw new IOException()));
        builder.AutoMockLoadFor("supply", _ => Baseline(), rules => rules.FixedCurrent("fixed", ElectricCurrent.FromAmperes(3)))
            .Simulated<MockPowerSupply>("supply", sp => new(sp.GetRequiredKeyedService<ILoadBehavior>("supply"), sp.GetRequiredService<ISimulationClock>()));
        await using var run = await builder.Build("retry").StartSimulatedAsync();
        Assert.Single(run.AutoMockRuleHashes); var supply = run.Instrument<MockPowerSupply>("supply");
        await supply.SetOutputAsync(true); Assert.Equal(3, (await supply.MeasureAsync()).Current.Amperes);
    }

    /// <summary>A null baseline preserves its explicit error and disposes earlier resolved roles.</summary>
    /// <returns>Completion after failed eager startup cleanup.</returns>
    [Fact] public async Task NullUnkeyedBaselineCleansEarlierRole()
    {
        var owned = new Owned();
        var definition = new ExperimentDefinitionBuilder().AutoMockLoad(_ => null!, _ => { })
            .Simulated<Owned>("first", _ => owned).Simulated<MockPowerSupply>("supply", Supply).Build("null baseline");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await definition.StartSimulatedAsync());
        Assert.Contains("baseline", error.Message); Assert.Equal(1, owned.Disposals);
    }

    /// <summary>A null keyed baseline identifies its role and cleans earlier resolved roles.</summary>
    /// <returns>Completion after failed keyed startup cleanup.</returns>
    [Fact] public async Task NullKeyedBaselineIdentifiesRoleAndCleansUp()
    {
        var owned = new Owned();
        var definition = new ExperimentDefinitionBuilder().AutoMockLoadFor("supply", _ => null!, _ => { })
            .Simulated<Owned>("first", _ => owned)
            .Simulated<MockPowerSupply>("supply", sp => new(sp.GetRequiredKeyedService<ILoadBehavior>("supply"), sp.GetRequiredService<ISimulationClock>())).Build("null keyed baseline");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await definition.StartSimulatedAsync());
        Assert.Contains("supply", error.Message); Assert.Equal(1, owned.Disposals);
    }

    /// <summary>Untrusted composition names fail before registering factories.</summary>
    /// <param name="name">Invalid role, host or definition name.</param>
    [Theory] [InlineData("name\n")] [InlineData("name\r")] [InlineData("name\0")] [InlineData(" ")]
    public void InvalidNamesDoNotMutateComposition(string name)
    {
        var builder = new ExperimentDefinitionBuilder(); var calls = 0;
        Assert.Throws<ArgumentException>(() => builder.Simulated<Owned>(name, _ => { ++calls; return new(); }));
        Assert.Throws<ArgumentException>(() => builder.Simulated<Owned>("valid", _ => { ++calls; return new(); }, name));
        Assert.Throws<ArgumentException>(() => builder.Build(name)); Assert.Equal(0, calls);
        Assert.Empty(builder.Build("valid").Instruments);
    }

    /// <summary>Exact name bounds are accepted while oversized names leave the builder reusable.</summary>
    [Fact] public void CompositionNameBoundariesAreExplicit()
    {
        var builder = new ExperimentDefinitionBuilder(); var invalid = new string('x', 129); var valid = new string('x', 128);
        Assert.Throws<ArgumentException>(() => builder.Simulated<Owned>(invalid, _ => new()));
        Assert.Throws<ArgumentException>(() => builder.Simulated<Owned>("valid", _ => new(), invalid));
        Assert.Throws<ArgumentException>(() => builder.Build(invalid));
        Assert.Equal(valid, builder.Simulated<Owned>(valid, _ => new(), valid).Build(valid).Name);
    }

    /// <summary>Two roles share one injected model and canonical clock within a run, while runs remain independent.</summary>
    /// <returns>Completion after scoped identity and virtual-time assertions.</returns>
    [Fact] public async Task SharedModelsRemainRunScoped()
    {
        var definition = new ExperimentDefinitionBuilder().Model(sp => new Stamp(sp.GetRequiredService<ISimulationClock>()))
            .Simulated<Stamp>("a", sp => sp.GetRequiredService<Stamp>()).Simulated<Stamp>("b", sp => sp.GetRequiredService<Stamp>()).Build("scoped");
        await using var first = await definition.StartSimulatedAsync(); await using var second = await definition.StartSimulatedAsync();
        Assert.Same(first.Instrument<Stamp>("a"), first.Instrument<Stamp>("b"));
        Assert.Same(first.Clock, first.Instrument<Stamp>("a").Clock); Assert.NotSame(first.Instrument<Stamp>("a"), second.Instrument<Stamp>("a"));
        first.Clock.AdvanceBy(TimeSpan.FromSeconds(2)); Assert.Equal(TimeSpan.Zero, second.Clock.Elapsed);
    }

    /// <summary>Concurrent starts do not share scoped models, clocks or run identifiers.</summary>
    /// <returns>Completion after all parallel runs are independently disposed.</returns>
    [Fact] public async Task ParallelRunsRemainIndependent()
    {
        var definition = new ExperimentDefinitionBuilder().Simulated<Stamp>("probe", sp => new(sp.GetRequiredService<ISimulationClock>())).Build("parallel");
        var ids = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
        {
            await using var run = await definition.StartSimulatedAsync(); run.Clock.AdvanceBy(TimeSpan.FromSeconds(index));
            Assert.Equal(TimeSpan.FromSeconds(index), run.Instrument<Stamp>("probe").Clock.Elapsed); return run.RunId;
        })));
        Assert.Equal(16, ids.Distinct().Count());
    }

    /// <summary>A null injected model prevents later factories from running and cleans earlier roles.</summary>
    /// <returns>Completion after fail-fast startup and cleanup.</returns>
    [Fact] public async Task NullModelStopsStartupAndCleansEarlierRole()
    {
        var owned = new Owned(); var laterCalls = 0;
        var definition = new ExperimentDefinitionBuilder().Model<Stamp>(_ => null!)
            .Simulated<Owned>("first", _ => owned).Simulated<Stamp>("broken", sp => sp.GetRequiredService<Stamp>())
            .Simulated<Owned>("later", _ => { ++laterCalls; return new(); }).Build("null model");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await definition.StartSimulatedAsync());
        Assert.Equal(1, owned.Disposals); Assert.Equal(0, laterCalls);
    }

    /// <summary>Cancellation in a baseline factory cleans resolved model dependencies before returning.</summary>
    /// <returns>Completion after cancellation and asynchronous model cleanup.</returns>
    [Fact] public async Task BaselineCancellationCleansResolvedModel()
    {
        using var cancellation = new CancellationTokenSource(); var owned = new Owned();
        var definition = new ExperimentDefinitionBuilder().Model<Owned>(_ => owned)
            .AutoMockLoad(sp => { sp.GetRequiredService<Owned>(); cancellation.Cancel(); return Baseline(); }, _ => { })
            .Simulated<MockPowerSupply>("supply", Supply).Build("cancel baseline");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await definition.StartSimulatedAsync(cancellation.Token));
        Assert.Equal(1, owned.Disposals);
    }
}
