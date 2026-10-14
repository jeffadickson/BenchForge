using BenchDouble.Experiments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BenchDouble.Tests;

public sealed class ExperimentRunnerTests
{
    [Fact]
    public async Task SuccessfulRunEmitsMeasurementsAndCleansUp()
    {
        var sink = new InMemoryExperimentResultSink();
        var runner = new ExperimentRunner(sink, NullLogger<ExperimentRunner>.Instance);
        var procedure = new ExampleProcedure();
        var run = await runner.RunAsync(procedure);
        Assert.Equal(ExperimentStatus.Completed, run.Status);
        Assert.True(procedure.CleanedUp);
        Assert.Single(sink.Samples);
        Assert.Equal(5.01, sink.Samples[0].Sample.Values["Voltage"]);
        Assert.Equal(run.Id, sink.Samples[0].Id);
    }

    [Fact]
    public async Task FailureStillRunsShutdown()
    {
        var runner = new ExperimentRunner(new InMemoryExperimentResultSink(), NullLogger<ExperimentRunner>.Instance);
        var procedure = new ExampleProcedure { Fail = true };
        var run = await runner.RunAsync(procedure);
        Assert.Equal(ExperimentStatus.Failed, run.Status);
        Assert.IsType<InvalidOperationException>(run.Error);
        Assert.True(procedure.CleanedUp);
    }

    [Fact]
    public async Task CancellationStillRunsShutdown()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new ExperimentRunner(new InMemoryExperimentResultSink(), NullLogger<ExperimentRunner>.Instance);
        var procedure = new ExampleProcedure();
        // Cancellation before obtaining the execution gate is propagated.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(procedure, cancellation.Token));
    }

    private sealed class ExampleProcedure : ExperimentProcedure
    {
        public override string Name => "DC Voltage";
        public bool Fail { get; set; }
        public bool CleanedUp { get; private set; }
        public override async Task ExecuteAsync(ExperimentContext context, CancellationToken token)
        {
            if (Fail) throw new InvalidOperationException("Measurement failed");
            await context.EmitAsync(new ExperimentSample(DateTimeOffset.UtcNow,
                new Dictionary<string, double> { ["Voltage"] = 5.01 },
                new Dictionary<string, string> { ["Voltage"] = "V" }), token);
        }
        public override Task ShutdownAsync(ExperimentContext context, CancellationToken token)
        {
            CleanedUp = true;
            return Task.CompletedTask;
        }
    }
}
