using BenchDouble.Experiments;
using BenchDouble.Procedures;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UnitsNet;
using Xunit;

namespace BenchDouble.Procedures.Tests;

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

    [Fact]
    public async Task ParlotProcedureExecutesAgainstRealTypedMockAndVirtualClock()
    {
        const string source = """
            # A simple simulated test procedure
            INSTRUMENT Meter1
            CONFIGURE Meter1 RESISTANCE
            MEASURE Meter1 RESISTANCE AS R1
            VERIFY R1 BETWEEN 95 Ohm AND 105 Ohm
            WAIT 5 s
            MEASURE Meter1 RESISTANCE AS R2
            VERIFY R2 BETWEEN 100 Ohm AND 110 Ohm
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success, string.Join("; ", parsed.Diagnostics.Select(x => x.Message)));
        await using var session = Definition().StartSimulated();
        var sink = new InMemoryExperimentResultSink();
        var runner = new ExperimentRunner(sink, NullLogger<ExperimentRunner>.Instance);
        var result = await runner.RunAsync(new MockProcedure("Resistance test", parsed.Document, session));
        Assert.Equal(ExperimentStatus.Completed, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(5), session.Clock.Elapsed);
        Assert.Equal(2, sink.Samples.Count);
        Assert.Equal(100, sink.Samples[0].Sample.Values["R1"]);
        Assert.Equal(105, sink.Samples[1].Sample.Values["R2"]);
        Assert.Equal("Ohm", sink.Samples[1].Sample.Units!["R2"]);
    }

    [Fact]
    public async Task FailedLimitProducesFailedRunAndSourceLine()
    {
        const string source = """
            INSTRUMENT Meter1
            CONFIGURE Meter1 VOLTAGE
            MEASURE Meter1 VOLTAGE AS V1
            VERIFY V1 BETWEEN 0 V AND 5 V
            """;
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success);
        await using var session = Definition().StartSimulated();
        var runner = new ExperimentRunner(new InMemoryExperimentResultSink(),
            NullLogger<ExperimentRunner>.Instance);
        var result = await runner.RunAsync(new MockProcedure("Failing voltage", parsed.Document, session));
        Assert.Equal(ExperimentStatus.Failed, result.Status);
        var assertion = Assert.IsType<ProcedureAssertionException>(result.Error);
        Assert.Equal(4, assertion.Location.Line);
    }

    [Theory]
    [InlineData("SET Meter1.Voltage = 12 V")]
    [InlineData("WAIT -1 s")]
    [InlineData("WAIT 2 min")]
    [InlineData("VERIFY R1 BETWEEN 5 V AND 10 Ohm")]
    [InlineData("MEASURE Meter1 TEMPERATURE AS T1")]
    public void RejectsUnsupportedOrInvalidSyntax(string source)
    {
        var parsed = ProcedureParser.Parse(source);
        Assert.False(parsed.Success);
        Assert.Single(parsed.Diagnostics);
        Assert.Equal(1, parsed.Diagnostics[0].Location.Line);
    }

    [Fact]
    public async Task SemanticPreflightRejectsWrongModeAndUnknownInstrument()
    {
        const string wrongMode = """
            INSTRUMENT Meter1
            CONFIGURE Meter1 VOLTAGE
            MEASURE Meter1 RESISTANCE AS R1
            """;
        await using var session = Definition().StartSimulated();
        var parsed = ProcedureParser.Parse(wrongMode);
        Assert.True(parsed.Success);
        var errors = ProcedureValidator.Validate(parsed.Document, session.RegisteredInstruments);
        Assert.Single(errors);
        Assert.Equal(3, errors[0].Location.Line);
        var unknown = ProcedureParser.Parse("INSTRUMENT Meter2");
        Assert.Contains(ProcedureValidator.Validate(unknown.Document,
            session.RegisteredInstruments), e => e.Message.Contains("Unknown experiment instrument"));
    }

    [Fact]
    public async Task SemanticPreflightRejectsWrongQuantityAndUndefinedVariable()
    {
        const string source = """
            INSTRUMENT Meter1
            CONFIGURE Meter1 RESISTANCE
            MEASURE Meter1 RESISTANCE AS R1
            VERIFY R1 BETWEEN 1 V AND 2 V
            VERIFY R2 BETWEEN 1 Ohm AND 2 Ohm
            """;
        await using var session = Definition().StartSimulated();
        var parsed = ProcedureParser.Parse(source);
        Assert.True(parsed.Success);
        var errors = ProcedureValidator.Validate(parsed.Document, session.RegisteredInstruments);
        Assert.Equal(2, errors.Count);
    }
}
