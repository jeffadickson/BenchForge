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
        if(_samples.Any(item=>!double.IsFinite(item.Value)))throw new ArgumentException("Transcript values must be finite.",nameof(samples));
        // Validate plan.
    }
    private static void ValidateLabel(string label,string parameter,int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label,parameter);
        if(label.Length>maximum||label.Any(char.IsControl))throw new ArgumentException("Invalid transcript label.",parameter);
    }
    /// <summary>Consume one exact operation at its recorded logical timestamp.</summary>
    /// <param name="mode">Exact ordinal mode label.</param>
    /// <param name="capability">Exact ordinal scalar capability label.</param>
    /// <param name="elapsed">Exact nonnegative logical sample timestamp.</param>
    /// <param name="ct">Cancellation observed before cursor mutation.</param>
    /// <returns>The immutable canonical sample including quantity and unit.</returns>
    /// <exception cref="ArgumentException">An input label or timestamp is invalid.</exception>
    /// <exception cref="InvalidOperationException">Transcript is exhausted or the operation does not match.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is observed before consumption.</exception>
    public SimulationReplaySample ReadNext(string mode,string capability,TimeSpan elapsed,CancellationToken ct=default)
    {
        ValidateLabel(mode,nameof(mode),128);ValidateLabel(capability,nameof(capability),128);
        if(elapsed<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(elapsed));
        ct.ThrowIfCancellationRequested();
        lock(_gate)
        {
            ct.ThrowIfCancellationRequested();
            if(_position==_samples.Length)throw new InvalidOperationException("Transcript is exhausted.");
            var item=_samples[_position];
            if(!StringComparer.Ordinal.Equals(item.Mode,mode)||!StringComparer.Ordinal.Equals(item.Capability,capability)||item.Elapsed!=elapsed)
                throw new InvalidOperationException("Operation differs from the next recorded sample.");
            ++_position;return item;
        }
    }

    /// <summary>Inspect the next immutable sample without consuming it.</summary>
    /// <returns>Next sample, or null when exhausted.</returns>
    public SimulationReplaySample? Peek(){lock(_gate)return _position==_samples.Length?null:_samples[_position];}

    /// <summary>Capture the cursor and plan length under one gate.</summary>
    /// <returns>Immutable position, length and remaining count.</returns>
    public SimulationReplayStatus Status(){lock(_gate)return new(_position,_samples.Length,_samples.Length-_position);}

    /// <summary>Reset only the local cursor to the beginning of this immutable plan.</summary>
    /// <param name="ct">Cancellation observed before cursor mutation.</param>
    /// <exception cref="OperationCanceledException">Cancellation is observed before reset.</exception>
    public void Reset(CancellationToken ct=default)
    {ct.ThrowIfCancellationRequested();lock(_gate){ct.ThrowIfCancellationRequested();_position=0;}}

    /// <summary>Create an independent cursor starting at zero, regardless of this cursor's position.</summary>
    /// <returns>New local replay with a copied immutable plan.</returns>
    public SimulationReplay Fork()=>new(_samples);

    /// <summary>Inspect the full immutable plan independently of current position.</summary>
    /// <returns>Detached read-only samples in recording order.</returns>
    public IReadOnlyList<SimulationReplaySample> Snapshot()=>Array.AsReadOnly(_samples.ToArray());
    /// <summary>Inspect the unconsumed tail under the cursor gate.</summary>
    /// <returns>Detached read-only remaining samples in recording order.</returns>
    public IReadOnlyList<SimulationReplaySample> RemainingSamples(){lock(_gate)return Array.AsReadOnly(_samples.Skip(_position).ToArray());}

    // Replay members.
}
