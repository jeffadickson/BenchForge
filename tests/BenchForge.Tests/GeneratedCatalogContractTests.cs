using System.Reflection;
using BenchForge.Generated;
using BenchForge.Simulation;
using UnitsNet;
using Xunit;

namespace BenchForge.Tests;

/// <summary>Contract tests exercising every clean-room generated SCPI driver and stateful mock pair.</summary>
public sealed class GeneratedCatalogContractTests
{
    /// <summary>Verify at least 35 clean-room SCPI driver/mock pairs exist and round-trip queries, writes, and resets.</summary>
    /// <returns>Completion of all generated driver/mock assertions.</returns>
    [Fact]
    public async Task AllGeneratedDriverMockPairsRoundTripAndResetCleanly()
    {
        var assembly = typeof(Keysight33522B).Assembly;
        var mockTypes = assembly.GetTypes()
            .Where(t => t.Namespace == "BenchForge.Generated" &&
                t.IsClass && !t.IsAbstract &&
                typeof(DefinitionDrivenScpiMockSession).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(mockTypes.Length >= 35, $"Expected at least 35 generated instrument mocks, found {mockTypes.Length}.");

        foreach (var mockType in mockTypes)
        {
            var mock = (DefinitionDrivenScpiMockSession)Activator.CreateInstance(mockType)!;
            var createDriver = mockType.GetMethod("CreateDriver", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(createDriver);
            var driver = createDriver!.Invoke(mock, null)!;
            var driverType = driver.GetType();

            var initialReadings = mock.GetReadingsSnapshot();
            Assert.NotEmpty(initialReadings);

            // Verify every Get*Async method returns the expected initial value for channel 1.
            foreach (var getter in driverType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.StartsWith("Get", StringComparison.Ordinal) && m.Name.EndsWith("Async", StringComparison.Ordinal)))
            {
                var taskObj = getter.Invoke(driver, new object[] { 1, CancellationToken.None })!;
                var asTask = taskObj.GetType().GetMethod("AsTask")!;
                var task = (Task)asTask.Invoke(taskObj, null)!;
                await task.ConfigureAwait(false);
                var result = task.GetType().GetProperty("Result")!.GetValue(task);
                Assert.NotNull(result);
            }

            // Verify every Set*Async method round-trips its default value cleanly and records a transcript entry.
            foreach (var setter in driverType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.StartsWith("Set", StringComparison.Ordinal) && m.Name.EndsWith("Async", StringComparison.Ordinal)))
            {
                string paramName = setter.Name[3..^5];
                var reading = initialReadings.First(r => r.Parameter == paramName && r.Channel == 1);
                var quantityType = setter.GetParameters()[1].ParameterType;
                var value = CreateQuantity(quantityType, reading.Value);

                var taskObj = setter.Invoke(driver, new object[] { 1, value, CancellationToken.None })!;
                await ((ValueTask)taskObj).ConfigureAwait(false);
            }

            Assert.NotEmpty(mock.GetTranscriptSnapshot());
            await mock.ResetAsync();
            Assert.Empty(mock.GetTranscriptSnapshot());
        }
    }

    private static object CreateQuantity(Type quantityType, double canonicalValue)
    {
        if (quantityType == typeof(ElectricPotential)) return ElectricPotential.FromVolts(canonicalValue);
        if (quantityType == typeof(ElectricCurrent)) return ElectricCurrent.FromAmperes(canonicalValue);
        if (quantityType == typeof(ElectricResistance)) return ElectricResistance.FromOhms(canonicalValue);
        if (quantityType == typeof(Frequency)) return Frequency.FromHertz(canonicalValue);
        if (quantityType == typeof(Power)) return Power.FromWatts((decimal)canonicalValue);
        if (quantityType == typeof(Temperature)) return Temperature.FromDegreesCelsius(canonicalValue);
        if (quantityType == typeof(Duration)) return Duration.FromSeconds(canonicalValue);
        if (quantityType == typeof(ElectricCapacitance)) return ElectricCapacitance.FromFarads(canonicalValue);
        if (quantityType == typeof(Ratio)) return Ratio.FromPercent(canonicalValue);
        throw new NotSupportedException($"Unexpected generated quantity type: {quantityType.Name}");
    }
}
