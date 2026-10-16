using System.Text.Json;
using BenchDouble.Host.Server;
using BenchDouble.Host.Server.Setup;
using BenchDouble.Ivi.Official;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Reviewed IVI bindings, persistence, refusal of activation and payload-free logs.</summary>
public sealed class IviSetupBindingTests
{
    /// <summary>Two buses and an explicitly associated LAN route survive save/reload without serial numbers.</summary>
    [Fact]
    public void Reviewed_bindings_roundtrip_and_never_activate()
    {
        using var f = new Fixture();
        var catalog = f.Catalogs.Inspect("private XML");
        var first = f.Catalogs.Apply(new(Empty(), catalog.Revision, "A", "MeterA", "voltmeter", true));
        var second = f.Catalogs.Apply(new(first, catalog.Revision, "B", "MeterB", "reference", true));
        var both = f.Catalogs.Apply(new(second, catalog.Revision, "LAN", "MeterA", null, true));
        Assert.Equal(2, both.SchemaVersion);
        Assert.Equal(2, both.Controllers.Length);
        Assert.Equal(2, both.Instruments.Length);
        Assert.Equal(2, both.Instruments[0].Connections.Length);
        Assert.True(both.Instruments[0].AssociationConfirmed);
        Assert.Equal("22", both.Instruments[1].Connections[0].Address);
        Assert.Equal("TCPIP0::meter.lab::hislip0::INSTR", both.Instruments[0].Connections[1].Address);
        Assert.Single(first.Instruments); // Input snapshot was not mutated.
        var active = f.Registry.Reserve("DMM1")!;
        var before = f.Registry.Monitor().Instruments.Sum(i => i.Activity.Count);
        Assert.Contains(f.Setup.Validate(both), p => p.Code == "driver_activation_unavailable");
        Assert.Throws<InvalidOperationException>(() => f.Setup.Save(new(both, null, false)));
        var saved = f.Setup.Save(new(both, null, true));
        var read = f.Setup.Read()!;
        Assert.Equal(saved.Revision, read.Revision);
        var binding = read.Configuration.Instruments[0].Connections[0].DriverBinding!;
        Assert.Equal(catalog.Revision, binding.SourceRevision);
        Assert.Equal("SECRET-OPTION", binding.Settings["DriverSetup"]);
        Assert.Equal("channel-one", binding.VirtualNames["sense"]);
        Assert.Throws<InvalidOperationException>(() => f.Setup.ReserveRole("voltmeter"));
        Assert.Equal(before, f.Registry.Monitor().Instruments.Sum(i => i.Activity.Count));
        Assert.Null(f.Registry.Reserve("DMM1"));
        Assert.Contains("5", f.Registry.Query("DMM1", active.LeaseId, "MEAS:VOLT:DC?"));
        Assert.True(f.Registry.Release(active.LeaseId));
        Assert.Throws<InvalidOperationException>(() => f.Setup.Save(new(both, "stale", true)));
        Assert.Equal(saved.Revision, f.Setup.Read()!.Revision);
        Assert.Contains(f.SetupLog.Events, e => e.Id == 7422);
        Assert.Contains(f.SetupLog.Events, e => e.Id == 7423);
        Assert.Contains(f.CatalogLog.Events, e => e.Id == 7410);
        var logs = string.Join("\n", f.SetupLog.Events.Concat(f.CatalogLog.Events).Select(e => e.Message));
        Assert.DoesNotContain("SECRET-OPTION", logs);
        Assert.DoesNotContain("private XML", logs);
        Assert.DoesNotContain("meter.lab", logs);
        Assert.DoesNotContain("Vendor.Driver", logs);
    }

    /// <summary>No implicit confirmation, endpoint reassignment, stale catalog use or role overwrite.</summary>
    [Fact]
    public void Conflicts_and_stale_catalogs_leave_drafts_unchanged()
    {
        using var f = new Fixture();
        var c = f.Catalogs.Inspect("input");
        Assert.Throws<InvalidOperationException>(() => f.Catalogs.Apply(new(Empty(), c.Revision, "A", "A", null, false)));
        var draft = f.Catalogs.Apply(new(Empty(), c.Revision, "A", "A", "meter", true));
        var original = JsonSerializer.Serialize(draft);
        Assert.Throws<ArgumentException>(() => f.Catalogs.Apply(new(draft, c.Revision, "A", "Other", null, true)));
        Assert.Throws<InvalidOperationException>(() => f.Catalogs.Apply(new(draft, c.Revision, "B", "B", "meter", true)));
        Assert.Equal(original, JsonSerializer.Serialize(draft));
        var aliases = f.Catalogs.Apply(new(draft, c.Revision, "A", "A", "monitor", true));
        Assert.Single(aliases.Instruments[0].Connections);
        Assert.Equal(2, aliases.Roles.Length);
        f.Next = Catalog('b');
        f.Catalogs.Inspect("new input");
        Assert.Throws<InvalidOperationException>(() => f.Catalogs.Apply(new(draft, c.Revision, "A", "A", null, true)));
        Assert.Contains(f.CatalogLog.Events, e => e.Id == 7411);
    }

    /// <summary>Schema one remains readable; bindings require schema two and strict request fields.</summary>
    [Fact]
    public void Schema_and_import_requests_are_strict()
    {
        using var f = new Fixture();
        var c = f.Catalogs.Inspect("input");
        var draft = f.Catalogs.Apply(new(Empty(), c.Revision, "A", "A", null, true));
        Assert.Throws<ArgumentException>(() => BenchSetupService.Parse(JsonSerializer.Serialize(draft with { SchemaVersion = 1 })));
        Assert.Equal(1, BenchSetupService.Parse(JsonSerializer.Serialize(Empty())).SchemaVersion);
        Assert.Throws<JsonException>(() => BenchSetupService.ParseApplyIviBindingRequest("{\"confirmed\":true,\"confirmed\":false}"));
        Assert.Throws<JsonException>(() => BenchSetupService.ParseApplyIviBindingRequest("{\"executeDriver\":true}"));
        var broken = draft.Instruments[0].Connections[0].DriverBinding! with { Settings = null! };
        var route = draft.Instruments[0].Connections[0] with { DriverBinding = broken };
        var invalid = draft with { Instruments = [draft.Instruments[0] with { Connections = [route] }] };
        Assert.Throws<ArgumentException>(() => BenchSetupService.Parse(JsonSerializer.Serialize(invalid)));
    }

    /// <summary>Inspection errors are logged by category without including exception payloads.</summary>
    [Fact]
    public void Inspection_failure_logs_do_not_echo_input_or_exception_message()
    {
        var log = new Capture<IviSetupCatalogService>();
        var service = new IviSetupCatalogService(log, (_, _) => throw new ArgumentException("SECRET"));
        Assert.Throws<ArgumentException>(() => service.Inspect("PRIVATE"));
        Assert.Contains(log.Events, e => e.Id == 7402 && e.Message.Contains("ArgumentException"));
        Assert.All(log.Events, e => { Assert.DoesNotContain("SECRET", e.Message); Assert.DoesNotContain("PRIVATE", e.Message); });
    }

    private static BenchSetup Empty() => new(1, [], [], []);
    private static IviConfigurationCatalog Catalog(char revision = 'a') => new(new string(revision, 64),
        [new("A", "sa"), new("B", "sb"), new("LAN", "sl")],
        new[] { ("sa", "ha"), ("sb", "hb"), ("sl", "hl") }.Select(x => new IviSessionBinding(x.Item1, x.Item2, "module",
            new Dictionary<string, string> { ["DriverSetup"] = "SECRET-OPTION", ["Simulate"] = "False" },
            new Dictionary<string, string> { ["sense"] = "channel-one" })).ToArray(),
        [new("ha", "GPIB0::22::INSTR"), new("hb", "GPIB1::22::INSTR"), new("hl", "TCPIP0::meter.lab::hislip0::INSTR")],
        [new("module", "IviNetSoftwareModule", "Vendor.Driver, VendorAssembly")], []);

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "ivi-binding-" + Guid.NewGuid() + ".json");
        public readonly HostResourceRegistry Registry = new();
        public readonly Capture<BenchSetupService> SetupLog = new();
        public readonly Capture<IviSetupCatalogService> CatalogLog = new();
        public IviConfigurationCatalog Next = Catalog();
        public readonly BenchSetupService Setup;
        public readonly IviSetupCatalogService Catalogs;
        public Fixture()
        {
            Setup = new(Registry, new SimulatedSetupDiscoveryProvider(Registry), _path, SetupLog);
            Catalogs = new(CatalogLog, (_, _) => Next);
        }
        public void Dispose() => File.Delete(_path);
    }

    private sealed class Capture<T> : ILogger<T>
    {
        public readonly List<(int Id, string Message)> Events = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Events.Add((eventId.Id, formatter(state, exception)));
    }
}
