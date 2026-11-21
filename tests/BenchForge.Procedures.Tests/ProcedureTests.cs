using BenchForge.Experiments;
using BenchForge.Procedures;
using BenchForge.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Starlark.Eval;
using UnitsNet;
using Xunit;

namespace BenchForge.Procedures.Tests;

/// <summary>Regression tests for the Starlark-cs mock-only procedure parser, preflight, and runner.</summary>
public sealed class ProcedureTests
{
    private static ExperimentDefinition Definition() =>
        new ExperimentDefinitionBuilder()
            .Model(_ => new TypedMockMeasurementsBuilder()
                .Register<ElectricResistance>(t =>
                    ElectricResistance.FromOhms(100 + t.TotalSeconds))
                .Constant(ElectricPotential.FromVolts(12))
                .Build())
            .Simulated<ITypedConfigurableMeasurementSource>("Meter1", sp =>
                new TypedMockMeter(sp.GetRequiredService<TypedMockMeasurements>(),
                    sp.GetRequiredService<ISimulationClock>()))
            .Build("Procedure fixture");

    /// <summary>Verify a Starlark procedure executes against typed mocks and deterministic virtual time.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task StarlarkProcedureExecutesAgainstRealTypedMockAndVirtualClock()
    {
        const string source = """
            # A simple simulated Starlark test procedure
            instrument("Meter1")
            configure("Meter1", "RESISTANCE")
            r1 = measure("Meter1", "RESISTANCE", "R1")
            verify_between(r1, 95, 105, "Ohm")
            wait_s(5)
            r2 = measure("Meter1", "RESISTANCE", "R2")
            verify_between(r2, 100, 110, "Ohm")
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success, string.Join("; ", parsed.Diagnostics.Select(x => x.Message)));
        await using var session = Definition().StartSimulated();
        var sink = new InMemoryExperimentResultSink();
        var runner = new ExperimentRunner(sink, NullLogger<ExperimentRunner>.Instance);
        var procedure = new MockProcedure("Resistance test", parsed.Document!, session);
        var result = await runner.RunAsync(procedure);
        Assert.Equal(ExperimentStatus.Completed, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(5), session.Clock.Elapsed);
        Assert.True(procedure.LastRunSteps > 0);
        Assert.Equal(2, sink.Samples.Count);
        Assert.Equal(100, sink.Samples[0].Sample.Values["R1"]);
        Assert.Equal(105, sink.Samples[1].Sample.Values["R2"]);
        Assert.Equal("Ohm", sink.Samples[1].Sample.Units!["R2"]);
    }

    /// <summary>Verify Starlark functions, finite loops, and quantity arithmetic work deterministically.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task StarlarkProcedureSupportsFunctionsAndFiniteLoops()
    {
        const string source = """
            def sample_step(idx):
                reading = measure("Meter1", "RESISTANCE", f"R{idx}")
                verify_between(reading, 100 + idx, 100 + idx, "Ohm")
                wait_s(1)
                return reading.value

            instrument("Meter1")
            configure("Meter1", "RESISTANCE")
            readings = [sample_step(i) for i in range(3)]
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success, string.Join("; ", parsed.Diagnostics.Select(x => x.Message)));
        await using var session = Definition().StartSimulated();
        var sink = new InMemoryExperimentResultSink();
        var runner = new ExperimentRunner(sink, NullLogger<ExperimentRunner>.Instance);
        var result = await runner.RunAsync(new MockProcedure("Sweep", parsed.Document!, session));
        Assert.Equal(ExperimentStatus.Completed, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(3), session.Clock.Elapsed);
        Assert.Equal(3, sink.Samples.Count);
        Assert.Equal(100, sink.Samples[0].Sample.Values["R0"]);
        Assert.Equal(101, sink.Samples[1].Sample.Values["R1"]);
        Assert.Equal(102, sink.Samples[2].Sample.Values["R2"]);
    }

    /// <summary>Verify a failed limit check reports a failed run with the exact Starlark source line.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task FailedLimitProducesFailedRunAndSourceLine()
    {
        const string source = """
            instrument("Meter1")
            configure("Meter1", "VOLTAGE")
            v1 = measure("Meter1", "VOLTAGE", "V1")
            verify_between(v1, 0, 5, "V")
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success);
        await using var session = Definition().StartSimulated();
        var runner = new ExperimentRunner(new InMemoryExperimentResultSink(),
            NullLogger<ExperimentRunner>.Instance);
        var result = await runner.RunAsync(new MockProcedure("Failing voltage", parsed.Document!, session));
        Assert.Equal(ExperimentStatus.Failed, result.Status);
        var assertion = Assert.IsType<ProcedureAssertionException>(result.Error);
        Assert.Equal(4, assertion.Location.Line);
    }

    /// <summary>Verify invalid syntax, disallowed load/while, and bad static arguments fail parsing.</summary>
    /// <param name="source">Invalid or disallowed Starlark procedure snippet.</param>
    [Theory]
    [InlineData("while True:\n    pass")]
    [InlineData("load('other.star', 'x')")]
    [InlineData("wait_s(-1)")]
    [InlineData("measure('Meter1', 'TEMPERATURE', 'T1')")]
    [InlineData("verify_between(r1, 10, 5, 'Ohm')")]
    [InlineData("verify_between(r1, 1, 5, 'Amp')")]
    public void RejectsUnsupportedOrInvalidSyntax(string source)
    {
        var parsed = ProcedureParser.Parse(source);
        Assert.False(parsed.Success);
        Assert.NotEmpty(parsed.Diagnostics);
        Assert.Equal(1, parsed.Diagnostics[0].Location.Line);
    }

    /// <summary>Verify semantic preflight rejects wrong modes and unknown instruments before running.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task SemanticPreflightRejectsWrongModeAndUnknownInstrument()
    {
        const string wrongMode = """
            instrument("Meter1")
            configure("Meter1", "VOLTAGE")
            r1 = measure("Meter1", "RESISTANCE", "R1")
            """;
        await using var session = Definition().StartSimulated();
        var parsed = ProcedureParser.Parse(wrongMode);
        Assert.True(parsed.Success);
        var errors = ProcedureValidator.Validate(parsed.Document!, session.RegisteredInstruments);
        Assert.Single(errors);
        Assert.Equal(3, errors[0].Location.Line);

        var unknown = ProcedureParser.Parse("instrument(\"Meter2\")");
        Assert.Contains(ProcedureValidator.Validate(unknown.Document!,
            session.RegisteredInstruments), e => e.Message.Contains("Unknown experiment instrument"));
    }

    /// <summary>Verify dimension mismatches between a measured quantity and verify_between unit fail safely.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task RuntimeRejectsMismatchedPhysicalDimension()
    {
        const string source = """
            instrument("Meter1")
            configure("Meter1", "RESISTANCE")
            r1 = measure("Meter1", "RESISTANCE", "R1")
            verify_between(r1, 1, 200, "V")
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success);
        await using var session = Definition().StartSimulated();
        var runner = new ExperimentRunner(new InMemoryExperimentResultSink(),
            NullLogger<ExperimentRunner>.Instance);
        var result = await runner.RunAsync(new MockProcedure("Unit mismatch", parsed.Document!, session));
        Assert.Equal(ExperimentStatus.Failed, result.Status);
        var error = Assert.IsType<StarlarkException>(result.Error);
        Assert.Contains("dimension", error.RawMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verify uncatchable Starlark step budgets terminate excessive loops.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task StepBudgetTerminatesExcessiveProcedureLoop()
    {
        const string source = """
            def spin():
                total = 0
                for i in range(1000):
                    total = total + i
                return total

            result = spin()
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success);
        await using var session = Definition().StartSimulated();
        var runner = new ExperimentRunner(new InMemoryExperimentResultSink(),
            NullLogger<ExperimentRunner>.Instance);
        var limits = new StarlarkProcedureLimits(MaxSteps: 25);
        var result = await runner.RunAsync(new MockProcedure("Budgeted", parsed.Document!, session, limits));
        Assert.Equal(ExperimentStatus.Failed, result.Status);
        Assert.IsType<ExecutionLimitException>(result.Error);
    }

    /// <summary>Verify cooperative cancellation halts a Starlark procedure cleanly.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task CancellationStopsProcedureExecution()
    {
        const string source = """
            def spin():
                total = 0
                for i in range(10000):
                    total = total + i
                return total

            result = spin()
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success);
        await using var session = Definition().StartSimulated();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var procedure = new MockProcedure("Cancelled", parsed.Document!, session);
        var runner = new ExperimentRunner(new InMemoryExperimentResultSink(),
            NullLogger<ExperimentRunner>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(procedure, cts.Token));
    }
}
