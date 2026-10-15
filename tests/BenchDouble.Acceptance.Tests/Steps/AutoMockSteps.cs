using BenchDouble.Simulation;
using Reqnroll;
using UnitsNet;
using Xunit;

namespace BenchDouble.Acceptance.Tests.Steps;

/// <summary>BDD verification of declarative fault injection and instrument state.</summary>
[Binding]
public sealed class AutoMockSteps
{
    private ManualSimulationClock? _clock;
    private MockPowerSupply? _supply;
    private PowerSupplySample? _last;

    [Given(@"an AutoMock supply with a (.*) ohm baseline and a fault at (.*) seconds")]
    public async Task GivenSupply(double ohms, int seconds)
    {
        _clock = new ManualSimulationClock();
        var rules = new AutoMockLoadRulesBuilder()
            .FixedCurrent("overcurrent", ElectricCurrent.FromAmperes(2.5),
                new AutoMockLoadCondition(From: TimeSpan.FromSeconds(seconds)))
            .Build();
        _supply = new MockPowerSupply(new RuleBasedLoadBehavior(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(ohms)), rules), _clock);
        await _supply.ConfigureAsync(ElectricPotential.FromVolts(12),
            ElectricCurrent.FromAmperes(3));
        await _supply.SetOutputAsync(true);
    }

    [When(@"the AutoMock supply is sampled at (.*) seconds")]
    public async Task WhenSampled(int seconds)
    {
        _clock!.AdvanceBy(TimeSpan.FromSeconds(seconds) - _clock.Elapsed);
        _last = await _supply!.MeasureAsync();
    }

    [When("the AutoMock supply output is disabled")]
    public async Task WhenDisabled()
    {
        await _supply!.SetOutputAsync(false);
        _last = await _supply.MeasureAsync();
    }

    [Then(@"the AutoMock supply current is (.*) amperes")]
    public void ThenCurrent(double amps) =>
        Assert.Equal(amps, _last!.Current.Amperes, 10);
}
