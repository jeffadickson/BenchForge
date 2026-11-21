using BenchForge.Experiments;
using BenchForge.Procedures;
using BenchForge.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Starlark.Eval;
using UnitsNet;
using Xunit;

namespace BenchForge.Procedures.Tests;

/// <summary>
/// Regression tests for deterministic Starlark futures (<c>measure_async</c>, <c>all_of</c>, <c>parallel_map</c>)
/// and content-addressed <see cref="ProcedureRunFingerprint"/> reproducibility.
/// </summary>
public sealed class ParallelStarlarkTests
{
    private static ExperimentDefinition MultiMeterDefinition() =>
        new ExperimentDefinitionBuilder()
            .Simulated<ITypedConfigurableMeasurementSource>("MeterA", sp =>
                new TypedMockMeter(
                    new TypedMockMeasurementsBuilder().Constant(ElectricPotential.FromVolts(3.3)).Build(),
                    sp.GetRequiredService<ISimulationClock>()))
            .Simulated<ITypedConfigurableMeasurementSource>("MeterB", sp =>
                new TypedMockMeter(
                    new TypedMockMeasurementsBuilder().Constant(ElectricResistance.FromOhms(470.0)).Build(),
                    sp.GetRequiredService<ISimulationClock>()))
            .Build("Multi-meter parallel fixture");

    /// <summary>Verify all_of with measure_async collects readings concurrently in 100% deterministic order.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task DeterministicFuturesAcquireAndRecordInOrder()
    {
        const string source = """
            instrument("MeterA")
            instrument("MeterB")
            configure("MeterA", "VOLTAGE")
            configure("MeterB", "RESISTANCE")

            readings = all_of([
                measure_async("MeterA", "VOLTAGE"),
                measure_async("MeterB", "RESISTANCE"),
            ])
            record_sample("rail_v", readings[0])
            record_sample("load_r", readings[1])
            verify_between(readings[0], 3.2, 3.4, "V")
            verify_between(readings[1], 460.0, 480.0, "Ohm")
            """;

        var parsed = ProcedureParser.Parse(source, "parallel.star");
        Assert.True(parsed.Success, string.Join("; ", parsed.Diagnostics.Select(d => d.Message)));

        await using var session = MultiMeterDefinition().StartSimulated();
        var sink = new InMemoryExperimentResultSink();
        var runner = new ExperimentRunner(sink, NullLogger<ExperimentRunner>.Instance);
        var procedure = new MockProcedure("Parallel sweep", parsed.Document!, session);

        var run = await runner.RunAsync(procedure);

        Assert.Equal(ExperimentStatus.Completed, run.Status);
        Assert.Equal(2, sink.Samples.Count);
        Assert.Equal(3.3, sink.Samples[0].Sample.Values["rail_v"]);
        Assert.Equal("V", sink.Samples[0].Sample.Units!["rail_v"]);
        Assert.Equal(470.0, sink.Samples[1].Sample.Values["load_r"]);
        Assert.Equal("Ohm", sink.Samples[1].Sample.Units!["load_r"]);
        Assert.Equal(64, procedure.RunFingerprint.Length);
    }

    /// <summary>Verify static preflight rejects measure_async on an unconfigured instrument before execution.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task PreflightRejectsUnconfiguredMeasureAsync()
    {
        const string source = """
            instrument("MeterA")
            readings = all_of([measure_async("MeterA", "VOLTAGE")])
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success);

        await using var session = MultiMeterDefinition().StartSimulated();
        var diagnostics = ProcedureValidator.Validate(parsed.Document!, session.RegisteredInstruments);

        Assert.Single(diagnostics);
        Assert.Contains("Configure 'MeterA' for Voltage", diagnostics[0].Message);
    }

    /// <summary>Verify MaxLiveFutures enforces an uncatchable cap on excessive future fan-out.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task BoundedLiveFuturesLimitStopsRunawayFanOut()
    {
        const string source = """
            instrument("MeterA")
            configure("MeterA", "VOLTAGE")
            readings = all_of([measure_async("MeterA", "VOLTAGE") for _ in range(10)])
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success);

        await using var session = MultiMeterDefinition().StartSimulated();
        var sink = new InMemoryExperimentResultSink();
        var runner = new ExperimentRunner(sink, NullLogger<ExperimentRunner>.Instance);
        var procedure = new MockProcedure(
            "Bounded futures",
            parsed.Document!,
            session,
            new StarlarkProcedureLimits(MaxLiveFutures: 2));

        var run = await runner.RunAsync(procedure);

        Assert.Equal(ExperimentStatus.Failed, run.Status);
        Assert.IsType<ExecutionLimitException>(run.Error);
    }

    /// <summary>Verify ProcedureRunFingerprint normalizes line endings and detects bench or source changes.</summary>
    [Fact]
    public void RunFingerprintIsLineEndingInvariantAndSensitiveToInputs()
    {
        var lf = ProcedureParser.Parse("instrument(\"MeterA\")\nconfigure(\"MeterA\", \"VOLTAGE\")\n").Document!;
        var crlf = ProcedureParser.Parse("instrument(\"MeterA\")\r\nconfigure(\"MeterA\", \"VOLTAGE\")\r\n").Document!;

        var fp1 = ProcedureRunFingerprint.Compute(lf, "{\"bench\":1}", "{\"rules\":1}", "rev-a");
        var fp2 = ProcedureRunFingerprint.Compute(crlf, "{\"bench\":1}", "{\"rules\":1}", "rev-a");
        var fp3 = ProcedureRunFingerprint.Compute(lf, "{\"bench\":2}", "{\"rules\":1}", "rev-a");

        Assert.Equal(fp1, fp2);
        Assert.NotEqual(fp1, fp3);
    }
}
