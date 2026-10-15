using BenchDouble;
using BenchDouble.Async;
using Xunit;

namespace BenchDouble.Tests;

public class AsyncInstrumentTests
{
    [Fact]
    public async Task LegacySimulatorRemainsUsableThroughAsyncCompatibilityAdapter()
    {
        var model = new GeneratorSimulator();
        using var serialized = new SerializedAsyncScpiSession(new SynchronousScpiAdapter(model));
        var generator = new AsyncFunctionGenerator(serialized);
        await generator.ConfigureSineAsync(2, 5000, 2);
        await generator.SetOutputAsync(2, true);
        Assert.True(model.Outputs[2]);
        Assert.Equal(5000, model.Frequencies[2]);
    }

    [Fact]
    public async Task CancelledOperationDoesNotReachLegacyInstrument()
    {
        var model = new MultimeterSimulator();
        var adapter = new SynchronousScpiAdapter(model);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await adapter.QueryAsync("*IDN?", cancellation.Token));
        Assert.Empty(model.Transcript);
    }

    [Fact]
    public async Task ConcurrentQueriesOnOneSessionAreSerialized()
    {
        var fake = new DelayedSession();
        using var session = new SerializedAsyncScpiSession(fake);
        var first = session.QueryAsync("A?").AsTask();
        await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = session.QueryAsync("B?").AsTask();
        Assert.Equal(1, fake.EnteredCount);
        fake.Release.TrySetResult(true);
        Assert.Equal("A?", await first);
        Assert.Equal("B?", await second);
        Assert.Equal(1, fake.MaxConcurrent);
    }

    private sealed class DelayedSession : IAsyncScpiSession
    {
        private int _active;
        public int EnteredCount { get; private set; }
        public int MaxConcurrent { get; private set; }
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask WriteAsync(string command, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public async ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default)
        {
            int count = Interlocked.Increment(ref _active);
            MaxConcurrent = Math.Max(MaxConcurrent, count);
            EnteredCount++;
            Entered.TrySetResult(true);
            try { await Release.Task.WaitAsync(cancellationToken); return command; }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
