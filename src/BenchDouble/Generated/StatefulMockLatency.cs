using BenchDouble.Simulation;
namespace BenchDouble.Generated;
/// <summary>Immutable scalar simulation trigger timing; not a physical acquisition or ownership token.</summary>
/// <param name="Capability">Declared scalar capability.</param>
/// <param name="Mode">Mode pinned at preparation.</param>
/// <param name="StartedAt">Nonnegative logical trigger time.</param>
/// <param name="ReadyAt">Logical ready deadline.</param>
/// <param name="Latency">Detached seeded phase timing sample.</param>
public sealed record PreparedMockMeasurement(string Capability,string Mode,TimeSpan StartedAt,TimeSpan ReadyAt,SimulationLatencySample Latency);
public sealed partial class StatefulMockEngine
{
    private readonly Dictionary<string,PreparedMockMeasurement> _prepared=new(StringComparer.Ordinal);
    // Latency members.
}
