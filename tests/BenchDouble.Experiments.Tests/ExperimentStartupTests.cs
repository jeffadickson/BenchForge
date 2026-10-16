using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>Verifies eager simulation startup and cleanup after partial construction.</summary>
public sealed class ExperimentStartupTests
{
    /// <summary>Every role is resolved once before a session is returned.</summary>
    /// <returns>Completion of eager construction and disposal checks.</returns>
    [Fact]
    public async Task SuccessfulStartupResolvesRolesOnce()
    {
        var created = new List<Resource>();
        var definition = new ExperimentDefinitionBuilder()
            .Simulated<Resource>("first", _ => AddResource(created))
            .Simulated<Resource>("second", _ => AddResource(created))
            .Build("eager startup");
        var run = await definition.StartSimulatedAsync();
        Assert.Equal(2, created.Count);
        Assert.Same(created[0], run.Instrument<Resource>("first"));
        Assert.Same(created[1], run.Instrument<Resource>("second"));
        await run.DisposeAsync();
        await run.DisposeAsync();
        Assert.All(created, resource => Assert.Equal(1, resource.Disposals));
    }

    /// <summary>A later factory failure awaits cleanup of already constructed instruments.</summary>
    /// <returns>Completion of partial startup cleanup checks.</returns>
    [Fact]
    public async Task FactoryFailureCleansUpEarlierRoles()
    {
        var resource = new Resource();
        var failure = new InvalidOperationException("factory failure");
        var definition = new ExperimentDefinitionBuilder()
            .Simulated<Resource>("first", _ => resource)
            .Simulated<Resource>("second", _ => throw failure)
            .Build("failed startup");
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await definition.StartSimulatedAsync()));
        Assert.Equal(1, resource.Disposals);
    }

    /// <summary>Pre-cancelled startup does not invoke factories.</summary>
    /// <returns>Completion of cancellation checks.</returns>
    [Fact]
    public async Task PreCancellationDoesNotConstructInstruments()
    {
        var calls = 0;
        var definition = new ExperimentDefinitionBuilder()
            .Simulated<Resource>("first", _ => { calls++; return new Resource(); })
            .Build("cancelled startup");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await definition.StartSimulatedAsync(cancellation.Token));
        Assert.Equal(0, calls);
    }

    /// <summary>Cancellation after the last factory still disposes the newly constructed resource.</summary>
    /// <returns>Completion of cancellation and cleanup checks.</returns>
    [Fact]
    public async Task CancellationDuringFactoryCleansUpCompletedResource()
    {
        using var cancellation = new CancellationTokenSource();
        var resource = new Resource();
        var definition = new ExperimentDefinitionBuilder()
            .Simulated<Resource>("first", _ => { cancellation.Cancel(); return resource; })
            .Build("cancelled during startup");
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await definition.StartSimulatedAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, resource.Disposals);
    }

    /// <summary>Cleanup failure does not hide the original construction failure.</summary>
    /// <returns>Completion of error preservation checks.</returns>
    [Fact]
    public async Task StartupAndCleanupFailuresAreBothPreserved()
    {
        var cleanup = new InvalidOperationException("cleanup failure");
        var startup = new InvalidOperationException("startup failure");
        var resource = new Resource(cleanup);
        var definition = new ExperimentDefinitionBuilder()
            .Simulated<Resource>("first", _ => resource)
            .Simulated<Resource>("second", _ => throw startup)
            .Build("two failures");
        var error = await Assert.ThrowsAsync<AggregateException>(
            async () => await definition.StartSimulatedAsync());
        Assert.Equal(new Exception[] { startup, cleanup }, error.InnerExceptions);
        Assert.Equal(1, resource.Disposals);
    }

    private static Resource AddResource(List<Resource> resources)
    {
        var resource = new Resource();
        resources.Add(resource);
        return resource;
    }

    private sealed class Resource(Exception? failure = null) : IAsyncDisposable
    {
        public int Disposals { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            Disposals++;
            if (failure is not null) throw failure;
        }
    }
}
