using System.Collections.ObjectModel;

namespace BenchDouble.Generated;

/// <summary>Detached cursor diagnostics for a configured scalar output sequence.</summary>
/// <param name="Length">Number of values copied at configuration time.</param>
/// <param name="Position">Number of initial values successfully published, at most Length.</param>
/// <param name="RepeatLast">Whether the last value remains readable after initial exhaustion.</param>
public sealed record MockSequenceStatus(int Length, int Position, bool RepeatLast)
{
    /// <summary>Number of initial sequence values not yet successfully published.</summary>
    public int Remaining => Length - Position;
    /// <summary>Whether all initial values have been consumed; RepeatLast may still permit reads.</summary>
    public bool Exhausted => Position == Length;
}

/// <summary>Transactional finite scalar sequences owned by the same mock state authority.</summary>
public sealed partial class StatefulMockEngine
{
    private sealed class Sequence(double[] values, bool repeatLast)
    {
        internal double[] Values { get; } = values;
        internal bool RepeatLast { get; } = repeatLast;
        internal int Position { get; set; }
    }
    private readonly Dictionary<string, Sequence> _sequences = new(StringComparer.Ordinal);

    /// <summary>Install a copied finite sequence, replacing the capability's synchronous or async model.</summary>
    /// <param name="capability">Declared scalar capability, measured in its canonical unit.</param>
    /// <param name="values">One to 4096 finite values; at most 4097 elements are consumed.</param>
    /// <param name="repeatLast">True to hold the final value after exhaustion; false to fail explicitly.</param>
    /// <exception cref="ArgumentException">The sequence is empty or exceeds the configured bound.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value is not finite.</exception>
    /// <remarks>Only a successful, mode-compatible, noncancelled scalar read advances the cursor. Callers must not reenter from a model callback.</remarks>
    public void SetSequence(string capability, IEnumerable<double> values, bool repeatLast = false)
    {
        _definition.Capability(capability);
        ArgumentNullException.ThrowIfNull(values);
        var copied = values.Take(4097).ToArray();
        if (copied.Length is < 1 or > 4096) throw new ArgumentException("A mock sequence requires one to 4096 values.", nameof(values));
        if (copied.Any(value => !double.IsFinite(value))) throw new ArgumentOutOfRangeException(nameof(values));
        _gate.Wait();
        try
        {
            _models.Remove(capability); _asyncModels.Remove(capability);
            _sequences[capability] = new(copied, repeatLast);
            ++_revision;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Remove one sequence and restore the capability's override or definition fallback.</summary>
    /// <param name="capability">Declared scalar capability.</param>
    /// <returns>True when a sequence was removed; an absent sequence leaves revision unchanged.</returns>
    public bool ClearSequence(string capability)
    {
        _definition.Capability(capability);
        _gate.Wait();
        try { if (!_sequences.Remove(capability)) return false; ++_revision; return true; }
        finally { _gate.Release(); }
    }

    /// <summary>Inspect a copied cursor without reading the clock or consuming values or faults.</summary>
    /// <param name="capability">Declared scalar capability.</param>
    /// <returns>Detached sequence status, or null when no sequence is installed.</returns>
    public MockSequenceStatus? SequenceStatus(string capability)
    {
        _definition.Capability(capability);
        _gate.Wait();
        try { return _sequences.TryGetValue(capability, out var sequence) ? new(sequence.Values.Length, sequence.Position, sequence.RepeatLast) : null; }
        finally { _gate.Release(); }
    }

    /// <summary>Remove all scalar sequences without changing modes, overrides, other models or faults.</summary>
    /// <returns>Number of removed sequences; zero leaves revision unchanged.</returns>
    public int ClearAllSequences()
    {
        _gate.Wait();
        try
        {
            var count = _sequences.Count;
            _sequences.Clear();
            if (count > 0) ++_revision;
            return count;
        }
        finally { _gate.Release(); }
    }

    private bool TrySequence(string capability, out double value, out Sequence? advancing)
    {
        value = 0; advancing = null;
        if (!_sequences.TryGetValue(capability, out var sequence)) return false;
        if (sequence.Position == sequence.Values.Length)
        {
            if (!sequence.RepeatLast) throw new InvalidOperationException("Mock reading sequence exhausted.");
            value = sequence.Values[^1]; return true;
        }
        value = sequence.Values[sequence.Position]; advancing = sequence; return true;
    }

    private IReadOnlyDictionary<string, int> SequencePositions() =>
        new ReadOnlyDictionary<string, int>(_sequences.ToDictionary(pair => pair.Key, pair => pair.Value.Position, StringComparer.Ordinal));
}
