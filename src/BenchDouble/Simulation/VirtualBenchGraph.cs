namespace BenchDouble.Simulation;

/// <summary>
/// A compile-time typed connection point in one virtual bench.
/// The type parameter preserves UnitsNet physical dimensions at wiring time.
/// </summary>
public sealed class SignalPort<T> where T : struct
{
    internal SignalPort(Guid graphId, string id)
    {
        GraphId = graphId;
        Id = id;
    }

    internal Guid GraphId { get; }
    public string Id { get; }
    public override string ToString() => $"{Id} ({typeof(T).Name})";
}

/// <summary>
/// Defines one simulation graph. Every port must have exactly one driver.
/// Bindings are frozen after Build, and cycles are rejected before execution.
/// </summary>
public sealed class VirtualBenchGraphBuilder
{
    private readonly Guid _id = Guid.NewGuid();
    private readonly Dictionary<string, PortDefinition> _ports = new(StringComparer.Ordinal);
    private readonly Dictionary<object, SignalBinding> _bindings = new();

    public SignalPort<T> Port<T>(string id) where T : struct
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_ports.ContainsKey(id))
            throw new ArgumentException($"Duplicate signal port: {id}", nameof(id));

        var port = new SignalPort<T>(_id, id);
        _ports.Add(id, new PortDefinition(port, id, typeof(T)));
        return port;
    }

    /// <summary>Attach a pure time-indexed source (for example a chamber setpoint).</summary>
    public void Source<T>(SignalPort<T> output, Func<TimeSpan, T> evaluate) where T : struct
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        Bind(output, Array.Empty<object>(), snapshot => evaluate(snapshot.Elapsed));
    }

    /// <summary>Wire a source directly to another port of exactly the same type.</summary>
    public void Connect<T>(SignalPort<T> input, SignalPort<T> output) where T : struct
    {
        Check(input);
        Bind(output, new object[] { input }, snapshot => snapshot.Read(input));
    }

    /// <summary>Transform one input into a derived output, including unit-safe physics.</summary>
    public void Transform<TInput, TOutput>(
        SignalPort<TInput> input, SignalPort<TOutput> output,
        Func<TInput, TimeSpan, TOutput> evaluate)
        where TInput : struct where TOutput : struct
    {
        Check(input);
        ArgumentNullException.ThrowIfNull(evaluate);
        Bind(output, new object[] { input },
            snapshot => evaluate(snapshot.Read(input), snapshot.Elapsed));
    }

    /// <summary>Transform two typed signals at one coherent logical timestamp.</summary>
    public void Transform<TA, TB, TOutput>(
        SignalPort<TA> first, SignalPort<TB> second, SignalPort<TOutput> output,
        Func<TA, TB, TimeSpan, TOutput> evaluate)
        where TA : struct where TB : struct where TOutput : struct
    {
        Check(first);
        Check(second);
        ArgumentNullException.ThrowIfNull(evaluate);
        Bind(output, new object[] { first, second },
            snapshot => evaluate(snapshot.Read(first), snapshot.Read(second), snapshot.Elapsed));
    }

    /// <summary>Validate complete wiring and cycles before making the graph usable.</summary>
    public VirtualBenchGraph Build()
    {
        foreach (var definition in _ports.Values)
        {
            if (!_bindings.ContainsKey(definition.Port))
                throw new InvalidOperationException($"Signal '{definition.Id}' has no source.");
        }

        var marks = new Dictionary<object, int>();
        void Visit(object port)
        {
            if (marks.TryGetValue(port, out int mark))
            {
                if (mark == 1)
                    throw new InvalidOperationException(
                        $"Cycle detected at signal '{_ports.Values.First(p => ReferenceEquals(p.Port, port)).Id}'.");
                if (mark == 2) return;
            }
            marks[port] = 1;
            foreach (var dependency in _bindings[port].Dependencies) Visit(dependency);
            marks[port] = 2;
        }

        foreach (var port in _bindings.Keys) Visit(port);
        return new VirtualBenchGraph(_id, new Dictionary<object, SignalBinding>(_bindings));
    }

    private void Bind<T>(
        SignalPort<T> output, object[] dependencies, Func<VirtualBenchSnapshot, object> evaluate)
        where T : struct
    {
        Check(output);
        if (!_bindings.TryAdd(output, new SignalBinding(dependencies, evaluate)))
            throw new InvalidOperationException($"Signal '{output.Id}' already has a source.");
    }

    private void Check<T>(SignalPort<T> port) where T : struct
    {
        ArgumentNullException.ThrowIfNull(port);
        if (port.GraphId != _id || !_ports.TryGetValue(port.Id, out var definition) ||
            !ReferenceEquals(definition.Port, port))
            throw new InvalidOperationException("Signal port belongs to another virtual bench.");
    }

    private sealed record PortDefinition(object Port, string Id, Type QuantityType);
}

internal sealed record SignalBinding(
    IReadOnlyList<object> Dependencies, Func<VirtualBenchSnapshot, object> Evaluate);

/// <summary>
/// Immutable wiring configuration. The graph is scoped to one experiment run.
/// Evaluation delegates should be deterministic and free of external side effects.
/// </summary>
public sealed class VirtualBenchGraph
{
    private readonly Guid _id;
    private readonly IReadOnlyDictionary<object, SignalBinding> _bindings;

    internal VirtualBenchGraph(Guid id, IReadOnlyDictionary<object, SignalBinding> bindings)
    {
        _id = id;
        _bindings = bindings;
    }

    public VirtualBenchSnapshot SampleAt(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        return new VirtualBenchSnapshot(_id, _bindings, elapsed);
    }
}

/// <summary>
/// One logical-time view of a virtual bench. A signal is evaluated at most once
/// per snapshot, so multiple instruments read the same cached sample.
/// </summary>
public sealed class VirtualBenchSnapshot
{
    private readonly Guid _id;
    private readonly IReadOnlyDictionary<object, SignalBinding> _bindings;
    private readonly Dictionary<object, object> _cache = new();
    private readonly HashSet<object> _evaluating = new();

    internal VirtualBenchSnapshot(Guid id,
        IReadOnlyDictionary<object, SignalBinding> bindings, TimeSpan elapsed)
    {
        _id = id;
        _bindings = bindings;
        Elapsed = elapsed;
    }

    public TimeSpan Elapsed { get; }

    public T Read<T>(SignalPort<T> port) where T : struct
    {
        ArgumentNullException.ThrowIfNull(port);
        if (port.GraphId != _id || !_bindings.TryGetValue(port, out var binding))
            throw new InvalidOperationException("Port is not part of this virtual bench.");
        if (_cache.TryGetValue(port, out var cached)) return (T)cached;
        if (!_evaluating.Add(port))
            throw new InvalidOperationException($"Recursive signal evaluation: {port.Id}");

        try
        {
            object value = binding.Evaluate(this);
            if (value is not T typed)
                throw new InvalidOperationException($"Signal '{port.Id}' returned an invalid value type.");
            _cache.Add(port, typed);
            return typed;
        }
        finally { _evaluating.Remove(port); }
    }
}
