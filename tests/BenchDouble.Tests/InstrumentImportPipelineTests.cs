using BenchDouble.Generated;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Bounded TPL Dataflow instrument import tests.</summary>
public sealed class InstrumentImportPipelineTests
{
    private const string Definition = """
    {
      "schemaVersion": 2,
      "identity": {"driverName":"ExampleMeter"},
      "modes":[{"id":"voltage","selectionCommand":"CONF:VOLT:DC"}],
      "capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt",
        "access":"read","query":"MEAS:VOLT:DC?","modes":["voltage"],"mockDefault":0.5}]
    }
    """;

    [Fact]
    public async Task ConcurrentValidationPreservesInputOrder()
    {
        var inputs = Enumerable.Range(0, 25)
            .Select(i => Definition.Replace("ExampleMeter", "Meter" + i)).ToArray();
        var result = await InstrumentImportPipeline.ValidateAsync(inputs, capacity: 3, parallelism: 4);
        Assert.Equal(25, result.Count);
        for (int i = 0; i < 25; i++) Assert.Equal("Meter" + i, result[i].DriverName);
    }

    [Fact]
    public async Task InvalidDefinitionFailsPipeline()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await InstrumentImportPipeline.ValidateAsync(new[] { Definition, "{}" }, capacity: 1));
    }

    [Fact]
    public async Task PreCancelledImportDoesNotProcess()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await InstrumentImportPipeline.ValidateAsync(new[] { Definition }, cancellationToken: source.Token));
    }
}
