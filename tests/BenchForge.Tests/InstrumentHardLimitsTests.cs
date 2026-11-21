using BenchForge.Async;
using BenchForge.Client;
using BenchForge.Contracts;
using BenchForge.Flight;
using BenchForge.Generated;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>
/// Comprehensive tests for registered final fail-safe <see cref="InstrumentHardLimits"/>,
/// monotonic tightening (<see cref="HardLimitRegistry"/>), pre-dispatch <see cref="HardLimitScpiGuard"/>,
/// SCPI hardware protection programming (<see cref="ScpiHardwareLimitProgrammer"/>), and symmetric wrappers.
/// </summary>
public sealed class InstrumentHardLimitsTests
{
    /// <summary>Verify registered limits can only be tightened and can never be relaxed or removed once registered.</summary>
    [Fact]
    public void HardLimitRegistryEnforcesMonotonicTighteningAndRejectsRelaxation()
    {
        var registry = new HardLimitRegistry();

        var initial = registry.TryRegister("psu", new InstrumentHardLimits(
            MaxVoltage: ElectricPotential.FromVolts(28.0),
            MaxCurrent: ElectricCurrent.FromAmperes(5.0),
            MaxPower: Power.FromWatts(100m),
            PolicyNote: "Flight EPS bus ICD-2026"));
        Assert.True(initial.IsSuccess);

        // Tightening succeeds
        var tightened = registry.TryRegister("psu", new InstrumentHardLimits(
            MaxVoltage: ElectricPotential.FromVolts(24.0),
            MaxCurrent: ElectricCurrent.FromAmperes(4.0),
            MaxPower: Power.FromWatts(80m)));
        Assert.True(tightened.IsSuccess);
        Assert.Equal(24.0, tightened.Value.MaxVoltage!.Value.Volts, 6);
        Assert.Equal(4.0, tightened.Value.MaxCurrent!.Value.Amperes, 6);

        // Raising MaxVoltage is forbidden
        var raiseVolt = registry.TryRegister("psu", new InstrumentHardLimits(
            MaxVoltage: ElectricPotential.FromVolts(30.0),
            MaxCurrent: ElectricCurrent.FromAmperes(4.0),
            MaxPower: Power.FromWatts(80m)));
        Assert.True(raiseVolt.IsFailure);
        Assert.Equal(BenchErrorKind.Forbidden, raiseVolt.Error.Kind);
        Assert.Equal("BF-HARDLIMIT-RELAX-DENIED", raiseVolt.Error.Code);

        // Removing MaxPower (setting null) is also forbidden
        var dropPower = registry.TryRegister("psu", new InstrumentHardLimits(
            MaxVoltage: ElectricPotential.FromVolts(24.0),
            MaxCurrent: ElectricCurrent.FromAmperes(4.0),
            MaxPower: null));
        Assert.True(dropPower.IsFailure);
        Assert.Equal("BF-HARDLIMIT-RELAX-DENIED", dropPower.Error.Code);

        // Existing tighter limits remain untouched
        Assert.Equal(24.0, registry.TryGet("psu")!.MaxVoltage!.Value.Volts, 6);
        Assert.Equal(80m, registry.TryGet("psu")!.MaxPower!.Value.Watts);
    }

    /// <summary>Verify registered hard maximums protect every output-capable symmetric instrument class and support live runtime tightening.</summary>
    /// <returns>Completion of all symmetric hard-limit assertions.</returns>
    [Fact]
    public async Task AllOutputCapableInstrumentClassesEnforceRegisteredHardLimitsAndRuntimeTightening()
    {
        await using var bench = BenchSession.Create("flight-hard-limits-bench")
            .AddMockSmu("psu", loadResistanceOhms: 10.0)
            .WithHardLimits("psu", new InstrumentHardLimits(
                MaxVoltage: ElectricPotential.FromVolts(28.0),
                MaxCurrent: ElectricCurrent.FromAmperes(3.0),
                MaxPower: Power.FromWatts(60m),
                RequireComplianceBeforeOutputOn: true,
                AutoSafeOnViolation: true))
            .AddMockThermal("thermal")
            .WithHardLimits("thermal", new InstrumentHardLimits(
                MinTemperature: Temperature.FromDegreesCelsius(-40.0),
                MaxTemperature: Temperature.FromDegreesCelsius(85.0)))
            .AddMockFunctionGenerator("fgen")
            .WithHardLimits("fgen", new InstrumentHardLimits(
                MinFrequency: Frequency.FromHertz(10.0),
                MaxFrequency: Frequency.FromMegahertz(20.0),
                MaxVoltage: ElectricPotential.FromVolts(3.3)))
            .AddMockElectronicLoad("eload")
            .WithHardLimits("eload", new InstrumentHardLimits(
                MaxCurrent: ElectricCurrent.FromAmperes(10.0),
                MinResistance: ElectricResistance.FromOhms(2.0)))
            .AddMockRfSignalGenerator("rfsg")
            .WithHardLimits("rfsg", new InstrumentHardLimits(
                MinFrequency: Frequency.FromGigahertz(2.0),
                MaxFrequency: Frequency.FromGigahertz(2.5),
                MaxPower: Power.FromMilliwatts(10m)))
            .AddMockLcrMeter("lcr")
            .WithHardLimits("lcr", new InstrumentHardLimits(
                MaxFrequency: Frequency.FromKilohertz(100.0),
                MaxVoltage: ElectricPotential.FromVolts(1.0)))
            .Build();

        Assert.True(bench.HardLimits.IsSealed);
        Assert.Contains("\"hardLimits\"", bench.DescribeJson(), StringComparison.Ordinal);

        // PSU: compliance required before output on, voltage & power ceilings enforced, AutoSafeOnViolation disables output
        var noComp = await bench.PowerSupply("psu").SetOutputEnabledAsync(true);
        Assert.True(noComp.IsFailure);
        Assert.Equal("BF-HARDLIMIT-NO-COMPLIANCE", noComp.Error.Code);

        Assert.True((await bench.PowerSupply("psu").SetCurrentLimitAsync(ElectricCurrent.FromAmperes(2.5))).IsSuccess);
        Assert.True((await bench.PowerSupply("psu").SetVoltageAsync(ElectricPotential.FromVolts(20.0))).IsSuccess);
        Assert.True((await bench.PowerSupply("psu").SetOutputEnabledAsync(true)).IsSuccess);

        // 25 V * 2.5 A = 62.5 W > 60 W ceiling -> rejected AND AutoSafeOnViolation turns output OFF!
        var powerViolation = await bench.PowerSupply("psu").SetVoltageAsync(ElectricPotential.FromVolts(25.0));
        Assert.True(powerViolation.IsFailure);
        Assert.Equal("BF-HARDLIMIT-POWER", powerViolation.Error.Code);
        Assert.False((await bench.PowerSupply("psu").GetOutputEnabledAsync()).Value);

        // Runtime tightening immediately narrows live proxy without rebuilding session
        var tighten = bench.TryTightenHardLimits("psu", new InstrumentHardLimits(MaxVoltage: ElectricPotential.FromVolts(12.0)));
        Assert.True(tighten.IsSuccess);
        var afterTighten = await bench.PowerSupply("psu").SetVoltageAsync(ElectricPotential.FromVolts(15.0));
        Assert.True(afterTighten.IsFailure);
        Assert.Equal("BF-HARDLIMIT-VOLT", afterTighten.Error.Code);

        // Thermal chamber: blocks baking (> 85 C) or freezing (< -40 C)
        Assert.True((await bench.Thermal("thermal").SetSetpointAsync(Temperature.FromDegreesCelsius(110.0))).IsFailure);
        Assert.True((await bench.Thermal("thermal").SetSetpointAsync(Temperature.FromDegreesCelsius(-55.0))).IsFailure);
        Assert.True((await bench.Thermal("thermal").SetSetpointAsync(Temperature.FromDegreesCelsius(60.0))).IsSuccess);

        // Function generator: blocks over-amplitude (> 3.3 Vpp) and out-of-band frequency
        Assert.True((await bench.FunctionGenerator("fgen").SetAmplitudeAsync(ElectricPotential.FromVolts(5.0))).IsFailure);
        Assert.True((await bench.FunctionGenerator("fgen").SetFrequencyAsync(Frequency.FromMegahertz(50.0))).IsFailure);
        Assert.True((await bench.FunctionGenerator("fgen").SetAmplitudeAsync(ElectricPotential.FromVolts(3.0))).IsSuccess);

        // Electronic load: blocks bus short-circuit (< 2 Ohm) and excess sink current (> 10 A)
        Assert.True((await bench.ElectronicLoad("eload").SetResistanceLoadAsync(ElectricResistance.FromOhms(0.5))).IsFailure);
        Assert.True((await bench.ElectronicLoad("eload").SetCurrentSinkAsync(ElectricCurrent.FromAmperes(15.0))).IsFailure);
        Assert.True((await bench.ElectronicLoad("eload").SetResistanceLoadAsync(ElectricResistance.FromOhms(10.0))).IsSuccess);

        // RF signal generator: blocks out-of-band frequency and excess RF power (> 10 mW / +10 dBm)
        Assert.True((await bench.RfSignalGenerator("rfsg").SetOutputPowerAsync(Power.FromMilliwatts(100m))).IsFailure);
        Assert.True((await bench.RfSignalGenerator("rfsg").SetFrequencyAsync(Frequency.FromGigahertz(5.8))).IsFailure);
        Assert.True((await bench.RfSignalGenerator("rfsg").SetOutputPowerAsync(Power.FromMilliwatts(5m))).IsSuccess);

        // LCR meter: blocks excess AC stimulus voltage (> 1.0 V)
        Assert.True((await bench.LcrMeter("lcr").SetTestVoltageAsync(ElectricPotential.FromVolts(2.0))).IsFailure);
        Assert.True((await bench.LcrMeter("lcr").SetTestVoltageAsync(ElectricPotential.FromVolts(0.5))).IsSuccess);
    }

    /// <summary>Verify RequireHardLimitsForPhysical fails closed when a physical output-capable instrument lacks registered maximums.</summary>
    [Fact]
    public void RequireHardLimitsForPhysicalFailsClosedWithoutRegisteredLimits()
    {
        var mockSession = new KeysightN8921AMock();
        var physicalAdapter = mockSession.CreateDriver().AsBenchPowerSupply("flight-psu", InstrumentBackend.Physical);

        var ex = Assert.Throws<BenchOperationException>(() =>
            BenchSession.Create("flight-strict")
                .AddInstrument(physicalAdapter)
                .RequireHardLimitsForPhysical()
                .Build());

        Assert.Equal("BF-HARDLIMIT-UNREGISTERED", ex.Error.Code);
    }

    /// <summary>Verify HardLimitScpiGuard blocks raw and generated SCPI writes exceeding registered ceilings before any byte reaches the transport.</summary>
    /// <returns>Completion of SCPI guard assertions.</returns>
    [Fact]
    public async Task HardLimitScpiGuardBlocksOverLimitCommandsBeforeWireDispatch()
    {
        var rawTransport = new RecordingScpiSession();
        var registry = new HardLimitRegistry();
        Assert.True(registry.TryRegister("psu", new InstrumentHardLimits(
            MaxVoltage: ElectricPotential.FromVolts(28.0),
            MaxCurrent: ElectricCurrent.FromAmperes(5.0),
            MaxPower: Power.FromWatts(100m),
            MaxTemperature: Temperature.FromDegreesCelsius(85.0),
            MinResistance: ElectricResistance.FromOhms(4.0),
            HardwareProtectionMarginFactor: 1.05)).IsSuccess);
        registry.Seal();

        var guarded = new GuardedScpiSession("psu", rawTransport, registry.ToScpiGuard(), isPhysicalHardware: true);

        // Allowed commands reach transport
        await guarded.WriteAsync("SOUR:VOLT 28.0");
        await guarded.WriteAsync("SOUR:VOLT:PROT 29.4"); // 28 * 1.05 = 29.4 allowed
        Assert.Equal(2, rawTransport.Writes.Count);

        // Over-limit commands are rejected BEFORE hitting rawTransport
        await Assert.ThrowsAsync<OperationGuardDeniedException>(async () => await guarded.WriteAsync("SOUR:VOLT 32.0"));
        await Assert.ThrowsAsync<OperationGuardDeniedException>(async () => await guarded.WriteAsync("SOUR:CURR 8.5"));
        await Assert.ThrowsAsync<OperationGuardDeniedException>(async () => await guarded.WriteAsync("SOUR:POW:LEV:IMM:AMPL 150.0"));
        await Assert.ThrowsAsync<OperationGuardDeniedException>(async () => await guarded.WriteAsync("SETP 1 120.0"));
        await Assert.ThrowsAsync<OperationGuardDeniedException>(async () => await guarded.WriteAsync("CHAN1:RES 0.5"));
        await Assert.ThrowsAsync<OperationGuardDeniedException>(async () => await guarded.WriteAsync("SOUR:VOLT:PROT 50.0"));

        // Zero blocked bytes ever touched the underlying transport
        Assert.Equal(2, rawTransport.Writes.Count);
    }

    /// <summary>Verify ScpiHardwareLimitProgrammer programs OVP/OCP/OPP registers on physical SCPI sessions and reports mock non-support honestly.</summary>
    /// <returns>Completion of hardware protection programming assertions.</returns>
    [Fact]
    public async Task ProgramAllHardwareLimitsProgramsScpiProtectionAndReportsHonestly()
    {
        var physicalTransport = new RecordingScpiSession();
        var physicalDriver = new KeysightN8921A(physicalTransport);
        var mockDriver = new KeysightN8921AMock().CreateDriver();

        var limits = new InstrumentHardLimits(
            MaxVoltage: ElectricPotential.FromVolts(28.0),
            MaxCurrent: ElectricCurrent.FromAmperes(5.0),
            MaxPower: Power.FromWatts(120m),
            HardwareProtectionMarginFactor: 1.05);

        await using var bench = BenchSession.Create("hw-prot-bench")
            .AddInstrument(physicalDriver.AsBenchPowerSupply("phys-psu", InstrumentBackend.Physical))
            .WithHardLimits("phys-psu", limits)
            .AddInstrument(mockDriver.AsBenchPowerSupply("sim-psu", InstrumentBackend.Mock))
            .WithHardLimits("sim-psu", limits)
            .Build();

        var reports = await bench.ProgramAllHardwareLimitsAsync();
        Assert.True(reports.IsSuccess);
        Assert.Equal(2, reports.Value.Count);

        var physReport = reports.Value.Single(r => r.Role == "phys-psu");
        Assert.True(physReport.HardwareProtectionSupported);
        Assert.True(physReport.OvpProgrammed);
        Assert.True(physReport.OcpProgrammed);
        Assert.True(physReport.OppProgrammed);
        Assert.False(physReport.HardwareProtectionVerifiedOnKillTest); // Honest per BD-RES-002
        Assert.Contains(physicalTransport.Writes, w => w.StartsWith("SOUR:VOLT:PROT 29.4", StringComparison.Ordinal));
        Assert.Contains(physicalTransport.Writes, w => w.StartsWith("SOUR:CURR:PROT 5.25", StringComparison.Ordinal));
        Assert.Contains(physicalTransport.Writes, w => w.StartsWith("SOUR:POW:PROT 126", StringComparison.Ordinal));

        var simReport = reports.Value.Single(r => r.Role == "sim-psu");
        Assert.False(simReport.HardwareProtectionSupported);
        Assert.False(simReport.HardwareProtectionVerifiedOnKillTest);
    }

    private sealed class RecordingScpiSession : IAsyncScpiSession
    {
        public List<string> Writes { get; } = new();
        public ValueTask WriteAsync(string command, CancellationToken cancellationToken = default)
        {
            Writes.Add(command);
            return ValueTask.CompletedTask;
        }
        public ValueTask<string> QueryAsync(string command, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult("0");
    }
}
