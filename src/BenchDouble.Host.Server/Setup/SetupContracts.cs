using System.Text.Json.Serialization;

namespace BenchDouble.Host.Server.Setup;

/// <summary>Versioned host setup draft; serial numbers are optional throughout.</summary>
/// <param name="SchemaVersion">Schema one for routes only; schema two also supports reviewed IVI bindings.</param>
/// <param name="Controllers">Operator-named controller bindings.</param>
/// <param name="Instruments">Physical instrument identities and alternate routes.</param>
/// <param name="Roles">Experiment role assignments.</param>
public sealed record BenchSetup(int SchemaVersion, SetupController[] Controllers,
    SetupInstrument[] Instruments, SetupRole[] Roles);

/// <summary>A named controller binding confirmed against a discovery snapshot.</summary>
/// <param name="Id">Unique operator-assigned controller identifier.</param>
/// <param name="Provider">Discovery provider identifier.</param>
/// <param name="Address">Provider-local controller address.</param>
/// <param name="StableId">Optional provider-reported stable hardware identity.</param>
/// <param name="ConfirmedDiscoveryId">Explicit confirmation for an adapter without stable identity.</param>
public sealed record SetupController(string Id, string Provider, string Address,
    string? StableId = null, string? ConfirmedDiscoveryId = null);

/// <summary>One physical instrument, with explicitly associated connection routes.</summary>
/// <param name="Id">Canonical host resource identity used by reservations.</param>
/// <param name="Name">Friendly operator-visible name.</param>
/// <param name="SelectedConnection">Connection pinned for subsequent role reservations.</param>
/// <param name="Connections">Named alternate connections.</param>
/// <param name="AssociationConfirmed">Operator confirms multiple routes belong to this instrument.</param>
/// <param name="ExpectedIdentity">Optional identity fields to enforce during validation.</param>
public sealed record SetupInstrument(string Id, string Name, string SelectedConnection,
    SetupConnection[] Connections, bool AssociationConfirmed = false, SetupIdentity? ExpectedIdentity = null);

/// <summary>Provider route to an instrument; controller-scoped addresses need no VISA syntax.</summary>
/// <param name="Id">Connection name unique within the instrument.</param>
/// <param name="Provider">Provider identifier.</param>
/// <param name="Transport">Ethernet, GPIB, USB or other provider transport.</param>
/// <param name="Address">Host address or provider-local device address; GPIB primary address is decimal.</param>
/// <param name="ControllerId">Named controller for a controller-scoped connection.</param>
/// <param name="DriverBinding">Reviewed IVI metadata, supported in schema two; does not activate a driver.</param>
public sealed record SetupConnection(string Id, string Provider, string Transport, string Address,
    string? ControllerId = null, SetupDriverBinding? DriverBinding = null);

/// <summary>Reviewed, partial IVI configuration evidence pinned to a route; not an executable factory request.</summary>
/// <param name="SourceRevision">Digest of the inspected source XML.</param>
/// <param name="LogicalName">Original IVI logical name.</param>
/// <param name="Session">Original configured driver session.</param>
/// <param name="Module">Declared software module metadata, not a loaded driver.</param>
/// <param name="ResourceDescriptor">Original I/O descriptor.</param>
/// <param name="Settings">Standard session options, retained without applying them.</param>
/// <param name="VirtualNames">Retained channel aliases; source XML may contain additional extensions.</param>
public sealed record SetupDriverBinding(string SourceRevision, string LogicalName, string Session,
    BenchDouble.Ivi.Official.IviModuleBinding Module, string ResourceDescriptor,
    Dictionary<string, string> Settings, Dictionary<string, string> VirtualNames);

/// <summary>Explicitly reviewed import into an unsaved setup draft.</summary>
/// <param name="Configuration">Current draft; preserved if import fails.</param>
/// <param name="CatalogRevision">Revision from the most recent successful host inspection.</param>
/// <param name="LogicalName">Selected logical name.</param>
/// <param name="InstrumentId">New or explicitly selected existing canonical instrument.</param>
/// <param name="Role">Optional experiment role; existing conflicting assignments are rejected.</param>
/// <param name="Confirmed">Operator reviewed the driver and route, including any physical association.</param>
public sealed record ApplyIviBindingRequest(BenchSetup Configuration, string CatalogRevision,
    string LogicalName, string InstrumentId, string? Role, bool Confirmed);

/// <summary>Explicit activation of one role from the saved configuration.</summary>
/// <param name="ExpectedRevision">Reviewed saved revision.</param>
/// <param name="Confirmed">Operator requests driver opening, separate from configuration save.</param>
public sealed record ActivateIviRoleRequest([property: JsonRequired] string ExpectedRevision, [property: JsonRequired] bool Confirmed);

/// <summary>Request concerning an already active or failed retained IVI lease.</summary>
/// <param name="LeaseId">Canonical activation lease.</param>
/// <param name="Confirmed">Explicit authorization, required for provider recovery.</param>
public sealed record IviLeaseRequest([property: JsonRequired] Guid LeaseId, bool Confirmed = false);

/// <summary>Cached session inspection with a per-experiment reading freshness budget.</summary>
/// <param name="LeaseId">Existing canonical activation capability.</param>
/// <param name="Confirmed">Legacy inspection field retained for request compatibility; does not authorize I/O.</param>
/// <param name="ReadingFreshnessMilliseconds">Optional observer override from one millisecond through one day; omission uses the saved role policy pinned at activation.</param>
public sealed record IviStatusRequest([property: JsonRequired] Guid LeaseId, bool Confirmed = false, double? ReadingFreshnessMilliseconds = null);

/// <summary>Typed DMM configuration under a host lease.</summary>
/// <param name="LeaseId">Canonical activation lease.</param>
/// <param name="Function">Measurement function.</param>
/// <param name="Range">Optional positive range in the function's base SI unit.</param>
/// <param name="Resolution">Optional positive resolution in the function's base SI unit.</param>
public sealed record IviDmmConfigureRequest([property: JsonRequired] Guid LeaseId, [property: JsonRequired] BenchDouble.Ivi.IviMeasurementFunction Function,
    double? Range = null, double? Resolution = null);

/// <summary>Logical experiment role assignment.</summary>
/// <param name="Role">Unique experiment role.</param>
/// <param name="InstrumentId">Canonical instrument identity.</param>
/// <param name="ReadingFreshnessMilliseconds">Optional saved experiment freshness budget from one millisecond through one day; does not initiate measurements.</param>
public sealed record SetupRole(string Role, string InstrumentId, double? ReadingFreshnessMilliseconds = null)
{
    /// <summary>Preserve the original two-field role deconstruction contract.</summary>
    /// <param name="role">Logical role name.</param>
    /// <param name="instrumentId">Canonical instrument identity.</param>
    public void Deconstruct(out string role,out string instrumentId) { role=Role;instrumentId=InstrumentId; }
}

/// <summary>Optional identity evidence or explicitly enforced expected fields.</summary>
/// <param name="Manufacturer">Optional manufacturer.</param>
/// <param name="Model">Optional model.</param>
/// <param name="Serial">Optional serial; absence never implies equivalence.</param>
public sealed record SetupIdentity(string? Manufacturer = null, string? Model = null, string? Serial = null);

/// <summary>Discovered controller, with stable identity only when the provider actually supplies it.</summary>
/// <param name="Provider">Provider identifier.</param>
/// <param name="Address">Current provider-local controller address.</param>
/// <param name="Label">Human-readable adapter description.</param>
/// <param name="StableId">Optional stable hardware identity.</param>
public sealed record DiscoveredController(string Provider, string Address, string Label, string? StableId = null);

/// <summary>Discovery evidence, not automatic proof of physical equivalence.</summary>
/// <param name="Provider">Provider identifier.</param>
/// <param name="Transport">Connection transport.</param>
/// <param name="Address">Device address scoped to its controller, when present.</param>
/// <param name="ControllerAddress">Provider-local controller address.</param>
/// <param name="ResourceId">Known host-owned canonical identity, or null for unregistered hardware.</param>
/// <param name="Identity">Cached identity metadata, if available.</param>
/// <param name="Busy">Whether the canonical host reports an active reservation.</param>
public sealed record DiscoveredEndpoint(string Provider, string Transport, string Address,
    string? ControllerAddress, string? ResourceId, SetupIdentity? Identity, bool Busy = false);

/// <summary>One bounded, cancellable discovery result for the local host.</summary>
/// <param name="Id">Snapshot ID used for operator confirmation.</param>
/// <param name="Controllers">Discovered adapters.</param>
/// <param name="Endpoints">Discovered endpoints, kept separate even if identities match.</param>
/// <param name="Diagnostics">Actionable provider diagnostics.</param>
public sealed record SetupDiscovery(string Id, DiscoveredController[] Controllers,
    DiscoveredEndpoint[] Endpoints, string[] Diagnostics);

/// <summary>Setup problem requiring operator action.</summary>
/// <param name="Code">Stable diagnostic code.</param>
/// <param name="Path">Configuration location.</param>
/// <param name="Message">Actionable explanation.</param>
public sealed record SetupProblem(string Code, string Path, string Message);

/// <summary>Stored setup revision; validation is historical evidence, not a physical lease.</summary>
/// <param name="Revision">Content digest used for optimistic concurrency.</param>
/// <param name="Draft">True if connection validation was not completed.</param>
/// <param name="Configuration">Versioned setup document.</param>
public sealed record SavedSetup(string Revision, bool Draft, BenchSetup Configuration);

/// <summary>Review/save request with the revision the operator last reviewed.</summary>
/// <param name="Configuration">Candidate configuration.</param>
/// <param name="ExpectedRevision">Previous revision or null when no file exists.</param>
/// <param name="Draft">Explicitly save an untested offline draft.</param>
public sealed record SaveSetupRequest(BenchSetup Configuration, string? ExpectedRevision, bool Draft);

/// <summary>Lease response with a pinned configuration and route.</summary>
/// <param name="LeaseId">Existing host registry lease.</param>
/// <param name="ResourceId">Canonical reserved resource.</param>
/// <param name="Revision">Configuration revision at reservation time.</param>
/// <param name="Connection">Selected connection snapshot.</param>
public sealed record SetupLease(Guid LeaseId, string ResourceId, string Revision, SetupConnection Connection);

/// <summary>Explicit review of an unresolved ownership record after host restart.</summary>
/// <param name="RecordId">Non-capability review ID from the host's pending recovery list.</param>
/// <param name="Confirmed">Operator requests the qualified provider's restart reconciliation procedure.</param>
public sealed record ReconcileRestartRequest([property: JsonRequired] Guid RecordId, [property: JsonRequired] bool Confirmed);

/// <summary>Capability-authenticated local owner heartbeat; no instrument command or automatic retry.</summary>
/// <param name="LeaseId">Existing canonical control capability.</param>
/// <param name="HostEpoch">Expected current host incarnation.</param>
/// <param name="OwnerGeneration">Exact lease generation from cached health.</param>
/// <param name="PeerEpoch">Client incarnation fixed after its first accepted heartbeat.</param>
/// <param name="Sequence">Strictly increasing positive client sequence.</param>
public sealed record OwnerHeartbeatRequest([property: JsonRequired] Guid LeaseId, [property: JsonRequired] Guid HostEpoch,
    [property: JsonRequired] long OwnerGeneration, [property: JsonRequired] Guid PeerEpoch, [property: JsonRequired] long Sequence);
