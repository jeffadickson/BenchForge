namespace BenchDouble.Simulation;
/// <summary>Separate simulated latency phases; never physical deadline or ownership clocks.</summary>
public enum SimulationLatencyPhase
{
    /// <summary>Initialization or warmup.</summary>
    Initialization,
    /// <summary>Physical-model settling.</summary>
    Settling,
    /// <summary>Measurement integration.</summary>
    Integration,
    /// <summary>Trigger acceptance.</summary>
    Trigger,
    /// <summary>Acquisition duration.</summary>
    Acquisition,
    /// <summary>Result processing.</summary>
    Processing,
    /// <summary>Optional simulated display pacing.</summary>
    Display
}
/// <summary>Immutable logical phase timing, validated by a latency profile.</summary>
/// <param name="Phase">One declared latency phase.</param>
/// <param name="Duration">Nonnegative base logical duration.</param>
/// <param name="Jitter">Nonnegative symmetric jitter radius, no greater than Duration.</param>
public sealed record SimulationLatencyStage(SimulationLatencyPhase Phase, TimeSpan Duration, TimeSpan Jitter);
