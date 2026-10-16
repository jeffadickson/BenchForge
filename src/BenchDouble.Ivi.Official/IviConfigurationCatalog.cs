using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Ivi.ConfigServer;

[assembly: InternalsVisibleTo("BenchDouble.Ivi.Official.Tests")]

namespace BenchDouble.Ivi.Official;

/// <summary>Detached read-only configuration evidence; never a driver instance or reservation.</summary>
/// <param name="Revision">SHA-256 of the submitted XML text encoded as UTF-8.</param>
/// <param name="LogicalNames">Logical names and their referenced sessions.</param>
/// <param name="Sessions">Configured driver sessions, including unreferenced sessions.</param>
/// <param name="HardwareAssets">Named resource descriptors, without inferred physical identity.</param>
/// <param name="SoftwareModules">Installed-module declarations; installation is not verified.</param>
/// <param name="Diagnostics">Limitations and unresolved reference diagnostics.</param>
public sealed record IviConfigurationCatalog(string Revision, IviLogicalBinding[] LogicalNames,
    IviSessionBinding[] Sessions, IviHardwareBinding[] HardwareAssets,
    IviModuleBinding[] SoftwareModules, string[] Diagnostics);

/// <summary>Logical name resolution without opening a session.</summary>
/// <param name="Name">Original IVI logical name.</param>
/// <param name="Session">Referenced configuration session name, or null when unresolved.</param>
public sealed record IviLogicalBinding(string Name, string? Session);

/// <summary>Declared hardware route; not proof of presence or unique physical identity.</summary>
/// <param name="Name">Original hardware asset name.</param>
/// <param name="ResourceDescriptor">Unmodified provider I/O descriptor.</param>
public sealed record IviHardwareBinding(string Name, string ResourceDescriptor);

/// <summary>Module metadata only; no assembly loading or driver construction is performed.</summary>
/// <param name="Name">Original module name.</param>
/// <param name="Kind">Official configuration object type.</param>
/// <param name="AssemblyQualifiedClassName">Declared .NET class, if applicable; never resolved to a Type.</param>
public sealed record IviModuleBinding(string Name, string Kind, string? AssemblyQualifiedClassName);

/// <summary>Configured driver settings for review only; none are applied to hardware.</summary>
/// <param name="Name">Original configured session name.</param>
/// <param name="HardwareAsset">Referenced asset, or null when missing.</param>
/// <param name="SoftwareModule">Referenced module, or null when missing.</param>
/// <param name="Settings">Standard initialization settings, including opaque DriverSetup text.</param>
/// <param name="VirtualNames">Virtual names and physical mappings; ranges remain in the original XML.</param>
public sealed record IviSessionBinding(string Name, string? HardwareAsset, string? SoftwareModule,
    IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> VirtualNames);

/// <summary>Reads IVI XML through the official ConfigServer API without using driver factories or instrument I/O.</summary>
public static class IviConfigurationCatalogReader
{
    /// <summary>Maximum submitted UTF-8 XML size in bytes (two mebibytes).</summary>
    public const int MaximumBytes = 2 * 1024 * 1024;

    /// <summary>Inspect a supplied XML snapshot. No external store is located, saved or modified.</summary>
    /// <param name="xml">IVI configuration XML; DTDs and external entity resolution are prohibited.</param>
    /// <param name="cancellationToken">Checked before parsing and before publishing; cannot interrupt the official synchronous loader.</param>
    /// <returns>Detached metadata and diagnostics, with no usable driver or lease.</returns>
    /// <exception cref="ArgumentException">Input exceeds the bound or cannot be read as IVI configuration.</exception>
    /// <exception cref="PlatformNotSupportedException">The official ConfigServer loader requires Windows.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested; no catalog is returned.</exception>
    public static IviConfigurationCatalog ReadXml(string xml, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xml);
        cancellationToken.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(xml) > MaximumBytes)
            throw new ArgumentException("IVI configuration exceeds the 2 MiB limit.", nameof(xml));
        var path = Path.Combine(Path.GetTempPath(), "benchforge-ivi-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes
            });
            var document = XDocument.Load(reader);
            if (document.Root?.Name != XName.Get("IviConfigStore"))
                throw new ArgumentException("Expected an IVI configuration store.");
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("IVI ConfigServer 2.1.0 requires Windows registry support. Inspect this file on a Windows BenchForge host; simulation and manual setup remain available.");
            // Re-serialize vetted XML to a private snapshot. The official API accepts paths,
            // so never pass a user-supplied server path or the original external store.
            using (var snapshot = new FileStream(path, new FileStreamOptions
            { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None }))
                document.Save(snapshot);
            cancellationToken.ThrowIfCancellationRequested();
            var store = ConfigStore.LoadWithoutValidation(path);
            return Project(xml, store.LogicalNames, store.DriverSessions, store.HardwareAssets, store.SoftwareModules, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (PlatformNotSupportedException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Vendor parser messages can include configuration values and temporary paths.
            throw new ArgumentException("Cannot read IVI configuration. Check the XML format and references; DTDs are not supported.", nameof(xml));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // Separate pure projection so the official metadata objects can be tested without
    // Windows registry access or driver construction. This is not a substitute XML parser.
    internal static IviConfigurationCatalog Project(string xml, IEnumerable<LogicalName> logicalNames,
        IEnumerable<DriverSession> driverSessions, IEnumerable<HardwareAsset> hardwareAssets,
        IEnumerable<SoftwareModule> softwareModules, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>
        {
            "Read-only catalog, not schema validation or hardware validation. No drivers were instantiated.",
            "Custom data, virtual ranges and other extensions remain in your source XML; this catalog is not a lossless export or an activation configuration."
        };
        var logical = logicalNames.Select(n => new IviLogicalBinding(n.Name, n.Session?.Name)).ToArray();
        foreach (var name in logical.Where(n => n.Session is null))
            diagnostics.Add($"Logical name '{name.Name}' has no resolved session.");
        var sessions = driverSessions.Select(s =>
        {
            if (s.HardwareAsset is null || s.SoftwareModule is null)
                diagnostics.Add($"Session '{s.Name}' has an unresolved hardware asset or software module.");
            return new IviSessionBinding(s.Name, s.HardwareAsset?.Name, s.SoftwareModule?.Name,
                new Dictionary<string, string>
                {
                    ["Cache"] = s.Cache.ToString(), ["DriverSetup"] = s.DriverSetup,
                    ["InterchangeCheck"] = s.InterchangeCheck.ToString(),
                    ["QueryInstrStatus"] = s.QueryInstrStatus.ToString(),
                    ["RangeCheck"] = s.RangeCheck.ToString(),
                    ["RecordCoercions"] = s.RecordCoercions.ToString(), ["Simulate"] = s.Simulate.ToString()
                }, s.VirtualNames.ToDictionary(n => n.Name, n => n.MapTo));
        }).ToArray();
        foreach (var name in logical.Where(n => n.Session is not null && !sessions.Any(s => s.Name == n.Session)))
            diagnostics.Add($"Logical name '{name.Name}' refers to a non-driver session; activation is unsupported.");
        var assets = hardwareAssets.Select(a => new IviHardwareBinding(a.Name, a.IOResourceDescriptor)).ToArray();
        var modules = softwareModules.Select(m => new IviModuleBinding(m.Name, m.GetType().Name,
            (m as IviNetSoftwareModule)?.AssemblyQualifiedClassName)).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))), logical,
            sessions, assets, modules, diagnostics.ToArray());
    }
}
