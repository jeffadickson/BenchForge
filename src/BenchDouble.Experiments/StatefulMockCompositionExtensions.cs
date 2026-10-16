using BenchDouble.Generated;
using BenchDouble.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BenchDouble.Experiments;

/// <summary>Composition-root helpers for run-scoped stateful instrument mocks.</summary>
public static class StatefulMockCompositionExtensions
{
    /// <summary>Register a keyed stateful mock with a shared run clock and structured logger.</summary>
    /// <typeparam name="TInstrument">Instrument facade type.</typeparam>
    /// <param name="builder">Experiment definition builder.</param>
    /// <param name="role">Unique instrument role.</param>
    /// <param name="factory">Factory receiving injected clock and logger.</param>
    /// <param name="hostId">Logical simulation host identity.</param>
    /// <returns>Updated experiment builder.</returns>
    public static ExperimentDefinitionBuilder StatefulMock<TInstrument>(
        this ExperimentDefinitionBuilder builder, string role,
        Func<ISimulationClock, ILogger<StatefulMockEngine>, TInstrument> factory,
        string hostId = "simulation-worker") where TInstrument : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        return builder.Simulated<TInstrument>(role, sp => factory(
            sp.GetRequiredService<ISimulationClock>(),
            sp.GetService<ILogger<StatefulMockEngine>>() ?? NullLogger<StatefulMockEngine>.Instance), hostId);
    }

    /// <summary>Register a stateful mock that shares a run-scoped physical simulation model.</summary>
    /// <typeparam name="TInstrument">Instrument facade type.</typeparam>
    /// <typeparam name="TModel">Registered run-scoped model type.</typeparam>
    /// <param name="builder">Experiment definition builder.</param>
    /// <param name="role">Unique instrument role.</param>
    /// <param name="factory">Factory receiving clock, logger, and shared model.</param>
    /// <param name="hostId">Logical simulation host identity.</param>
    /// <returns>Updated experiment builder.</returns>
    public static ExperimentDefinitionBuilder StatefulMock<TInstrument, TModel>(
        this ExperimentDefinitionBuilder builder, string role,
        Func<ISimulationClock, ILogger<StatefulMockEngine>, TModel, TInstrument> factory,
        string hostId = "simulation-worker") where TInstrument : class where TModel : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        return builder.Simulated<TInstrument>(role, sp => factory(
            sp.GetRequiredService<ISimulationClock>(),
            sp.GetService<ILogger<StatefulMockEngine>>() ?? NullLogger<StatefulMockEngine>.Instance,
            sp.GetRequiredService<TModel>()), hostId);
    }
}
