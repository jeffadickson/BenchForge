namespace BenchDouble.Host.Server.Setup;

/// <summary>Versioned host setup draft; serial numbers are optional throughout.</summary>
/// <param name="SchemaVersion">Supported schema version, currently one.</param>
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
public sealed record SetupConnection(string Id, string Provider, string Transport, string Address,
    string? ControllerId = null);

/// <summary>Logical experiment role assignment.</summary>
/// <param name="Role">Unique experiment role.</param>
/// <param name="InstrumentId">Canonical instrument identity.</param>
public sealed record SetupRole(string Role, string InstrumentId);

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
