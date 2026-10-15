using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class InstrumentDefinitionMockLoaderTests
{
    private const string TwoModeDefinition = """
        {
          "schemaVersion": 2,
          "modes": [{"id":"voltage"},{"id":"resistance"}],
          "capabilities": [
            {"name":"voltage","access":"read","quantity":"ElectricPotential",
             "unit":"Volt","modes":["voltage"],"mockDefault":0.25},
            {"name":"resistance","access":"read","quantity":"ElectricResistance",
             "unit":"Ohm","modes":["resistance"],"mockDefault":4700}
          ]
        }
        """;

    [Fact]
    public async Task SchemaV2AutomaticallyRegistersModeSpecificUnitsNetDefaults()
    {
        var meter = InstrumentDefinitionMockLoader.CreatePreview(
            TwoModeDefinition, new ManualSimulationClock());
        await meter.ConfigureModeAsync("resistance");
        Assert.Equal(4700, (await meter.MeasureAsync<ElectricResistance>()).Ohms);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await meter.MeasureAsync<ElectricPotential>());
        await meter.ConfigureModeAsync("voltage");
        Assert.Equal(0.25, (await meter.MeasureAsync<ElectricPotential>()).Volts, 10);
    }

    [Fact]
    public async Task MissingDefaultIsNotInvented()
    {
        const string definition = """
            {"schemaVersion":2,"modes":[{"id":"voltage"}],
             "capabilities":[{"name":"voltage","access":"read",
               "quantity":"ElectricPotential","unit":"Volt","modes":["voltage"],
               "mockDefault":null}]}
            """;
        var meter = InstrumentDefinitionMockLoader.CreatePreview(
            definition, new ManualSimulationClock());
        await meter.ConfigureModeAsync("voltage");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await meter.MeasureAsync<ElectricPotential>());
    }

    [Fact]
    public void RejectsUnsupportedQuantityAndOutOfRangeDefault()
    {
        const string wrongDimension = """
            {"schemaVersion":2,"modes":[{"id":"voltage"}],
             "capabilities":[{"name":"voltage","access":"read",
               "quantity":"ElectricResistance","unit":"Volt","modes":["voltage"],
               "mockDefault":1}]}
            """;
        Assert.Throws<NotSupportedException>(() =>
            InstrumentDefinitionMockLoader.CreatePreview(
                wrongDimension, new ManualSimulationClock()));

        const string outsideRange = """
            {"schemaVersion":2,"modes":[{"id":"voltage"}],
             "capabilities":[{"name":"voltage","access":"read",
               "quantity":"ElectricPotential","unit":"Volt","modes":["voltage"],
               "mockDefault":10,"minimum":0,"maximum":5}]}
            """;
        Assert.Throws<FormatException>(() =>
            InstrumentDefinitionMockLoader.CreatePreview(
                outsideRange, new ManualSimulationClock()));
    }

    [Fact]
    public void RejectsUnknownSchemaVersion()
    {
        Assert.Throws<NotSupportedException>(() =>
            InstrumentDefinitionMockLoader.CreatePreview(
                """{"schemaVersion":1,"modes":[],"capabilities":[]}""",
                new ManualSimulationClock()));
    }
}
