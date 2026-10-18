using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using BenchDouble.Simulation;
using BenchDouble.Generated;
using UnitsNet;
using BenchDouble.Experiments;

namespace BenchDouble.Experiments.Tests;

/// <summary>Regression cases accumulated between ten-iteration test checkpoints.</summary>
public sealed class BatchedDevelopmentTests
{
    private const string Definition = """
    {"schemaVersion":2,"identity":{"driverName":"BatchMeter"},
     "modes":[{"id":"voltage","selectionCommand":"CONF:VOLT"}],
     "capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"MEAS:VOLT?","modes":["voltage"],"mockDefault":0.25}]}
    """;
    private static StatefulMockEngine Engine() => new(ImportedInstrumentDefinition.Parse(Definition));

    private sealed class ConstantLoad : ILoadBehavior
    {
        public ElectricCurrent CurrentAt(ElectricPotential voltage,TimeSpan elapsed)=>ElectricCurrent.FromAmperes(1);
    }
    /// <summary>Unkeyed AutoMock registration refuses an existing load dependency before evaluating rules.</summary>
    [Fact] public void AutoMockCannotSilentlyReplaceDependencies()
    {
        var builder=new ExperimentDefinitionBuilder().Model<ILoadBehavior>(_=>new ConstantLoad());
        var configured=false;
        Assert.Throws<InvalidOperationException>(()=>builder.AutoMockLoad(_=>new ConstantLoad(),rules=>configured=true));
        Assert.False(configured);
    }

// CASES
}
