using BenchDouble.Generated;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Generated mock-decorator behavior and provenance regression tests.</summary>
public sealed class GeneratedV2MockDecoratorTests
{
    /// <summary>Injected readings are typed and cannot bypass mode validation.</summary>
    /// <summary>Verify injected typed readings cannot bypass operating-mode validation.</summary>
    /// <returns>A completed asynchronous verification.</returns>
    [Fact]
    public async Task DecoratorUsesInjectedReadingsOnlyInValidMode()
    {
        var preview = new ExampleTwoModeDmmPreview();
        var mock = new ExampleTwoModeDmmMockDecorator(preview);
        mock.SetvoltageBehavior(_ => ValueTask.FromResult(ElectricPotential.FromMillivolts(750)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await mock.GetvoltageAsync());
        await mock.ConfigureModeAsync("voltage");
        Assert.Equal(0.75, (await mock.GetvoltageAsync()).Volts, 10);
        mock.SetvoltageBehavior(null);
        mock.SetMockvoltage(ElectricPotential.FromVolts(1.25));
        Assert.Equal(1.25, (await mock.GetvoltageAsync()).Volts, 10);
        mock.ResetMockvoltage();
        Assert.Equal(0.25, (await mock.GetvoltageAsync()).Volts, 10);
        await mock.ConfigureModeAsync("resistance");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await mock.GetvoltageAsync());
    }

    /// <summary>Definition hash and missing upstream revision are explicitly recorded.</summary>
    [Fact]
    public void BuildMetadataContainsDefinitionDigest()
    {
        Assert.Equal(64, ExampleTwoModeDmmPreview.DefinitionSha256.Length);
        Assert.Equal("v2-stateful-2", ExampleTwoModeDmmPreview.GeneratorVersion);
    }
}
