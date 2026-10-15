using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using UnitsNet;

namespace BenchDouble.Simulation;

/// <summary>
/// Versioned, data-only AutoMock configuration. Deliberately does not accept
/// C# expressions, reflection, method calls, or dynamically loaded assemblies.
/// </summary>
public static class AutoMockRuleFiles
{
    public sealed class Document
    {
        public int Version { get; set; }
        public List<RuleSpec>? Rules { get; set; }
    }

    public sealed class RuleSpec
    {
        public string? Name { get; set; }
        public string? Action { get; set; }
        public double Value { get; set; }
        public int Priority { get; set; }
        public double? FromSeconds { get; set; }
        public double? UntilSeconds { get; set; }
        public double? MinimumVolts { get; set; }
        public double? MaximumVolts { get; set; }
    }

    /// <summary>Parse a bounded version-one JSON rule set and validate before use.</summary>
    public static IReadOnlyList<AutoMockLoadRule> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 131072)
            throw new ArgumentException("AutoMock configuration exceeds the 128K character limit.", nameof(json));
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var document = JsonSerializer.Deserialize<Document>(json, options)
            ?? throw new ArgumentException("AutoMock document must be an object.", nameof(json));
        if (document.Version != 1 || document.Rules is null || document.Rules.Count > 256)
            throw new ArgumentException("Unsupported AutoMock version or rule count.", nameof(json));
        var builder = new AutoMockLoadRulesBuilder();
        foreach (var spec in document.Rules)
        {
            if (spec is null || !Enum.TryParse<AutoMockLoadActionKind>(
                    spec.Action, true, out var action) || !Enum.IsDefined(action))
                throw new ArgumentException("Invalid AutoMock action.", nameof(json));
            var condition = new AutoMockLoadCondition(
                From: spec.FromSeconds is { } from ? Seconds(from) : null,
                Until: spec.UntilSeconds is { } until ? Seconds(until) : null,
                MinimumVoltage: spec.MinimumVolts is { } low
                    ? ElectricPotential.FromVolts(low) : null,
                MaximumVoltage: spec.MaximumVolts is { } high
                    ? ElectricPotential.FromVolts(high) : null);
            if (action == AutoMockLoadActionKind.FixedCurrent)
                builder.FixedCurrent(spec.Name ?? "", ElectricCurrent.FromAmperes(spec.Value),
                    condition, spec.Priority);
            else
                builder.ScaleBaseline(spec.Name ?? "", spec.Value, condition, spec.Priority);
        }
        return builder.Build();
    }

    /// <summary>
    /// Stable SHA-256 of the validated ordered rule data. This identifies rules,
    /// not the baseline behavior assembly or full experiment configuration.
    /// </summary>
    public static string Sha256(IEnumerable<AutoMockLoadRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var validated = rules.ToArray();
        foreach (var rule in validated) rule.Validate();
        var canonical = JsonSerializer.Serialize(validated);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static TimeSpan Seconds(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > TimeSpan.MaxValue.TotalSeconds)
            throw new ArgumentOutOfRangeException(nameof(value));
        return TimeSpan.FromSeconds(value);
    }
}
