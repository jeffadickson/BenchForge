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

    /// <summary>Accept the parameter name length boundary.</summary>
    [Fact] public void MaxName()
    {
        var s=new DefinitionDrivenScpiMockSession(1,[Parameter(name:new string('v',128))]); Assert.Equal(128,s.GetReadingsSnapshot()[0].Parameter.Length);
    }

    /// <summary>Reject control characters in quantity identities.</summary>
    [Fact] public void ControlQuantity()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(quantity:"V\t")]));
    }

    /// <summary>Reject oversized quantity identities.</summary>
    [Fact] public void LongQuantity()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(quantity:new string('v',129))]));
    }

    /// <summary>Accept the quantity length boundary.</summary>
    [Fact] public void MaxQuantity()
    {
        var s=new DefinitionDrivenScpiMockSession(1,[Parameter(quantity:new string('v',128))]); Assert.Equal(128,s.GetReadingsSnapshot()[0].Quantity.Length);
    }

    /// <summary>Reject control characters in unit identities.</summary>
    [Fact] public void ControlUnit()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(unit:"V\0")]));
    }

    /// <summary>Reject oversized unit identities.</summary>
    [Fact] public void LongUnit()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1,[Parameter(unit:new string('v',81))]));
    }
}
