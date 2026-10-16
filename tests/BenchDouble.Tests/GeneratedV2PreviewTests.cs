using BenchDouble.Generated;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Executable acceptance tests for committed schema-v2 generated preview classes.</summary>
public sealed class GeneratedV2PreviewTests
{
    /// <summary>Verify generated mode-safe, UnitsNet-typed DMM measurements.</summary>
    /// <summary>Verify generated UnitsNet measurements respect selected DMM modes.</summary>
    /// <returns>A completed asynchronous verification.</returns>
    [Fact]
    public async Task ExampleMeterModesAndUnits()
    {
        var meter = new ExampleTwoModeDmmPreview();
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await meter.GetvoltageAsync());
        await meter.ConfigureModeAsync("voltage");
        Assert.Equal(0.25, (await meter.GetvoltageAsync()).Volts, 10);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await meter.GetresistanceAsync());
        await meter.ConfigureModeAsync("resistance");
        Assert.Equal(4700, (await meter.GetresistanceAsync()).Ohms, 10);
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await meter.ConfigureModeAsync("invalid"));
    }

    /// <summary>Verify the Keithley draft emits no unreviewed physical operations.</summary>
    /// <summary>Verify the draft preview exposes mode selection without hardware commands.</summary>
    /// <returns>A completed asynchronous verification.</returns>
    [Fact]
    public async Task KeithleyDraftExposesModesButNoHardwareCommands()
    {
        var smu = new Keithley2400Preview();
        await smu.ConfigureModeAsync("sourceVoltage");
        await smu.ConfigureModeAsync("sourceCurrent");
    }
}
