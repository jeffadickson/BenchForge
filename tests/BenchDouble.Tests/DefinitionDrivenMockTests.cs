using BenchDouble.Generated;
using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>
/// Exercises generated typed driver and generated mock against the same
/// definition, without VISA, a physical instrument or a separate test DSL.
/// Requires the normal DriverGen step before building tests.
/// </summary>
public sealed class DefinitionDrivenMockTests
{
    [Fact]
    public async Task GeneratedDriverAndMockShareUnitsAndCommands()
    {
        var mock = new Keysight33522BMock();
        var driver = mock.CreateDriver();

        Assert.Equal(1000, (await driver.GetFrequencyAsync(1)).Hertz);
        Assert.Equal(1, (await driver.GetAmplitudeAsync(2)).Volts);

        await driver.SetFrequencyAsync(2, Frequency.FromKilohertz(2.5));
        await driver.SetAmplitudeAsync(1, ElectricPotential.FromVolts(3));

        Assert.Equal(2500, (await driver.GetFrequencyAsync(2)).Hertz);
        Assert.Equal(1000, (await driver.GetFrequencyAsync(1)).Hertz);
        Assert.Equal(3, (await driver.GetAmplitudeAsync(1)).Volts);
        Assert.Equal(1, (await driver.GetAmplitudeAsync(2)).Volts);
        Assert.Contains("W:SOUR2:FREQ 2500", mock.Transcript);
    }

    [Fact]
    public async Task TypedMockOverridesAreVisibleThroughNormalDriver()
    {
        var mock = new Keysight33522BMock();
        mock.SetMockFrequency(1, Frequency.FromMegahertz(1));
        mock.SetMockAmplitude(2, ElectricPotential.FromVolts(0.25));
        var driver = mock.CreateDriver();
        Assert.Equal(1_000_000, (await driver.GetFrequencyAsync(1)).Hertz);
        Assert.Equal(0.25, (await driver.GetAmplitudeAsync(2)).Volts);
    }

    [Fact]
    public async Task RejectsUnsupportedCommandsAndOutOfRangeChannels()
    {
        var mock = new Keysight33522BMock();
        var driver = mock.CreateDriver();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await driver.SetFrequencyAsync(3, Frequency.FromHertz(1000)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await driver.GetFrequencyAsync(0));
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await mock.QueryAsync("SOUR1:VOLT:OFFS?"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await mock.WriteAsync("SOUR3:FREQ 1000"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await driver.SetFrequencyAsync(1, Frequency.FromHertz(0)));
        Assert.Empty(mock.Transcript);
    }

    [Fact]
    public async Task DirectSessionRejectsNonfiniteAndInvalidScpiValues()
    {
        var mock = new Keysight33522BMock();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await mock.WriteAsync("SOUR1:FREQ NaN"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await mock.WriteAsync("SOUR1:FREQ 1000000000"));
        Assert.Equal(1000, (await mock.CreateDriver().GetFrequencyAsync(1)).Hertz);
    }

    [Fact]
    public void MockDefinitionRequiresFiniteValidDefaults()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DefinitionDrivenScpiMockSession(1, new[]
            {
                new MockScpiParameter("R", "ElectricResistance", "Ohm",
                    "MEAS:RES?", null, double.NaN, 0, 1e6)
            }));
    }
}
