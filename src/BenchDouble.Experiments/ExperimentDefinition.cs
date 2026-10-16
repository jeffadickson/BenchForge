using BenchDouble.Simulation;
using System.Collections.ObjectModel;
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
    private string? _autoMockRuleHash;
    private bool _hasAutoMockLoad;
    private readonly Dictionary<string, string> _roleRuleHashes = new(StringComparer.Ordinal);
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

    /// <summary>
    /// Register a named simulated instrument with an explicit composition-root
    /// factory. This supports injecting a role-specific behavior without exposing
    /// IServiceProvider to the instrument implementation itself.
    /// </summary>
    public ExperimentDefinitionBuilder Simulated<TInterface>(
        string role, Func<IServiceProvider, TInterface> factory,
        string hostId = "simulation-worker")
        where TInterface : class
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentNullException.ThrowIfNull(factory);
        if (_instruments.Any(i => string.Equals(i.Role, role, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Duplicate instrument role '{role}'.");
        _instruments.Add(new ExperimentInstrument(role, typeof(TInterface), hostId, "Simulated"));
        _services.AddKeyedScoped<TInterface>(role, (sp, _) => factory(sp));
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

    /// <summary>
    /// Compose a deterministic, declarative AutoMock decorator around a normal
    /// instrument load behavior. The rules are frozen with the experiment definition.
    /// The factory is used only at the composition root, never by the instrument.
    /// </summary>
    public ExperimentDefinitionBuilder AutoMockLoad(
        Func<IServiceProvider, ILoadBehavior> baselineFactory,
        Action<AutoMockLoadRulesBuilder> configure)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(baselineFactory);
        ArgumentNullException.ThrowIfNull(configure);
        if (_hasAutoMockLoad)
            throw new InvalidOperationException("Only one unkeyed default ILoadBehavior AutoMock is supported; use AutoMockLoadFor for named instruments.");
        var rulesBuilder = new AutoMockLoadRulesBuilder();
        configure(rulesBuilder);
        var rules = rulesBuilder.Build();
        _autoMockRuleHash = AutoMockRuleFiles.Sha256(rules);
        _hasAutoMockLoad = true;
        _services.AddScoped<ILoadBehavior>(sp =>
            new RuleBasedLoadBehavior(baselineFactory(sp), rules));
        return this;
    }

    /// <summary>
    /// Register a keyed AutoMock for a particular instrument role.
    /// Use with the Simulated factory overload to supply the selected behavior
    /// to a normal instrument constructor. No global behavior lookup is needed.
    /// </summary>
    public ExperimentDefinitionBuilder AutoMockLoadFor(
        string role,
        Func<IServiceProvider, ILoadBehavior> baselineFactory,
        Action<AutoMockLoadRulesBuilder> configure)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(baselineFactory);
        ArgumentNullException.ThrowIfNull(configure);
        if (_roleRuleHashes.ContainsKey(role))
            throw new InvalidOperationException($"AutoMock already registered for role '{role}'.");
        var rulesBuilder = new AutoMockLoadRulesBuilder();
        configure(rulesBuilder);
        var rules = rulesBuilder.Build();
        _roleRuleHashes.Add(role, AutoMockRuleFiles.Sha256(rules));
        _services.AddKeyedScoped<ILoadBehavior>(role,
            (sp, _) => new RuleBasedLoadBehavior(baselineFactory(sp), rules));
        return this;
    }

    /// <summary>
    /// Configure a data-only AutoMock from a versioned JSON document.
    /// Parsing/validation happens before the experiment can be started.
    /// </summary>
    public ExperimentDefinitionBuilder AutoMockLoadFromJson(
        Func<IServiceProvider, ILoadBehavior> baselineFactory, string json)
    {
        EnsureMutable();
        var rules = AutoMockRuleFiles.Parse(json);
        return AutoMockLoad(baselineFactory, builder =>
        {
            foreach (var rule in rules) builder.AddRule(rule);
        });
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
        foreach (var role in _roleRuleHashes.Keys)
        {
            if (!_instruments.Any(i => i.Role == role && i.Backend == "Simulated"))
                throw new InvalidOperationException($"AutoMock role '{role}' has no simulated instrument.");
        }
        _built = true;
        var services = new ServiceCollection();
        foreach (var descriptor in _services)
            ((ICollection<ServiceDescriptor>)services).Add(descriptor);
        return new ExperimentDefinition(name, _instruments.ToArray(), services,
            _autoMockRuleHash, new Dictionary<string, string>(_roleRuleHashes));
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
        IServiceCollection services, string? autoMockRuleHash,
        IReadOnlyDictionary<string, string> roleRuleHashes)
    {
        Name = name;
        Instruments = instruments;
        _services = services;
        AutoMockRuleSha256 = autoMockRuleHash;
        AutoMockRuleHashes = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(roleRuleHashes));
    }

    /// <summary>Rule-set digest only; not a complete software/run manifest.</summary>
    public string? AutoMockRuleSha256 { get; }
    /// <summary>Per-instrument rule hashes; still not a complete run manifest.</summary>
    public IReadOnlyDictionary<string, string> AutoMockRuleHashes { get; }

    public string Name { get; }
    public IReadOnlyList<ExperimentInstrument> Instruments { get; }

    /// <summary>Create a simulation scope with instrument factories resolved lazily on first access.</summary>
    /// <returns>A session owned by the caller, who must dispose it even if later resolution fails.</returns>
    /// <remarks>Use StartSimulatedAsync for eager role resolution and automatic failed-start cleanup.</remarks>
    public ExperimentSession StartSimulated()
    {
        // A new container per run prevents singleton-like model state leaking across runs.
        var provider = _services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });
        return new ExperimentSession(provider, provider.CreateAsyncScope(), Instruments,
            AutoMockRuleSha256, AutoMockRuleHashes);
    }

    /// <summary>Create a simulation run and resolve all registered instrument roles before returning it.</summary>
    /// <remarks>
    /// Factories are synchronous; cancellation is checked between factories and after the last one.
    /// Failed startup disposes the DI scope and provider, including asynchronous disposable services.
    /// Factories own resources that they allocate but do not successfully return to DI.
    /// This method does not acquire physical leases or establish a hardware safe state.
    /// </remarks>
    /// <param name="ct">Cancellation before startup or between instrument resolutions.</param>
    /// <returns>A fully resolved simulation session owned by the caller, who must dispose it.</returns>
    /// <exception cref="OperationCanceledException">Startup was cancelled.</exception>
    /// <exception cref="AggregateException">Startup and cleanup both failed; both errors are retained.</exception>
    public async ValueTask<ExperimentSession> StartSimulatedAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var session = StartSimulated();
        try
        {
            session.ResolveInstruments(ct);
            return session;
        }
        catch (Exception startupError)
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Simulation startup and cleanup failed.", startupError, cleanupError);
            }
            throw;
        }
    }
}

/// <summary>
/// Owns the simulation scope and the provider. Only this composition boundary
/// resolves named instruments. Always dispose when a run finishes.
/// </summary>
public sealed class ExperimentSession : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;
    private readonly IReadOnlyList<ExperimentInstrument> _instruments;
    private TaskCompletionSource? _disposalCompletion;

    internal ExperimentSession(ServiceProvider provider, AsyncServiceScope scope,
        IReadOnlyList<ExperimentInstrument> instruments, string? autoMockRuleHash,
        IReadOnlyDictionary<string, string> roleRuleHashes)
    {
        _provider = provider;
        _scope = scope;
        _instruments = instruments;
        RunId = Guid.NewGuid().ToString("N");
        AutoMockRuleSha256 = autoMockRuleHash;
        AutoMockRuleHashes = roleRuleHashes;
    }

    public string RunId { get; }
    /// <summary>Registered instrument roles visible to procedure preflight.</summary>
    public IReadOnlyList<ExperimentInstrument> RegisteredInstruments => _instruments;
    /// <summary>Digest of declarative mock rules; excludes code, drivers and dependencies.</summary>
    public string? AutoMockRuleSha256 { get; }
    public IReadOnlyDictionary<string, string> AutoMockRuleHashes { get; }
    /// <summary>Run-scoped virtual clock, available until disposal starts.</summary>
    /// <exception cref="ObjectDisposedException">Run cleanup has started.</exception>
    public ManualSimulationClock Clock
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposalCompletion) is not null, this);
            return _scope.ServiceProvider.GetRequiredService<ManualSimulationClock>();
        }
    }

    public T Instrument<T>(string role) where T : class
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposalCompletion) is not null, this);
        var match = _instruments.FirstOrDefault(i => i.Role == role);
        if (match is null || match.InterfaceType != typeof(T))
            throw new KeyNotFoundException($"No instrument role '{role}' implementing {typeof(T).Name}.");
        return _scope.ServiceProvider.GetRequiredKeyedService<T>(role);
    }

    internal void ResolveInstruments(CancellationToken ct)
    {
        foreach (var instrument in _instruments)
        {
            ct.ThrowIfCancellationRequested();
            _scope.ServiceProvider.GetRequiredKeyedService(instrument.InterfaceType, instrument.Role);
        }
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Dispose the run scope and always attempt provider cleanup, even after a scope failure.</summary>
    /// <returns>Shared completion of asynchronous cleanup, including its failure if any.</returns>
    /// <remarks>
    /// Concurrent and repeated calls await the same cleanup operation; failures remain observable.
    /// Callers must still serialize instrument access with disposal. Resource cleanup callbacks must
    /// not await disposal of their owning session, which would wait on their own completion.
    /// </remarks>
    /// <exception cref="AggregateException">Both scope and provider cleanup failed.</exception>
    public ValueTask DisposeAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = Interlocked.CompareExchange(ref _disposalCompletion, completion, null);
        if (existing is not null) return new ValueTask(existing.Task);
        // Publish completion before invoking user disposal callbacks, without holding a lock.
        _ = CompleteDisposalAsync(completion);
        return new ValueTask(completion.Task);
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeOwnedServicesAsync().ConfigureAwait(false);
            completion.SetResult();
        }
        catch (Exception error) { completion.SetException(error); }
    }

    private async Task DisposeOwnedServicesAsync()
    {
        try { await _scope.DisposeAsync().ConfigureAwait(false); }
        catch (Exception scopeError)
        {
            try { await _provider.DisposeAsync().ConfigureAwait(false); }
            catch (Exception providerError)
            {
                throw new AggregateException("Simulation scope and provider cleanup failed.", scopeError, providerError);
            }
            throw;
        }
        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}
