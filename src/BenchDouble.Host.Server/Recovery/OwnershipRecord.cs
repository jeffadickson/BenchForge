using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BenchDouble.Host.Server.Recovery;

/// <summary>Unresolved ownership evidence; contains no usable lease token or driver options.</summary>
/// <param name="ResourceId">Canonical instrument ID.</param>
/// <param name="RecordId">Reconciliation review identifier; not a control lease.</param>
/// <param name="RoutesDigest">SHA-256 of the registered descriptor set.</param>
/// <param name="Revision">Optional saved configuration revision at acquisition.</param>
/// <param name="RecordedUtc">Host time when ownership was recorded, not instrument time.</param>
public sealed record OwnershipRecord(string ResourceId, Guid RecordId, string RoutesDigest,
    string? Revision, DateTimeOffset RecordedUtc)
{
    /// <summary>Validate bounded journal evidence before accepting ownership state.</summary>
    /// <exception cref="ArgumentException">A field is absent, malformed or outside the supported bounds.</exception>
    public void Validate()
    {
        if (ResourceId is null || !Regex.IsMatch(ResourceId, @"\A[A-Za-z0-9_.-]{1,80}\z") || RecordId == Guid.Empty ||
            RoutesDigest is null || !Regex.IsMatch(RoutesDigest, @"\A[0-9a-f]{64}\z") ||
            Revision is not null && !Regex.IsMatch(Revision, @"\A[0-9a-fA-F]{64}\z") || RecordedUtc == default)
            throw new ArgumentException("Invalid ownership record.");
    }

    /// <summary>Fingerprint a descriptor set without retaining raw addresses in the journal.</summary>
    /// <param name="descriptors">Explicitly associated routes; descriptor equality ignores case.</param>
    /// <returns>Order-independent lowercase SHA-256 digest.</returns>
    public static string DigestRoutes(IEnumerable<string> descriptors)
    {
        var canonical = descriptors.Select(d => d.ToUpperInvariant()).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(d => d.Length + ":" + d);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", canonical))));
    }
}

/// <summary>Durable unresolved ownership evidence used by the canonical registry.</summary>
public interface IHostOwnershipJournal
{
    /// <summary>Read detached unresolved records; corruption must fail closed.</summary>
    /// <returns>Records requiring reconciliation or current-owner cleanup.</returns>
    OwnershipRecord[] Read();

    /// <summary>Persist ownership before returning a grant or opening hardware.</summary>
    /// <param name="record">New ownership evidence; existing ownership must not be overwritten.</param>
    void Add(OwnershipRecord record);

    /// <summary>Remove evidence only after successful cleanup or explicit reconciliation.</summary>
    /// <param name="resourceId">Canonical resource.</param>
    /// <param name="recordId">Exact record reviewed; stale identifiers are rejected.</param>
    void Remove(string resourceId, Guid recordId);
}
