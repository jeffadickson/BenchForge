using System.Text.Json;
using System.Text.RegularExpressions;
using BenchDouble.Ivi.Official;

namespace BenchDouble.Host.Server.Setup;

/// <summary>Retains one inspected catalog and creates reviewed draft bindings without instrument I/O.</summary>
/// <param name="logger">Structured setup events; payloads and names are deliberately excluded.</param>
/// <param name="reader">Optional catalog provider for embedding and hardware-free tests.</param>
public sealed class IviSetupCatalogService(ILogger<IviSetupCatalogService> logger,
    Func<string, CancellationToken, IviConfigurationCatalog>? reader = null)
{
    private readonly ILogger<IviSetupCatalogService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private long _loggingFailures;
    /// <summary>Diagnostic sink failures counted without changing catalog or draft outcomes.</summary>
    public long LoggingFailureCount => Interlocked.Read(ref _loggingFailures);
    private void Emit(Action action)
    {
        try { action(); } catch { Interlocked.Increment(ref _loggingFailures); }
    }
    private readonly object _gate = new();
    private IviConfigurationCatalog? _catalog;

    /// <summary>Inspect XML and replace the current catalog only after successful complete parsing.</summary>
    /// <param name="xml">Bounded XML snapshot, never written to application logs.</param>
    /// <param name="ct">Cancellation; synchronous vendor parsing cannot be interrupted.</param>
    /// <returns>Detached catalog metadata.</returns>
    public IviConfigurationCatalog Inspect(string xml, CancellationToken ct = default)
    {
        lock (_gate)
        {
            Emit(() => _logger.LogInformation(new EventId(7400, "IviInspectionStarted"), "IVI configuration inspection started"));
            try
            {
                var catalog = (reader ?? IviConfigurationCatalogReader.ReadXml)(xml, ct);
                ct.ThrowIfCancellationRequested();
                _catalog = Copy(catalog);
                Emit(() => _logger.LogInformation(new EventId(7401, "IviInspectionCompleted"),
                    "IVI inspection completed with {LogicalNameCount} logical names and {SessionCount} sessions; no driver opened",
                    catalog.LogicalNames.Length, catalog.Sessions.Length));
                return Copy(catalog);
            }
            catch (Exception ex)
            {
                Emit(() => _logger.LogWarning(new EventId(7402, "IviInspectionFailed"), "IVI inspection failed: {FailureKind}", ex.GetType().Name));
                throw;
            }
        }
    }

    /// <summary>Apply one reviewed IVI binding to a detached draft, upgrading it to schema two.</summary>
    /// <param name="request">Selected source, target instrument, optional role and explicit confirmation.</param>
    /// <returns>New draft; saving remains a separate revision-checked operation.</returns>
    /// <exception cref="ArgumentException">The target, descriptor or resulting configuration is invalid.</exception>
    /// <exception cref="InvalidOperationException">Review is absent, catalog stale, or mapping conflicts.</exception>
    public BenchSetup Apply(ApplyIviBindingRequest request)
    {
        lock (_gate)
        {
            try
            {
                var result = ApplyCore(request);
                Emit(() => _logger.LogInformation(new EventId(7410, "IviBindingApplied"),
                    "Reviewed IVI binding applied to draft with {InstrumentCount} instruments; activation remains disabled", result.Instruments.Length));
                return result;
            }
            catch (Exception ex)
            {
                Emit(() => _logger.LogWarning(new EventId(7411, "IviBindingRejected"), "IVI draft binding rejected: {FailureKind}", ex.GetType().Name));
                throw;
            }
        }
    }

    private BenchSetup ApplyCore(ApplyIviBindingRequest request)
    {
        if (!request.Confirmed) throw new InvalidOperationException("Review and confirm the IVI driver, route and target instrument first.");
        if (_catalog is null || _catalog.Revision != request.CatalogRevision)
            throw new InvalidOperationException("IVI catalog changed or is no longer available. Inspect and review it again.");
        var config = BenchSetupService.Parse(JsonSerializer.Serialize(request.Configuration));
        var logical = _catalog.LogicalNames.SingleOrDefault(x => x.Name == request.LogicalName)
            ?? throw new InvalidOperationException("The selected logical name is not in the inspected catalog.");
        var session = _catalog.Sessions.SingleOrDefault(x => x.Name == logical.Session)
            ?? throw new InvalidOperationException("The logical name has no supported driver session.");
        var asset = _catalog.HardwareAssets.SingleOrDefault(x => x.Name == session.HardwareAsset)
            ?? throw new InvalidOperationException("The driver session has no resolved hardware asset.");
        var module = _catalog.SoftwareModules.SingleOrDefault(x => x.Name == session.SoftwareModule)
            ?? throw new InvalidOperationException("The driver session has no resolved software module.");
        var controllers = config.Controllers.ToList();
        string transport, address;
        string? controllerId = null;
        var gpib = Regex.Match(asset.ResourceDescriptor, @"\AGPIB([0-9]+)::([0-9]+)::INSTR\z", RegexOptions.IgnoreCase);
        if (gpib.Success)
        {
            if (!int.TryParse(gpib.Groups[1].Value, out _) || !int.TryParse(gpib.Groups[2].Value, out var primary) || primary > 30)
                throw new ArgumentException("GPIB controller or primary address is out of range.");
            transport = "GPIB";
            address = int.Parse(gpib.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var controllerAddress = "GPIB" + int.Parse(gpib.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var controller = controllers.SingleOrDefault(x => Same(x.Provider, "visa") && Same(x.Address, controllerAddress));
            if (controller is null)
            {
                controllerId = Unique("ivi-adapter", controllers.Select(x => x.Id));
                controllers.Add(new(controllerId, "visa", controllerAddress));
            }
            else controllerId = controller.Id;
        }
        else
        {
            // Keep complete non-GPIB descriptors opaque: do not drop ports, protocol,
            // secondary addresses, USB identity or vendor-specific resource syntax.
            transport = "VISA";
            address = asset.ResourceDescriptor;
        }
        var binding = new SetupDriverBinding(_catalog.Revision, logical.Name, session.Name, module,
            asset.ResourceDescriptor, new(session.Settings), new(session.VirtualNames));
        var instruments = config.Instruments.ToList();
        var index = instruments.FindIndex(x => Same(x.Id, request.InstrumentId));
        var target = index < 0 ? new SetupInstrument(request.InstrumentId, request.InstrumentId, "", []) : instruments[index];
        var routes = target.Connections.ToList();
        var existing = routes.FindIndex(x => Same(x.Provider, "visa") && Same(x.Transport, transport) &&
            Same(x.Address, address) && Same(x.ControllerId, controllerId));
        if (existing >= 0)
        {
            if (routes[existing].DriverBinding is { } previous &&
                (previous.SourceRevision != binding.SourceRevision || previous.Session != binding.Session))
                throw new InvalidOperationException("The route has another driver binding. Remove it explicitly before importing a replacement.");
            // Multiple logical names may map to the same session/route. Keep its first
            // provenance binding; a second experiment role does not create another route.
            routes[existing] = routes[existing] with { DriverBinding = routes[existing].DriverBinding ?? binding };
        }
        else routes.Add(new(Unique("ivi-route", routes.Select(x => x.Id)), "visa", transport, address, controllerId, binding));
        target = target with { Connections = routes.ToArray(), SelectedConnection = target.SelectedConnection.Length == 0 ? routes[0].Id : target.SelectedConnection,
            AssociationConfirmed = routes.Count > 1 || target.AssociationConfirmed };
        if (index < 0) instruments.Add(target); else instruments[index] = target;
        var roles = config.Roles.ToList();
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var role = roles.SingleOrDefault(x => Same(x.Role, request.Role));
            if (role is not null && !Same(role.InstrumentId, target.Id)) throw new InvalidOperationException("That role already refers to another instrument.");
            if (role is null) roles.Add(new(request.Role, target.Id));
        }
        return BenchSetupService.Parse(JsonSerializer.Serialize(new BenchSetup(2, controllers.ToArray(), instruments.ToArray(), roles.ToArray())));
    }

    private static string Unique(string prefix, IEnumerable<string> values)
    {
        var used = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++) if (!used.Contains(prefix + i)) return prefix + i;
    }
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
