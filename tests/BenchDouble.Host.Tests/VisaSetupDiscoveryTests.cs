using BenchDouble.Host.Server.Setup;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Verifies scoped VISA catalog translation without vendor drivers or instrument commands.</summary>
public sealed class VisaSetupDiscoveryTests
{
    /// <summary>Two GPIB controllers preserve address scope and unknown physical identity.</summary>
    /// <returns>Completion of catalog translation checks.</returns>
    [Fact]
    public async Task GpibCatalogPreservesControllerScope()
    {
        string? expression = null;
        var provider = new NativeVisaDiscoveryProvider("GPIB", enumerate: (value, _) =>
        { expression = value; return ["GPIB0::INTFC", "GPIB1::INTFC", "GPIB0::22::INSTR", "GPIB1::22::INSTR", "GPIB1::22::1::INSTR"]; });
        var result = await provider.DiscoverAsync();
        Assert.Equal("GPIB[0-9]*::?*", expression);
        Assert.Equal(2, result.Controllers.Length);
        Assert.Equal(2, result.Endpoints.Length);
        Assert.All(result.Endpoints, x => { Assert.Equal("22", x.Address); Assert.Null(x.ResourceId); Assert.Null(x.Identity); });
        Assert.NotEqual(result.Endpoints[0].ControllerAddress, result.Endpoints[1].ControllerAddress);
        Assert.Contains(result.Diagnostics, x => x.Contains("secondary addresses"));
    }

    /// <summary>USB descriptor serials are optional evidence and never canonical instrument identity.</summary>
    /// <returns>Completion of USB descriptor translation checks.</returns>
    [Fact]
    public async Task UsbDescriptorSerialIsEvidenceOnly()
    {
        var provider = new NativeVisaDiscoveryProvider("USB", enumerate: (_, _) =>
            ["USB0::0x1234::0x5678::ABC123::INSTR"]);
        var endpoint = Assert.Single((await provider.DiscoverAsync()).Endpoints);
        Assert.Equal("ABC123", endpoint.Identity!.Serial);
        Assert.Null(endpoint.ResourceId);
        Assert.Null(endpoint.Identity.Model);
    }

    /// <summary>Missing native libraries are reported differently from an empty inventory.</summary>
    /// <returns>Completion of provider diagnostic checks.</returns>
    [Fact]
    public async Task MissingRuntimeHasActionableDiagnostic()
    {
        var provider = new NativeVisaDiscoveryProvider("USB", enumerate: (_, _) => throw new DllNotFoundException("missing"));
        var result = await provider.DiscoverAsync();
        Assert.Empty(result.Endpoints);
        Assert.Contains(result.Diagnostics, x => x.Contains("Install a compatible vendor VISA runtime"));
    }

    /// <summary>Ethernet enumeration is restricted to one explicit host and excludes catalog entries outside it.</summary>
    /// <returns>Completion of Ethernet scope checks.</returns>
    [Fact]
    public async Task EthernetScopeIsExplicitAndBounded()
    {
        Assert.Throws<ArgumentException>(() => new NativeVisaDiscoveryProvider("Ethernet"));
        Assert.Throws<ArgumentException>(() => new NativeVisaDiscoveryProvider("Ethernet", "*"));
        var provider = new NativeVisaDiscoveryProvider("Ethernet", "scope.lab", (expression, _) =>
        {
            Assert.Equal("TCPIP[0-9]*::scope.lab::?*INSTR", expression);
            return ["TCPIP0::scope.lab::inst0::INSTR", "TCPIP0::other.lab::inst0::INSTR"];
        });
        Assert.Single((await provider.DiscoverAsync()).Endpoints);
    }

    /// <summary>Cancellation is propagated rather than reported as an empty successful scan.</summary>
    /// <returns>Completion of cancellation checks.</returns>
    [Fact]
    public async Task CancellationDoesNotInvokeCatalog()
    {
        var invoked = false;
        var provider = new NativeVisaDiscoveryProvider("USB", enumerate: (_, _) => { invoked = true; return []; });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.DiscoverAsync(cancellation.Token));
        Assert.False(invoked);
    }
}
