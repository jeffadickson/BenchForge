namespace BenchDouble.Host.Contracts;

/// <summary>Network-safe resource description; no physical session handles cross the API.</summary>
public sealed record InstrumentDescriptor(string ResourceId, string InstrumentClass, string Backend);
public sealed record ReservationRequest(string ResourceId);
public sealed record ReservationGrant(string ResourceId, Guid LeaseId);
public sealed record ScpiRequest(Guid LeaseId, string Command);
public sealed record ScpiResponse(string Response);
public sealed record HostError(string Code, string Message);
