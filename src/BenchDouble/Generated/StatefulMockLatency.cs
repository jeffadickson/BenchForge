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
    /// <summary>Prepare one opt-in simulated scalar measurement without sleeping or advancing time.</summary>
    /// <param name="capability">Declared mode-compatible scalar capability.</param>
    /// <param name="profile">Immutable simulation latency profile.</param>
    /// <param name="operationIndex">Nonnegative deterministic sample index.</param>
    /// <param name="ct">Cancellation before mutation or while waiting for the transaction.</param>
    /// <returns>Detached logical timing pinned to the current mode.</returns>
    /// <exception cref="InvalidOperationException">Mode is incompatible, a preparation exists or clock is invalid.</exception>
    /// <exception cref="ArgumentException">Profile, capability or index is invalid.</exception>
    /// <exception cref="OverflowException">The ready deadline cannot be represented.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is observed before mutation.</exception>
    public async ValueTask<PreparedMockMeasurement> PrepareMeasurementAsync(string capability,SimulationLatencyProfile profile,long operationIndex,CancellationToken ct=default)
    {
        var metadata=_definition.Capability(capability);ArgumentNullException.ThrowIfNull(profile);
        var latency=profile.Sample(operationIndex);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            if(_mode is null||!metadata.Modes.Contains(_mode))throw new InvalidOperationException("Incompatible preparation mode.");
            if(_prepared.ContainsKey(capability))throw new InvalidOperationException("A scalar preparation is already pending.");
            var started=ReadElapsed();ct.ThrowIfCancellationRequested();
            var prepared=new PreparedMockMeasurement(capability,_mode,started,latency.ReadyAt(started),latency);
            _prepared.Add(capability,prepared);++_revision;return prepared;
        }
        finally{_gate.Release();}
    }

    // Latency members.
}
