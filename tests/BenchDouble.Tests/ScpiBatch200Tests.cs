using BenchDouble.Simulation;
using System.Collections.ObjectModel;
using Xunit;
namespace BenchDouble.Tests;

/// <summary>SCPI metadata, ambiguity, cancellation and snapshot regressions for batch 200.</summary>
public sealed class ScpiBatch200Tests
{
    private static MockScpiParameter Parameter(string name="voltage", string quantity="ElectricPotential", string unit="Volt", string? query="V{channel}?", string? setter="V{channel} {value}", double initial=1, double? minimum=0, double? maximum=10) => new(name,quantity,unit,query,setter,initial,minimum,maximum);
    private static DefinitionDrivenScpiMockSession Session() => new(2,[Parameter()]);

    /// <summary>Reject control characters in parameter names.</summary>
    [Fact] public void ControlName()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(name:"v\n")]));
    }

    /// <summary>Reject oversized parameter names.</summary>
    [Fact] public void LongName()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(name:new string('v',129))]));
    }
}
