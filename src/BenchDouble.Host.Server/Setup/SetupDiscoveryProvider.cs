namespace BenchDouble.Host.Server.Setup;

/// <summary>Provider boundary for cancellable inventory discovery; enumeration must respect host ownership.</summary>
public interface ISetupDiscoveryProvider
{
    /// <summary>Enumerate supported endpoints without changing instrument configuration.</summary>
    /// <param name="ct">Cancellation before or during enumeration.</param>
    /// <returns>Separate endpoint evidence and actionable provider diagnostics.</returns>
    ValueTask<SetupDiscovery> DiscoverAsync(CancellationToken ct = default);
}

/// <summary>Demonstrates two independent GPIB buses and an alternate LAN route using existing host simulators.</summary>
/// <param name="registry">The host's existing canonical ownership authority.</param>
public sealed class SimulatedSetupDiscoveryProvider(HostResourceRegistry registry) : ISetupDiscoveryProvider
{
    /// <summary>Read cached host metadata; never issue discovery I/O through an active lease.</summary>
    /// <param name="ct">Cancellation before enumeration.</param>
    /// <returns>Simulated adapter/endpoint inventory, explicitly labeled as simulated.</returns>
    public ValueTask<SetupDiscovery> DiscoverAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = registry.Monitor().Instruments.ToDictionary(x => x.ResourceId, StringComparer.OrdinalIgnoreCase);
        DiscoveredEndpoint Endpoint(string resource, string transport, string address, string? controller)
        {
            var item = state[resource];
            var identity = item.Identification?.Identity;
            return new("simulation", transport, address, controller, resource,
                new(identity?.Manufacturer, identity?.Model, null), item.IsLocked);
        }
        return ValueTask.FromResult(new SetupDiscovery(Guid.NewGuid().ToString("N"),
            [new("simulation", "GPIB0", "Simulated rack A adapter"),
             new("simulation", "GPIB1", "Simulated rack B adapter")],
            [Endpoint("DMM1", "Ethernet", "dmm1.simulated", null),
             Endpoint("DMM1", "GPIB", "22", "GPIB0"),
             Endpoint("FGEN1", "GPIB", "22", "GPIB1")],
            ["Simulation inventory only. Physical VISA/USB-GPIB/LAN discovery providers are not installed. Add physical endpoints manually as untested drafts."]));
    }
}
