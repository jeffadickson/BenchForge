using BenchDouble.Generated;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Generated simulation decorator result, cancellation, mode and concurrency contracts.</summary>
public sealed class GeneratedMockBehaviorContractTests
{
    private static async Task<ExampleTwoModeDmmMockDecorator> Voltage()
    {
        var mock=new ExampleTwoModeDmmMockDecorator(new());await mock.ConfigureModeAsync("voltage");return mock;
    }

    /// <summary>UnitsNet rejects invalid quantity construction inside callbacks and scalar state remains usable.</summary>
    /// <param name="value">Nonfinite behavior result.</param>
    /// <returns>Completion after rejected readings and recovery.</returns>
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public async Task InvalidQuantityConstructionIsRejected(double value)
    {
        var mock=await Voltage();mock.SetMockvoltage(ElectricPotential.FromVolts(2));mock.SetvoltageBehavior(_=>ValueTask.FromResult(ElectricPotential.FromVolts(value)));
        await Assert.ThrowsAsync<ArgumentException>(async()=>await mock.GetvoltageAsync());mock.SetvoltageBehavior(null);Assert.Equal(2,(await mock.GetvoltageAsync()).Volts);
    }

    /// <summary>A finite stored value whose canonical conversion overflows is rejected.</summary>
    /// <returns>Completion after canonical-unit validation.</returns>
    [Fact] public async Task CanonicalOverflowIsRejected()
    {
        var mock=await Voltage();mock.SetvoltageBehavior(_=>ValueTask.FromResult(ElectricPotential.FromKilovolts(double.MaxValue)));
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await mock.GetvoltageAsync());
    }

    /// <summary>Cancellation requested inside a callback wins over a finite result.</summary>
    /// <returns>Completion after cancellation and successful subsequent recovery.</returns>
    [Fact] public async Task CallbackCancellationCannotPublishSuccess()
    {
        var mock=await Voltage();using var canceled=new CancellationTokenSource();mock.SetvoltageBehavior(_=>{canceled.Cancel();return ValueTask.FromResult(ElectricPotential.FromVolts(4));});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await mock.GetvoltageAsync(canceled.Token));mock.SetvoltageBehavior(null);Assert.Equal(0.25,(await mock.GetvoltageAsync()).Volts);
    }

    /// <summary>Precancelled requests do not invoke user callbacks.</summary>
    /// <returns>Completion after cancellation is observed.</returns>
    [Fact] public async Task PreCancellationSkipsCallback()
    {
        var mock=await Voltage();var calls=0;mock.SetvoltageBehavior(_=>{++calls;return ValueTask.FromResult(ElectricPotential.FromVolts(4));});using var canceled=new CancellationTokenSource();canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await mock.GetvoltageAsync(canceled.Token));Assert.Equal(0,calls);
    }

    /// <summary>Incompatible modes fail before invoking injected behavior.</summary>
    /// <returns>Completion after mode gating.</returns>
    [Fact] public async Task ModeRejectionSkipsCallback()
    {
        var mock=await Voltage();var calls=0;mock.SetvoltageBehavior(_=>{++calls;return ValueTask.FromResult(ElectricPotential.FromVolts(4));});await mock.ConfigureModeAsync("resistance");
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await mock.GetvoltageAsync());Assert.Equal(0,calls);
    }

    /// <summary>Callback failures retain their identity and leave the decorator usable.</summary>
    /// <returns>Completion after failure and fallback recovery.</returns>
    [Fact] public async Task CallbackFailurePreservesIdentity()
    {
        var mock=await Voltage();var error=new IOException("behavior");mock.SetvoltageBehavior(_=>throw error);
        Assert.Same(error,await Assert.ThrowsAsync<IOException>(async()=>await mock.GetvoltageAsync()));mock.SetvoltageBehavior(null);Assert.Equal(0.25,(await mock.GetvoltageAsync()).Volts);
    }

    /// <summary>Asynchronous callbacks are checked after awaiting their completion.</summary>
    /// <returns>Completion after an asynchronously returned invalid value is rejected.</returns>
    [Fact] public async Task AsyncNonfiniteResultIsRejected()
    {
        var mock=await Voltage();mock.SetvoltageBehavior(async _=>{await Task.Yield();return ElectricPotential.FromKilovolts(double.MaxValue);});
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await mock.GetvoltageAsync());
    }

    /// <summary>Cancellation after suspension is observed even when a callback ignores its token.</summary>
    /// <returns>Completion after controlled callback release.</returns>
    [Fact] public async Task AsyncCancellationIsObserved()
    {
        var mock=await Voltage();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var canceled=new CancellationTokenSource();
        mock.SetvoltageBehavior(async _=>{entered.SetResult();await release.Task;return ElectricPotential.FromVolts(1);});var pending=mock.GetvoltageAsync(canceled.Token).AsTask();
        await entered.Task;canceled.Cancel();release.SetResult();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);
    }

    /// <summary>Behavior removal restores the explicit scalar reading and reset restores the definition default.</summary>
    /// <returns>Completion after precedence and reset checks.</returns>
    [Fact] public async Task BehaviorRemovalPreservesScalarState()
    {
        var mock=await Voltage();mock.SetMockvoltage(ElectricPotential.FromMillivolts(500));mock.SetvoltageBehavior(_=>ValueTask.FromResult(ElectricPotential.FromVolts(3)));
        Assert.Equal(3,(await mock.GetvoltageAsync()).Volts);mock.SetvoltageBehavior(null);Assert.Equal(0.5,(await mock.GetvoltageAsync()).Volts);mock.ResetAllMockReadings();Assert.Equal(0.25,(await mock.GetvoltageAsync()).Volts);
    }

    /// <summary>Parallel scalar assignments and reads remain finite and cannot tear typed state.</summary>
    /// <returns>Completion after concurrent reader and writer tasks.</returns>
    [Fact] public async Task ParallelScalarReadsRemainCoherent()
    {
        var mock=await Voltage();await Task.WhenAll(Enumerable.Range(0,32).Select(i=>Task.Run(async()=>{for(var j=0;j<30;j++){mock.SetMockvoltage(ElectricPotential.FromVolts(i));var value=(await mock.GetvoltageAsync()).Volts;Assert.True(double.IsFinite(value));Assert.InRange(value,0,31);}})));
    }

    /// <summary>Invalid resistance construction in user callbacks cannot publish a reading.</summary>
    /// <param name="value">Nonfinite resistance.</param>
    /// <returns>Completion after validation.</returns>
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public async Task ResistanceUsesSameFiniteContract(double value)
    {
        var mock=new ExampleTwoModeDmmMockDecorator(new());await mock.ConfigureModeAsync("resistance");mock.SetresistanceBehavior(_=>ValueTask.FromResult(ElectricResistance.FromOhms(value)));
        await Assert.ThrowsAsync<ArgumentException>(async()=>await mock.GetresistanceAsync());
    }
}
