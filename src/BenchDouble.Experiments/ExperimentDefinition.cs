using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;

namespace BenchDouble.Experiments;

/// <summary>Stable experiment role and host assignment; not a live physical lease.</summary>
public sealed record ExperimentInstrument(string Role, Type InterfaceType, string HostId, string Backend);

/// <summary>
/// Defines instrument roles and simulation strategies at the composition root.
/// No service locator is exposed to instrument/behavior implementation classes.
/// </summary>
public sealed class ExperimentDefinitionBuilder
{
    private readonly ServiceCollection _services = new();
    private readonly List<ExperimentInstrument> _instruments = new();
    private bool _built;

    public ExperimentDefinitionBuilder()
    {
        _services.AddScoped<ManualSimulationClock>();
        _services.AddScoped<ISimulationClock>(sp => sp.GetRequiredService<ManualSimulationClock>());
    }

    /// <summary>
    /// Registers one named simulated instrument. Implementation constructor dependencies
    /// are resolved normally by the .NET DI container. The role is a keyed service.
    /// </summary>
    public ExperimentDefinitionBuilder Simulated<TInterface, TImplementation>(
        string role, string hostId = "simulation-worker")
        where TInterface : class where TImplementation : class, TInterface
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        if (_instruments.Any(i => string.Equals(i.Role, role, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Duplicate instrument role '{role}'.");
        _instruments.Add(new ExperimentInstrument(role, typeof(TInterface), hostId, "Simulated"));
        _services.AddKeyedScoped<TInterface, TImplementation>(role);
        return this;
    }

    /// <summary>Register an injectable behavior, clock consumer, or virtual physical model.</summary>
    public ExperimentDefinitionBuilder Behavior<TInterface, TImplementation>()
        where TInterface : class where TImplementation : class, TInterface
    {
        EnsureMutable();
        _services.AddScoped<TInterface, TImplementation>();
        return this;
    }

    /// <summary>Configure a run-scoped model using a composition-root factory.</summary>
    public ExperimentDefinitionBuilder Model<TService>(Func<IServiceProvider, TService> factory)
        where TService : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(factory);
        _services.AddScoped(factory);
        return this;
    }

    public ExperimentDefinition Build(string name)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _built = true;
        var services = new ServiceCollection();
        foreach (var descriptor in _services) services.Add(descriptor);
        return new ExperimentDefinition(name, _instruments.ToArray(), services);
    }

    private void EnsureMutable()
    {
        if (_built) throw new InvalidOperationException("Experiment definition is already frozen.");
    }
}

/// <summary>
/// Immutable experiment description. Starting creates a new DI container and run scope;
/// physical host discovery, leases and authorization are intentionally NOT implemented.
/// </summary>
public sealed class ExperimentDefinition
{
    private readonly IServiceCollection _services;
    internal ExperimentDefinition(string name, IReadOnlyList<ExperimentInstrument> instruments,
        IServiceCollection services)
    {
        Name = name;
        Instruments = instruments;
        _services = services;
    }

    public string Name { get; }
    public IReadOnlyList<ExperimentInstrument> Instruments { get; }

    public ExperimentRun StartSimulated()
    {
        // A new container per run prevents singleton-like model state leaking across runs.
        var provider = _services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });
        return new ExperimentRun(provider, provider.CreateAsyncScope(), Instruments);
    }
}

/// <summary>
/// Owns the simulation scope and the provider. Only this composition boundary
/// resolves named instruments. Always dispose when a run finishes.
/// </summary>
public sealed class ExperimentRun : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;
    private readonly IReadOnlyList<ExperimentInstrument> _instruments;
    private bool _disposed;

    internal ExperimentRun(ServiceProvider provider, AsyncServiceScope scope,
        IReadOnlyList<ExperimentInstrument> instruments)
    {
        _provider = provider;
        _scope = scope;
        _instruments = instruments;
        RunId = Guid.NewGuid().ToString("N");
    }

    public string RunId { get; }
    public ManualSimulationClock Clock => _scope.ServiceProvider.GetRequiredService<ManualSimulationClock>();

    public T Instrument<T>(string role) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var match = _instruments.FirstOrDefault(i => i.Role == role);
        if (match is null || match.InterfaceType != typeof(T))
            throw new KeyNotFoundException($"No instrument role '{role}' implementing {typeof(T).Name}.");
        return _scope.ServiceProvider.GetRequiredKeyedService<T>(role);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _scope.DisposeAsync().ConfigureAwait(false);
        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}
