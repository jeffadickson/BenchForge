using BenchDouble.Generated;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Schema-v2 import and mode-dependent mock regression tests.</summary>
public sealed class ImportedInstrumentDefinitionTests
{
    private const string Definition = """
    {
      "schemaVersion": 2,
      "identity": {"driverName": "ExampleDmm"},
      "modes": [
        {"id":"voltage","selectionCommand":"CONF:VOLT:DC"},
        {"id":"resistance","selectionCommand":"CONF:RES"}
      ],
      "capabilities": [
        {"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read",
         "query":"MEAS:VOLT:DC?","modes":["voltage"],"mockDefault":0.25},
        {"name":"resistance","quantity":"ElectricResistance","unit":"Ohm","access":"read",
         "query":"MEAS:RES?","modes":["resistance"],"mockDefault":4700}
      ]
    }
    """;

    [Fact]
    public async Task ImportedModesControlReadings()
    {
        var definition = ImportedInstrumentDefinition.Parse(Definition);
        var mock = new ImportedInstrumentMock(definition);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await mock.MeasureAsync("voltage"));
        await mock.ConfigureModeAsync("voltage");
        Assert.Equal(0.25, await mock.MeasureAsync("voltage"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await mock.MeasureAsync("resistance"));
        await mock.ConfigureModeAsync("resistance");
        Assert.Equal(4700, await mock.MeasureAsync("resistance"));
        mock.SetReading("resistance", 1000);
        Assert.Equal(1000, await mock.MeasureAsync("resistance"));
    }

    [Fact]
    public void RejectsUnsafeCommands()
    {
        var invalid = Definition.Replace("CONF:RES", "CONF:RES\nOUTP ON");
        Assert.ThrowsAny<Exception>(() => ImportedInstrumentDefinition.Parse(invalid));
    }

    [Fact]
    public void RejectsUnknownModes()
    {
        var invalid = Definition.Replace("\"modes\":[\"voltage\"]", "\"modes\":[\"unknown\"]");
        Assert.Throws<InvalidDataException>(() => ImportedInstrumentDefinition.Parse(invalid));
    }
}
