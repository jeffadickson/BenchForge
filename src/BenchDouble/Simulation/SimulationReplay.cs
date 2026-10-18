namespace BenchDouble.Simulation;
/// <summary>Immutable canonical scalar transcript entry; no conversion, hardware or ownership behavior.</summary>
/// <param name="Mode">Exact ordinal operating mode label.</param>
/// <param name="Capability">Exact ordinal scalar capability label.</param>
/// <param name="Quantity">Canonical quantity label supplied by the recording producer.</param>
/// <param name="Unit">Canonical unit label supplied by the recording producer.</param>
/// <param name="Elapsed">Nonnegative logical sample time, never a wall timestamp.</param>
/// <param name="Value">Finite canonical scalar value in Unit.</param>
public sealed record SimulationReplaySample(string Mode,string Capability,string Quantity,string Unit,TimeSpan Elapsed,double Value);
/// <summary>Immutable coherent standalone replay cursor diagnostics.</summary>
/// <param name="Position">Number of consumed samples.</param>
/// <param name="Length">Total plan samples.</param>
/// <param name="Remaining">Unconsumed sample count.</param>
public sealed record SimulationReplayStatus(int Position,int Length,int Remaining);
/// <summary>Bounded standalone transcript cursor with explicit logical times and serialized consumption.</summary>
/// <remarks>No clocks, timers or workers are owned. This cursor is not a transactional mock callback adapter; integration must commit consumption with the surrounding operation. It never authorizes physical commands.</remarks>
public sealed class SimulationReplay
{
    private readonly object _gate=new();
    private readonly SimulationReplaySample[] _samples;
    private int _position;
    /// <summary>Copy at most 4096 immutable ordered transcript samples.</summary>
    /// <param name="samples">Nonnull finite sequence; empty transcripts are exhausted.</param>
    /// <exception cref="ArgumentException">Samples are invalid, excessive or out of time order.</exception>
    public SimulationReplay(IEnumerable<SimulationReplaySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _samples=samples.Take(4097).ToArray();
        if(_samples.Length>4096)throw new ArgumentException("Transcript exceeds 4096 samples.",nameof(samples));
        foreach(var item in _samples)
        {
            if(item is null)throw new ArgumentException("Null transcript sample.",nameof(samples));
            ValidateLabel(item.Mode,nameof(item.Mode),128);ValidateLabel(item.Capability,nameof(item.Capability),128);
            ValidateLabel(item.Quantity,nameof(item.Quantity),128);ValidateLabel(item.Unit,nameof(item.Unit),80);
        }
        var previous=TimeSpan.Zero;
        foreach(var item in _samples)
        {
            if(item.Elapsed<previous)throw new ArgumentException("Transcript time must be nonnegative and ordered.",nameof(samples));
            previous=item.Elapsed;
        }
        // Validate plan.
    }
    private static void ValidateLabel(string label,string parameter,int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label,parameter);
        if(label.Length>maximum||label.Any(char.IsControl))throw new ArgumentException("Invalid transcript label.",parameter);
    }
    // Replay members.
}
