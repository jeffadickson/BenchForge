using BenchDouble.Experiments;
using BenchDouble.Procedures;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UnitsNet;

string path = args.Length == 0
    ? "examples/BenchDouble.ProcedureDemo/resistance-test.bench"
    : args[0];
var source = await File.ReadAllTextAsync(path);
var parsed = ProcedureParser.Parse(source);
if (!parsed.Success)
{
    foreach (var error in parsed.Diagnostics)
        Console.Error.WriteLine($"Line {error.Location.Line}: {error.Message}");
    Environment.ExitCode = 2;
    return;
}

var definition = new ExperimentDefinitionBuilder()
    .Model(_ => new TypedMockMeasurementsBuilder()
        .Register<ElectricResistance>(elapsed =>
            ElectricResistance.FromOhms(100 + elapsed.TotalSeconds))
        .Constant(ElectricPotential.FromVolts(12))
        .Build())
    .Simulated<ITypedConfigurableMeasurementSource>("Meter1", sp =>
        new TypedMockMeter(sp.GetRequiredService<TypedMockMeasurements>(),
            sp.GetRequiredService<ISimulationClock>()))
    .Build("Procedure demonstration");

await using var session = definition.StartSimulated();
var sink = new InMemoryExperimentResultSink();
var runner = new ExperimentRunner(sink, NullLogger<ExperimentRunner>.Instance);
var procedure = new MockProcedure("Resistance test", parsed.Document, session);
var result = await runner.RunAsync(procedure);
foreach (var item in sink.Samples)
{
    Console.WriteLine($"Run {item.Id}: " +
        string.Join(", ", item.Sample.Values.Select(v =>
            $"{v.Key}={v.Value:G6} {item.Sample.Units?.GetValueOrDefault(v.Key)}")));
}
Console.WriteLine($"Status: {result.Status}; simulated time: {session.Clock.Elapsed}");
Console.WriteLine("No physical instrument or network access. No real waiting.");
if (result.Status != ExperimentStatus.Completed) Environment.ExitCode = 1;
