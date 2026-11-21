using System.Text.Json;
using BenchForge.Client;
using BenchForge.Contracts;
using BenchForge.Flight;
using BenchForge.Interfaces;
using BenchForge.Outcomes;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>
/// Regression tests for railroad-oriented BenchResult&lt;T&gt;, symmetric instrument/mock interfaces,
/// flight-hardware safe envelopes, monotonic voltage ramps, calibration preflight, sealed evidence,
/// and the BenchSession client SDK.
/// </summary>
public sealed class FlightClientAndRailroadTests
{
    /// <summary>Verify BenchResult&lt;T&gt; railroad combinators, wire round-trip, and CommandReceipt translation.</summary>
    [Fact]
    public void RailroadResultCombinatorsWireDtoAndReceiptMappingWorkDeterministically()
    {
        var ok = BenchResult<double>.Ok(3.30)
            .Map(v => v * 1000.0)
            .Ensure(
                mv => mv is >= 3200 and <= 3400,
                mv => BenchError.Validation("BF-LIMIT", $"Voltage {mv} mV out of range."));
        Assert.True(ok.IsSuccess);
        Assert.Equal(3300.0, ok.Value, precision: 6);

        var wireJson = JsonSerializer.Serialize(ok.ToWire());
        var roundTrip = JsonSerializer.Deserialize<BenchWireResult<double>>(wireJson)!.ToResult();
        Assert.True(roundTrip.IsSuccess);
        Assert.Equal(3300.0, roundTrip.Value, precision: 6);

        var uncertainReceipt = new CommandReceipt(
            Guid.NewGuid(),
            "corr-flight-99",
            "SMU1",
            new LeaseFence(Guid.NewGuid(), 2, 5),
            "DIGEST",
            CommandExecutionState.Uncertain,
            CommandFaultKind.TimeoutFault,
            1,
            null,
            "Timeout after wire dispatch.",
            DateTimeOffset.UtcNow);

        var mapped = BenchError.FromReceipt(uncertainReceipt);
        Assert.Equal(BenchErrorKind.Timeout, mapped.Kind);
        Assert.Equal(PhysicalStateConfidence.UnknownRequiresRecovery, mapped.Confidence);
        Assert.True(mapped.RequiresRecovery);
        Assert.False(mapped.IsRetryable);
        Assert.Equal("corr-flight-99", mapped.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(mapped.Remedy));
    }

    /// <summary>Verify FlightEnvelopedSmuProxy enforces compliance-first, voltage, current, and power ceilings before dispatch.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task FlightSafeEnvelopeBlocksUnsafeActuationBeforeHardware()
    {
        var envelope = new FlightSafeEnvelope(
            MaxVoltage: ElectricPotential.FromVolts(5.5),
            MinVoltage: ElectricPotential.FromVolts(0.0),
            MaxCurrent: ElectricCurrent.FromAmperes(0.5),
            MaxPower: Power.FromWatts(2.0),
            RequireComplianceBeforeOutputOn: true);

        await using var bench = BenchSession.Create("FlightEnvelopeCheck")
            .WithFlightEnvelope("SMU1", envelope)
            .AddMockSmu("SMU1", loadResistanceOhms: 10.0)
            .Build();

        var smu = bench.Smu("SMU1");

        // 1. Output ON blocked before explicit compliance limit is set.
        var enableTooSoon = await smu.SetOutputEnabledAsync(true);
        Assert.True(enableTooSoon.IsFailure);
        Assert.Equal(BenchErrorKind.EnvelopeViolation, enableTooSoon.Error.Kind);
        Assert.Equal("BF-ENVELOPE-COMPLIANCE-REQUIRED", enableTooSoon.Error.Code);

        // 2. Over-current limit rejected.
        var overCurrent = await smu.SetCurrentLimitAsync(ElectricCurrent.FromAmperes(0.8));
        Assert.True(overCurrent.IsFailure);
        Assert.Equal("BF-ENVELOPE-CURR", overCurrent.Error.Code);

        // 3. Valid compliance set, then over-voltage and over-power rejected.
        Assert.True((await smu.SetCurrentLimitAsync(ElectricCurrent.FromAmperes(0.5))).IsSuccess);

        var overVolt = await smu.SetVoltageAsync(ElectricPotential.FromVolts(6.0));
        Assert.True(overVolt.IsFailure);
        Assert.Equal("BF-ENVELOPE-VOLT", overVolt.Error.Code);

        var reverseBias = await smu.SetVoltageAsync(ElectricPotential.FromVolts(-0.5));
        Assert.True(reverseBias.IsFailure);
        Assert.Equal("BF-ENVELOPE-VOLT", reverseBias.Error.Code);

        // 5.0 V * 0.5 A = 2.5 W > 2.0 W MaxPower!
        var overPower = await smu.SetVoltageAsync(ElectricPotential.FromVolts(5.0));
        Assert.True(overPower.IsFailure);
        Assert.Equal("BF-ENVELOPE-POWER", overPower.Error.Code);

        // 3.3 V * 0.5 A = 1.65 W <= 2.0 W -> Allowed!
        Assert.True((await smu.SetVoltageAsync(ElectricPotential.FromVolts(3.3))).IsSuccess);
        Assert.True((await smu.SetOutputEnabledAsync(true)).IsSuccess);
    }

    /// <summary>Verify SafeVoltageRampController steps monotonically and automatically shuts down on short-circuit compliance trip.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task SafeVoltageRampSucceedsOnNominalLoadAndAbortsSafelyOnComplianceTrip()
    {
        var nominalMock = new StatefulMockSourceMeasureUnit("SMU1", loadResistanceOhms: 20.0);
        await using var bench = BenchSession.Create("FlightRampTest")
            .WithFlightEnvelope("SMU1", new FlightSafeEnvelope(ElectricPotential.FromVolts(5.5), MaxCurrent: ElectricCurrent.FromAmperes(0.5)))
            .AddInstrument(nominalMock)
            .Build();

        var ramp = await bench.RampVoltageSafelyAsync(
            "SMU1",
            ElectricPotential.FromVolts(3.3),
            ElectricCurrent.FromAmperes(0.3),
            ElectricPotential.FromVolts(1.1));

        Assert.True(ramp.IsSuccess);
        Assert.Equal(3, ramp.Value.Count);
        Assert.Equal(3.3, ramp.Value[^1].MeasuredVoltage.Volts, precision: 6);
        Assert.Equal(0.165, ramp.Value[^1].MeasuredCurrent.Amperes, precision: 6);

        // Inject a short-circuit load (1 Ohm -> 3.3 A > 0.3 A compliance!) -> ramp MUST trip and disable output.
        nominalMock.SetSimulatedLoadResistance(1.0);
        await bench.Smu("SMU1").SafeStateAsync();

        var shorted = await bench.RampVoltageSafelyAsync(
            "SMU1",
            ElectricPotential.FromVolts(3.3),
            ElectricCurrent.FromAmperes(0.3),
            ElectricPotential.FromVolts(1.1));

        Assert.True(shorted.IsFailure);
        Assert.Equal(BenchErrorKind.ComplianceTripped, shorted.Error.Kind);
        Assert.True(shorted.Error.RequiresRecovery);

        var outputAfterAbort = await bench.Smu("SMU1").GetOutputEnabledAsync();
        Assert.True(outputAfterAbort.IsSuccess);
        Assert.False(outputAfterAbort.Value);
    }

    /// <summary>Verify calibration preflight, agent-readable DescribeJson, mock fault injection, and tamper-evident FlightEvidencePackage.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task CalibrationPreflightAgentManifestFaultInjectionAndSealedEvidenceWorkEndToEnd()
    {
        var expiredCal = new InstrumentCalibrationInfo("CAL-OLD-01", DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddDays(-1));
        await using var expiredBench = BenchSession.Create("ExpiredCalBench")
            .AddMockDmm("DMM1", 3.3, 100.0, expiredCal)
            .Build();

        var calCheck = expiredBench.PreflightCalibration();
        Assert.True(calCheck.IsFailure);
        Assert.Equal(BenchErrorKind.CalibrationExpired, calCheck.Error.Kind);

        var validCal = new InstrumentCalibrationInfo("CAL-2026-99", DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(11));
        await using var flightBench = BenchSession.Create("AvionicsQualBench")
            .AddMockDmm("DMM1", 3.302, 100.0, validCal)
            .AddMockThermal("CHAMBER1", 25.0, validCal)
            .Build();

        Assert.True(flightBench.PreflightCalibration().IsSuccess);

        var manifestJson = flightBench.DescribeJson();
        Assert.Contains("AvionicsQualBench", manifestJson);
        Assert.Contains("CAL-2026-99", manifestJson);
        Assert.Contains("MeasureDcVoltage", manifestJson);

        // Inject one-shot fault via IBenchMockControl without leaking mock APIs onto IBenchDigitalMultimeter.
        var mockCtl = flightBench.TryGetMockControl("DMM1").GetValueOrThrow();
        mockCtl.InjectNextFault(new BenchError("BF-SIM-DROP", BenchErrorKind.TransportFault, "Injected bus glitch.", "DMM1"));
        var faultedRead = await flightBench.Dmm("DMM1").MeasureDcVoltageAsync();
        Assert.True(faultedRead.IsFailure);
        Assert.Equal("BF-SIM-DROP", faultedRead.Error.Code);

        // Next read recovers cleanly.
        var goodRead = await flightBench.Dmm("DMM1").MeasureDcVoltageAsync();
        Assert.True(goodRead.IsSuccess);
        flightBench.RecordStep("CheckRail3V3", true, $"Measured {goodRead.Value.Volts:F4} V");

        var evidence = flightBench.SealEvidence("FLIGHT-SN-0042", "agent-qual-01", "FP-1234", "AUDIT-HEAD-ABCD");
        Assert.True(evidence.OverallPassed);
        Assert.True(evidence.VerifySeal());

        // Tampering with even one field breaks the SHA-256 seal.
        var tampered = evidence with { UnitUnderTestSerial = "FLIGHT-SN-9999" };
        Assert.False(tampered.VerifySeal());
    }
}
