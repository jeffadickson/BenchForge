using BenchDouble.Ivi.Official;
using Ivi.ConfigServer;
using Xunit;

namespace BenchDouble.Ivi.Official.Tests;

/// <summary>Official metadata projection and platform-specific XML loading checks.</summary>
public sealed class IviConfigurationCatalogTests
{
    /// <summary>Runs the actual official loader on Windows; explicitly skipped elsewhere.</summary>
    [WindowsFact]
    public void Official_store_roundtrip_preserves_routes_settings_and_aliases_without_loading_driver()
    {
        var xml = Fixture();
        var result = IviConfigurationCatalogReader.ReadXml(xml);
        Assert.Equal(3, result.HardwareAssets.Length);
        Assert.Contains(result.HardwareAssets, a => a.ResourceDescriptor == "GPIB0::22::INSTR");
        Assert.Contains(result.HardwareAssets, a => a.ResourceDescriptor == "GPIB1::22::INSTR");
        Assert.Equal(2, result.LogicalNames.Count(n => n.Session == "SessionA"));
        Assert.Equal("True", result.Sessions.Single(s => s.Name == "SessionA").Settings["Simulate"]);
        Assert.Equal("Channel1", result.Sessions[0].VirtualNames["sense"]);
        Assert.Equal("Vendor.DoesNotExist, Assembly.NotInstalled", result.SoftwareModules[0].AssemblyQualifiedClassName);
        Assert.Equal(result.Revision, IviConfigurationCatalogReader.ReadXml(xml).Revision);
        Assert.NotEqual(result.Revision, IviConfigurationCatalogReader.ReadXml(xml + "\n").Revision);
    }

    /// <summary>Unresolved metadata never substitutes another device.</summary>
    [Fact]
    public void Missing_references_are_diagnosed_without_substitution()
    {
        var session = new DriverSession("Unbound");
        var result = IviConfigurationCatalogReader.Project("snapshot", [new LogicalName("meter", session)], [session], [], []);
        Assert.Null(result.Sessions[0].HardwareAsset);
        Assert.Contains(result.Diagnostics, d => d.Contains("unresolved"));
    }

    /// <summary>Rejects invalid XML and external entities before official loading.</summary>
    /// <param name="xml">Untrusted input.</param>
    [Theory]
    [InlineData("<not-an-ivi-store />")]
    [InlineData("<broken>")]
    [InlineData("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///etc/passwd'>]><x>&a;</x>")]
    public void Malformed_or_external_entity_documents_are_rejected(string xml) =>
        Assert.Throws<ArgumentException>(() => IviConfigurationCatalogReader.ReadXml(xml));

    /// <summary>Bounds input and honors cancellation before parsing.</summary>
    [Fact]
    public void Size_and_cancellation_are_bounded()
    {
        Assert.Throws<ArgumentException>(() => IviConfigurationCatalogReader.ReadXml(new string('x', IviConfigurationCatalogReader.MaximumBytes + 1)));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => IviConfigurationCatalogReader.ReadXml("<IviConfigStore />", cancel.Token));
    }

    /// <summary>Projects actual official metadata objects without registry access or vendor assemblies.</summary>
    [Fact]
    public void Portable_metadata_keeps_controller_scopes_and_aliases()
    {
        var a = new HardwareAsset("A", "GPIB0::22::INSTR");
        var b = new HardwareAsset("B", "GPIB1::22::INSTR");
        var module = new IviNetSoftwareModule("driver", "Vendor.DoesNotExist, MissingAssembly", "D");
        var session = new DriverSession("session", a, module, new VirtualName("sense", "Channel1")) { Simulate = true };
        var catalog = IviConfigurationCatalogReader.Project("snapshot", [new LogicalName("meter", session), new LogicalName("monitor", session)], [session], [a, b], [module]);
        Assert.Equal(2, catalog.HardwareAssets.Length);
        Assert.Equal("GPIB1::22::INSTR", catalog.HardwareAssets[1].ResourceDescriptor);
        Assert.All(catalog.LogicalNames, n => Assert.Equal("session", n.Session));
        Assert.Equal("True", catalog.Sessions[0].Settings["Simulate"]);
        Assert.Equal("Channel1", catalog.Sessions[0].VirtualNames["sense"]);
        Assert.Equal("Vendor.DoesNotExist, MissingAssembly", catalog.SoftwareModules[0].AssemblyQualifiedClassName);
        session.Simulate = false;
        Assert.Equal("True", catalog.Sessions[0].Settings["Simulate"]);
    }

    /// <summary>Unavailable platforms report a provider limitation, not a corrupt file.</summary>
    [Fact]
    public void Non_windows_reports_provider_limitation()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Contains("Windows", Assert.Throws<PlatformNotSupportedException>(() =>
            IviConfigurationCatalogReader.ReadXml("<IviConfigStore />")).Message);
    }

    internal static string Fixture()
    {
        var module = new IviNetSoftwareModule("MeterDriver", "Vendor.DoesNotExist, Assembly.NotInstalled", "Meter");
        var a = new HardwareAsset("RackA", "GPIB0::22::INSTR");
        var b = new HardwareAsset("RackB", "GPIB1::22::INSTR");
        var ethernet = new HardwareAsset("LAN", "TCPIP0::meter.lab::inst0::INSTR");
        var session = new DriverSession("SessionA", a, module, new VirtualName("sense", "Channel1")) { Simulate = true };
        return Serialize(new ConfigStore(a, b, ethernet, module, session,
            new DriverSession("SessionB", b, module), new DriverSession("SessionLAN", ethernet, module),
            new LogicalName("voltmeter", session), new LogicalName("monitor", session)));
    }

    private static string Serialize(ConfigStore store)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        try { store.Save(path); return File.ReadAllText(path); }
        finally { File.Delete(path); }
    }
}

/// <summary>Marks actual ConfigServer file-loader tests as Windows-only.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    /// <summary>Reports an explicit skip when the official loader cannot run.</summary>
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Official ConfigServer 2.1.0 requires Windows registry support.";
    }
}
