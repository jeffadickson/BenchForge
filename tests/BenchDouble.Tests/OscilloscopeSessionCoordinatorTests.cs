using BenchDouble.Oscilloscopes;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Concurrency and recovery tests for whole-acquisition ownership.</summary>
public sealed class OscilloscopeSessionCoordinatorTests
{
    /// <summary>Verify concurrent acquisitions remain channel consistent.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task ConcurrentAcquisitionsRemainChannelConsistent()
    {
        var coordinator = new OscilloscopeSessionCoordinator(new SimulatedOscilloscopeSession(samples: 128));
        var scope = new CoordinatedOscilloscope(coordinator);
        var jobs = Enumerable.Range(0, 40).Select(async i =>
        {
            int channel = i % 2 + 1;
            var capture = await scope.AcquireAsync(channel, Guid.NewGuid());
            Assert.Equal(channel, capture.Channel);
            Assert.Equal(128, capture.SampleCount);
            Assert.Equal(channel == 1 ? 0 : 1, capture.VoltageAt(0).Volts, 12);
        });
        await Task.WhenAll(jobs);
        Assert.False(await coordinator.RequiresRecoveryAsync());
    }
    /// <summary>Verify failed transaction blocks further commands until recovery.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task FailedTransactionBlocksFurtherCommandsUntilRecovery()
    {
        var coordinator = new OscilloscopeSessionCoordinator(new SimulatedOscilloscopeSession());
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await coordinator.ExecuteAsync<int>(async (session, token) =>
            {
                await session.WriteAsync("UNSUPPORTED", token);
                return 1;
            }));
        Assert.True(await coordinator.RequiresRecoveryAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.ExecuteAsync<int>((_, _) => ValueTask.FromResult(1)));
        await coordinator.ConfirmExternalRecoveryAsync();
        Assert.Equal(1, await coordinator.ExecuteAsync<int>((_, _) => ValueTask.FromResult(1)));
    }
    /// <summary>Verify cancelled waiter cannot enter active transaction.</summary>
    /// <returns>Completion of the asynchronous verification.</returns>
    [Fact]
    public async Task CancelledWaiterCannotEnterActiveTransaction()
    {
        var coordinator = new OscilloscopeSessionCoordinator(new SimulatedOscilloscopeSession());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = coordinator.ExecuteAsync<int>(async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await entered.Task;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await coordinator.ExecuteAsync<int>((_, _) => ValueTask.FromResult(2), cancellation.Token));
        release.SetResult();
        Assert.Equal(1, await active);
        Assert.False(await coordinator.RequiresRecoveryAsync());
    }
}
