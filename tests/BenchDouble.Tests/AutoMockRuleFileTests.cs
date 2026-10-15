using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class AutoMockRuleFileTests
{
    [Fact]
    public void JsonRulesApplyVoltageScalingThenTimedFault()
    {
        const string json = """
            {"version":1,"rules":[
              {"name":"fault","priority":100,"action":"FixedCurrent","value":2.5,"fromSeconds":5},
              {"name":"gain","priority":10,"action":"MultiplyBaseline","value":1.1,
               "minimumVolts":10,"maximumVolts":15,"untilSeconds":5}
            ]}
            """;
        var rules = AutoMockRuleFiles.Parse(json);
        var behavior = new RuleBasedLoadBehavior(
            new ResistiveLoadBehavior(ElectricResistance.FromOhms(24)), rules);
        Assert.Equal(0.55, behavior.CurrentAt(ElectricPotential.FromVolts(12),
            TimeSpan.Zero).Amperes, 10);
        Assert.Equal(2.5, behavior.CurrentAt(ElectricPotential.FromVolts(12),
            TimeSpan.FromSeconds(5)).Amperes, 10);
    }

    [Theory]
    [InlineData("""{"version":2,"rules":[]}""")]
    [InlineData("""{"version":1,"rules":[{"name":"bad","action":"ExecuteCode","value":1}]}""")]
    [InlineData("""{"version":1,"rules":[{"name":"bad","action":"FixedCurrent","value":-1}]}""")]
    [InlineData("""{"version":1,"rules":[{"name":"bad","action":"FixedCurrent","value":1,"unexpected":"code"}]}""")]
    public void RejectsUnsupportedOrInvalidConfiguration(string json) =>
        Assert.ThrowsAny<Exception>(() => AutoMockRuleFiles.Parse(json));
}
