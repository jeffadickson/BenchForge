using System.Text.Json;
using BenchDouble.Host.Server.Setup;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Exercises discovery-assisted configuration with real host ownership and no attached hardware.</summary>
public sealed class BenchSetupTests
{
    /// <summary>No serials are needed for roundtrip persistence or two independent GPIB buses.</summary>
    /// <returns>Completion of discovery, confirmation, validation and persistence checks.</returns>
    [Fact]
    public async Task NoSerialSetupSupportsTwoBusesAndRoundTrips()
    {
        using var fixture = new Fixture();
        var discovered = await fixture.Setup.DiscoverAsync();
        var config = Configuration(discovered.Id);
        Assert.Empty(fixture.Setup.Validate(config));
        var saved = fixture.Setup.Save(new(config, null, false));
        Assert.False(saved.Draft);
        Assert.Equal(saved.Revision, fixture.Setup.Read()!.Revision);
        var a = fixture.Setup.ReserveRole("voltmeter")!;
        var b = fixture.Setup.ReserveRole("generator")!;
        Assert.NotEqual(a.ResourceId, b.ResourceId);
        Assert.Equal("22", b.Connection.Address);
        Assert.True(fixture.Registry.Release(a.LeaseId));
        Assert.True(fixture.Registry.Release(b.LeaseId));
        config.Instruments[0] = config.Instruments[0] with { Name = "mutated" };
        Assert.Equal("Bench meter", fixture.Setup.Read()!.Configuration.Instruments[0].Name);
    }

    /// <summary>Alternate routes require confirmation and share the normal canonical lease.</summary>
    /// <returns>Completion of alias and contention checks.</returns>
    [Fact]
    public async Task AlternateRoutesShareCanonicalOwnership()
    {
        using var f = new Fixture();
        var d = await f.Setup.DiscoverAsync();
        var config = Configuration(d.Id);
        var unconfirmed = config with { Instruments = [config.Instruments[0] with { AssociationConfirmed = false }] , Roles = [config.Roles[0]] };
        Assert.Contains(f.Setup.Validate(unconfirmed), x => x.Message.Contains("Confirm"));
        f.Setup.Save(new(config, null, false));
        var lease = f.Setup.ReserveRole("voltmeter")!;
        Assert.Null(f.Registry.Reserve("DMM1"));
        Assert.Null(f.Setup.ReserveRole("voltmeter"));
        Assert.Equal("DMM1", lease.ResourceId);
        Assert.Contains("5", f.Registry.Query("DMM1", lease.LeaseId, "MEAS:VOLT:DC?"));
        f.Registry.Release(lease.LeaseId);
        config.Instruments[0] = config.Instruments[0] with { SelectedConnection = "gpib" };
        f.Setup.Save(new(config, f.Setup.Read()!.Revision, false));
        var gpib = f.Setup.ReserveRole("voltmeter")!;
        Assert.Equal("gpib", gpib.Connection.Id);
        Assert.Null(f.Registry.Reserve("DMM1"));
        f.Registry.Release(gpib.LeaseId);
    }

    /// <summary>Discovery and attempted validation do not query instruments reserved by another client.</summary>
    /// <returns>Completion of busy-instrument checks.</returns>
    [Fact]
    public async Task BusyInstrumentsAreNotProbed()
    {
        using var f = new Fixture();
        var lease = f.Registry.Reserve("DMM1")!;
        var before = Queries(f.Registry, "DMM1");
        var d = await f.Setup.DiscoverAsync();
        Assert.All(d.Endpoints.Where(x => x.ResourceId == "DMM1"), x => Assert.True(x.Busy));
        Assert.Contains(f.Setup.Validate(Configuration(d.Id)), x => x.Code == "busy");
        Assert.Equal(before, Queries(f.Registry, "DMM1"));
        f.Registry.Release(lease.LeaseId);
    }

    /// <summary>Optional expected identity mismatch rejects validation and releases its temporary lease.</summary>
    /// <returns>Completion of optional identity checks.</returns>
    [Fact]
    public async Task OptionalIdentityMismatchDoesNotSubstituteOrLeakLease()
    {
        using var f = new Fixture();
        var d = await f.Setup.DiscoverAsync();
        var config = Configuration(d.Id);
        config.Instruments[0] = config.Instruments[0] with { ExpectedIdentity = new(Serial: "not-the-serial") };
        Assert.Contains(f.Setup.Validate(config), x => x.Code == "identity_mismatch");
        var lease = f.Registry.Reserve("DMM1");
        Assert.NotNull(lease);
        f.Registry.Release(lease!.LeaseId);
    }

    /// <summary>Unknown physical providers can be saved offline but cannot activate instrument control.</summary>
    [Fact]
    public void OfflineDraftRemainsExplicitlyUntested()
    {
        using var f = new Fixture();
        var config = new BenchSetup(1, [], [new("MY-DMM", "My meter", "lan",
            [new("lan", "visa", "Ethernet", "192.0.2.10")])], [new("voltmeter", "MY-DMM")]);
        var saved = f.Setup.Save(new(config, null, true));
        Assert.True(saved.Draft);
        Assert.Throws<InvalidOperationException>(() => f.Setup.ReserveRole("voltmeter"));
        Assert.Throws<InvalidOperationException>(() => f.Setup.Save(new(config, saved.Revision, false)));
        Assert.Equal(saved.Revision, f.Setup.Read()!.Revision);
    }

    /// <summary>Editing is optimistic and returned snapshots cannot mutate saved configuration.</summary>
    /// <returns>Completion of concurrent-edit and active-lease preservation checks.</returns>
    [Fact]
    public async Task FailedSavePreservesPriorRevisionAndActiveLease()
    {
        using var f = new Fixture();
        var d = await f.Setup.DiscoverAsync();
        var config = Configuration(d.Id);
        var saved = f.Setup.Save(new(config, null, false));
        var lease = f.Setup.ReserveRole("voltmeter")!;
        Assert.Throws<InvalidOperationException>(() => f.Setup.Save(new(config, null, true)));
        Assert.Throws<InvalidOperationException>(() => f.Setup.Save(new(config, saved.Revision, false)));
        Assert.Equal(saved.Revision, f.Setup.Read()!.Revision);
        config.Instruments[0] = config.Instruments[0] with { SelectedConnection = "gpib" };
        var next = f.Setup.Save(new(config, saved.Revision, true));
        Assert.NotEqual(saved.Revision, next.Revision);
        Assert.Equal("lan", lease.Connection.Id);
        Assert.Contains("5", f.Registry.Query(lease.ResourceId, lease.LeaseId, "MEAS:VOLT:DC?"));
        f.Registry.Release(lease.LeaseId);
    }

    /// <summary>Adapters without stable identity must be reconfirmed after a fresh discovery.</summary>
    /// <returns>Completion of operator binding repair checks.</returns>
    [Fact]
    public async Task RediscoveryRequiresConfirmationForUnidentifiedAdapters()
    {
        using var f = new Fixture();
        var d = await f.Setup.DiscoverAsync();
        var config = Configuration(d.Id);
        Assert.Empty(f.Setup.Validate(config));
        await f.Setup.DiscoverAsync();
        Assert.Contains(f.Setup.Validate(config), x => x.Code == "controller_confirmation");
    }

    /// <summary>Provider stable identity can retain a binding after controller re-enumeration.</summary>
    /// <returns>Completion of stable binding checks.</returns>
    [Fact]
    public async Task StableBindingSurvivesChangedControllerIndex()
    {
        using var f = new Fixture();
        var d = await f.Setup.DiscoverAsync();
        var changed = d with { Controllers = [new("simulation", "GPIB5", "rack A", "stable-A"), d.Controllers[1]],
            Endpoints = d.Endpoints.Select(x => x.ControllerAddress == "GPIB0" ? x with { ControllerAddress = "GPIB5" } : x).ToArray() };
        var provider = new FixedDiscovery(changed);
        var setup = new BenchSetupService(f.Registry, provider, f.Path);
        await setup.DiscoverAsync();
        var config = Configuration(d.Id);
        config.Controllers[0] = config.Controllers[0] with { StableId = "stable-A" };
        Assert.Empty(setup.Validate(config));
    }

    /// <summary>Models and serial metadata do not merge independently registered devices.</summary>
    /// <returns>Completion of independent device checks.</returns>
    [Fact]
    public async Task IdenticalModelsRemainSeparate()
    {
        using var f = new Fixture();
        var d = await f.Setup.DiscoverAsync();
        var same = d with { Endpoints = d.Endpoints.Select(x => x with { Identity = new("Same", "Same", "duplicate") }).ToArray() };
        var setup = new BenchSetupService(f.Registry, new FixedDiscovery(same), f.Path);
        var result = await setup.DiscoverAsync();
        Assert.Equal(3, result.Endpoints.Length);
        Assert.Equal(2, result.Endpoints.Select(x => x.ResourceId).Distinct().Count());
    }

    /// <summary>Cancelled discovery does not replace the current confirmed snapshot.</summary>
    /// <returns>Completion of cancellation checks.</returns>
    [Fact]
    public async Task CancelledDiscoveryPreservesSnapshot()
    {
        using var f = new Fixture();
        var d = await f.Setup.DiscoverAsync();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await f.Setup.DiscoverAsync(cancel.Token));
        Assert.Empty(f.Setup.Validate(Configuration(d.Id)));
    }

    /// <summary>Strict JSON rejects duplicate fields instead of silently taking the last value.</summary>
    [Fact]
    public void DuplicateAndUnknownFieldsAreRejected()
    {
        Assert.Throws<JsonException>(() => BenchSetupService.Parse("{\"schemaVersion\":1,\"schemaVersion\":2}"));
        Assert.Throws<JsonException>(() => BenchSetupService.Parse("{\"schemaVersion\":1,\"controllers\":[],\"instruments\":[],\"roles\":[],\"typo\":true}"));
        Assert.Throws<JsonException>(() => BenchSetupService.ParseSaveRequest("{\"draft\":true,\"draft\":false}"));
    }

    /// <summary>Invalid structure is rejected even when saving offline.</summary>
    /// <param name="kind">Mutation applied to a valid document.</param>
    [Theory]
    [InlineData("duplicate-role")]
    [InlineData("duplicate-endpoint")]
    [InlineData("bad-controller")]
    [InlineData("unknown-role-target")]
    [InlineData("bad-selected-route")]
    [InlineData("bad-address")]
    [InlineData("null-connections")]
    public void InvalidDraftCannotReplaceSavedFile(string kind)
    {
        using var f = new Fixture();
        var config = Configuration("test");
        config = kind switch
        {
            "duplicate-role" => config with { Roles = [config.Roles[0], config.Roles[0]] },
            "duplicate-endpoint" => config with { Instruments = [config.Instruments[0], config.Instruments[0] with { Id = "OTHER" }] },
            "unknown-role-target" => config with { Roles = [new("meter", "MISSING")] },
            _ => config
        };
        if (kind == "bad-controller") config.Controllers[0] = config.Controllers[0] with { Id = "renamed" };
        if (kind == "bad-selected-route") config.Instruments[0] = config.Instruments[0] with { SelectedConnection = "missing" };
        if (kind == "bad-address") config.Instruments[0].Connections[1] = config.Instruments[0].Connections[1] with { Address = "31" };
        if (kind == "null-connections") config.Instruments[0] = config.Instruments[0] with { Connections = null! };
        Assert.Throws<ArgumentException>(() => f.Setup.Save(new(config, null, true)));
        Assert.Null(f.Setup.Read());
    }

    private static BenchSetup Configuration(string discoveryId) => new(1,
        [new("rackA", "simulation", "GPIB0", ConfirmedDiscoveryId: discoveryId),
         new("rackB", "simulation", "GPIB1", ConfirmedDiscoveryId: discoveryId)],
        [new("DMM1", "Bench meter", "lan", [new("lan", "simulation", "Ethernet", "dmm1.simulated"),
             new("gpib", "simulation", "GPIB", "22", "rackA")], true),
         new("FGEN1", "Generator", "gpib", [new("gpib", "simulation", "GPIB", "22", "rackB")])],
        [new("voltmeter", "DMM1"), new("generator", "FGEN1")]);

    private static int Queries(HostResourceRegistry registry, string resource) => registry.Monitor().Instruments
        .Single(x => x.ResourceId == resource).Activity.Count(x => x.Action == "Query");

    private sealed class FixedDiscovery(SetupDiscovery snapshot) : ISetupDiscoveryProvider
    {
        public ValueTask<SetupDiscovery> DiscoverAsync(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(snapshot); }
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bench-setup-" + Guid.NewGuid().ToString("N"), "setup.json");
        public HostResourceRegistry Registry { get; } = new();
        public BenchSetupService Setup { get; }
        public Fixture() => Setup = new(Registry, new SimulatedSetupDiscoveryProvider(Registry), Path);
        public void Dispose()
        {
            var directory = System.IO.Path.GetDirectoryName(Path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
