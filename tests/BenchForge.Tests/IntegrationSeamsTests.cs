using BenchForge.Contracts;
using BenchForge.Procedures;
using BenchForge.Quantities;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>Tests for future-ready integration seams targeting SharpUnits, SharpContracts, and StarLark-cs.</summary>
public sealed class IntegrationSeamsTests
{
    /// <summary>Verify all nine canonical dimensions round-trip losslessly through UnitsNetQuantityConverter.</summary>
    [Fact]
    public void UnitsNetQuantityConverterRoundTripsAllCanonicalDimensions()
    {
        var converter = UnitsNetQuantityConverter.Instance;
        IQuantity[] samples =
        [
            ElectricPotential.FromVolts(12.5),
            ElectricCurrent.FromAmperes(0.75),
            ElectricResistance.FromOhms(470.0),
            Frequency.FromHertz(1_000_000.0),
            Power.FromWatts(2.5),
            Temperature.FromDegreesCelsius(37.5),
            Duration.FromSeconds(0.025),
            ElectricCapacitance.FromFarads(1e-6),
            Ratio.FromPercent(42.0)
        ];

        foreach (var sample in samples)
        {
            var canonical = converter.ToCanonical(sample);
            canonical.Validate();
            var roundTripped = converter.FromCanonical(canonical);
            Assert.Equal(sample.GetType(), roundTripped.GetType());
            Assert.Equal(canonical, converter.ToCanonical(roundTripped));
        }
    }

    /// <summary>Verify CanonicalQuantity rejects non-finite values and blank units.</summary>
    [Fact]
    public void CanonicalQuantityRejectsInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CanonicalQuantity(QuantityDimension.ElectricPotential, double.NaN, "V").Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CanonicalQuantity(QuantityDimension.ElectricCurrent, double.PositiveInfinity, "A").Validate());
        Assert.Throws<ArgumentException>(() =>
            new CanonicalQuantity(QuantityDimension.ElectricResistance, 10.0, " ").Validate());
    }

    /// <summary>Verify CompositeOperationGuard fails closed on the first denying guard without invoking later guards.</summary>
    /// <returns>Completion of the guard evaluation assertion.</returns>
    [Fact]
    public async Task CompositeOperationGuardFailsClosedOnFirstDenial()
    {
        bool thirdInvoked = false;
        var guard = new CompositeOperationGuard(new IInstrumentOperationGuard[]
        {
            new DelegateGuard((_, _) => ValueTask.FromResult(OperationGuardDecision.Allow("RULE-1"))),
            new DelegateGuard((_, _) => ValueTask.FromResult(OperationGuardDecision.Deny("RULE-2", "Compliance limit exceeded"))),
            new DelegateGuard((_, _) =>
            {
                thirdInvoked = true;
                return ValueTask.FromResult(OperationGuardDecision.Allow("RULE-3"));
            })
        });

        var context = new InstrumentOperationContext(
            "SMU1",
            "SourceVoltage",
            InstrumentOperationKind.OutputControl,
            1,
            new CanonicalQuantity(QuantityDimension.ElectricPotential, 250.0, "V"),
            IsPhysicalHardware: true);

        var decision = await guard.EvaluateAsync(context);
        Assert.False(decision.Allowed);
        Assert.Equal("RULE-2", decision.RuleId);
        Assert.Equal("Compliance limit exceeded", decision.Reason);
        Assert.False(thirdInvoked);
    }

    /// <summary>Verify ProcedureScriptBudget rejects non-positive limits.</summary>
    [Fact]
    public void ProcedureScriptBudgetRejectsNonPositiveBounds()
    {
        new ProcedureScriptBudget().Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcedureScriptBudget(MaxSteps: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcedureScriptBudget(MaxAllocatedBytes: -1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcedureScriptBudget(Timeout: TimeSpan.Zero).Validate());
    }

    private sealed class DelegateGuard(
        Func<InstrumentOperationContext, CancellationToken, ValueTask<OperationGuardDecision>> evaluator)
        : IInstrumentOperationGuard
    {
        public ValueTask<OperationGuardDecision> EvaluateAsync(
            InstrumentOperationContext context,
            CancellationToken cancellationToken = default) => evaluator(context, cancellationToken);
    }
}
