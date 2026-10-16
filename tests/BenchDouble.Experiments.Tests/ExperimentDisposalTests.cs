using Xunit;

namespace BenchDouble.Experiments.Tests;

/// <summary>Checks shared cleanup completion under concurrent disposal and failures.</summary>
public sealed class ExperimentDisposalTests
{
    /// <summary>Concurrent and later callers observe the same single cleanup operation.</summary>
    /// <param name="fail">Whether the owned resource fails after cleanup is released.</param>
    /// <returns>Completion of deterministic disposal race checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllDisposersAwaitTheSameOutcome(bool fail)
    {
        var resource = new BlockingResource(fail);
        var definition = new ExperimentDefinitionBuilder()
            .Simulated<BlockingResource>("resource", _ => resource)
            .Build("concurrent cleanup");
        var run = await definition.StartSimulatedAsync();
        var callers = new Task[32];
        try
        {
            Parallel.For(0, callers.Length, index => callers[index] = run.DisposeAsync().AsTask());
            await resource.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(callers, task =>
            {
                Assert.Same(callers[0], task);
                Assert.False(task.IsCompleted);
            });
            Assert.Equal(1, resource.Disposals);
            Assert.Throws<ObjectDisposedException>(() => run.Instrument<BlockingResource>("resource"));
            Assert.Throws<ObjectDisposedException>(() => run.Clock);
        }
        finally { resource.Release.TrySetResult(); }

        foreach (var task in callers)
        {
            if (fail)
                Assert.Same(resource.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() => task));
            else
                await task;
        }
        var later = run.DisposeAsync().AsTask();
        Assert.Same(callers[0], later);
        if (fail)
            Assert.Same(resource.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() => later));
        else
            await later;
        Assert.Equal(1, resource.Disposals);
    }

    private sealed class BlockingResource(bool fail) : IAsyncDisposable
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public InvalidOperationException Failure { get; } = new("cleanup failed");
        public int Disposals;

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposals);
            Started.TrySetResult();
            await Release.Task;
            if (fail) throw Failure;
        }
    }
}
