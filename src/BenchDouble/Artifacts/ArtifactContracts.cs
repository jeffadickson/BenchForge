namespace BenchDouble.Artifacts;

/// <summary>Raw waveform decoding information; absent optional fields mean unknown, never assumed zero.</summary>
/// <param name="Channel">Instrument channel identifier.</param>
/// <param name="Encoding">Versioned encoding or vendor format name, including byte order when applicable.</param>
/// <param name="SampleCount">Number of samples in the acquisition.</param>
/// <param name="Unit">Physical unit of decoded samples.</param>
/// <param name="SampleIntervalSeconds">Uniform interval in seconds, or null for unknown/nonuniform timing.</param>
/// <param name="TimeOriginSeconds">First sample time relative to trigger, or null if unknown.</param>
/// <param name="VerticalScale">Multiplier from encoded samples to the stated unit, or null if unknown.</param>
/// <param name="VerticalOffset">Additive offset in the stated unit, or null if unknown.</param>
/// <param name="SettingsJson">Optional JSON object preserving acquisition, probe and calibration settings.</param>
public sealed record WaveformDescription(string Channel, string Encoding, long SampleCount, string Unit,
    double? SampleIntervalSeconds = null, double? TimeOriginSeconds = null,
    double? VerticalScale = null, double? VerticalOffset = null, string? SettingsJson = null);

/// <summary>Version-one capture provenance. A shared acquisition ID links traces and screenshots.</summary>
/// <param name="RunId">Run identifier, scoped by the application.</param>
/// <param name="AcquisitionId">Acquisition identifier within the run.</param>
/// <param name="InstrumentId">Instrument resource identifier.</param>
/// <param name="CapturedUtc">Acquisition UTC with zero offset, not upload time.</param>
/// <param name="MediaType">Payload media type; the store preserves bytes without decoding.</param>
/// <param name="Kind">One of waveform, screenshot or attachment.</param>
/// <param name="Identification">Optional identification string captured from the instrument.</param>
/// <param name="Waveform">Required for waveform artifacts; null for other kinds.</param>
public sealed record CaptureDescriptor(string RunId, string AcquisitionId, string InstrumentId,
    DateTimeOffset CapturedUtc, string MediaType, string Kind, string? Identification = null,
    WaveformDescription? Waveform = null);

/// <summary>Immutable committed capture metadata, independent of its backend locator.</summary>
/// <param name="ArtifactId">Caller-selected idempotency identity; reuse only for exactly identical content and metadata.</param>
/// <param name="Descriptor">Acquisition provenance and decoding manifest.</param>
/// <param name="ByteLength">Length of the original stored bytes.</param>
/// <param name="Sha256">Uppercase hexadecimal SHA-256 of the exact payload.</param>
public sealed record ArtifactRecord(Guid ArtifactId, CaptureDescriptor Descriptor, long ByteLength, string Sha256);

/// <summary>Store immutable capture streams and discover them by acquisition; callers retain ownership of streams.</summary>
/// <remarks>This contract does not reserve instruments or authorize remote callers. Implementations document local durability and limits.</remarks>
public interface IArtifactStore
{
    /// <summary>Commit bytes and metadata, or verify an identical replay of a previously committed ID.</summary>
    /// <param name="artifactId">Nonempty stable ID retained by the caller across retries.</param>
    /// <param name="descriptor">Validated capture metadata.</param>
    /// <param name="source">Readable stream at the payload start, consumed through EOF but not closed.</param>
    /// <param name="byteLength">Exact declared length; truncated and extra bytes are rejected.</param>
    /// <param name="cancellationToken">Cooperative cancellation before commit; a lost response can leave a committed capture, so retry the same ID.</param>
    /// <returns>Committed metadata after successful local commit or exact replay verification.</returns>
    /// <exception cref="InvalidDataException">Length, metadata or duplicate content is inconsistent.</exception>
    Task<ArtifactRecord> SaveAsync(Guid artifactId, CaptureDescriptor descriptor, Stream source,
        long byteLength, CancellationToken cancellationToken = default);

    /// <summary>Stream a capture into a destination and validate its length and digest.</summary>
    /// <param name="artifactId">Committed artifact ID.</param>
    /// <param name="destination">Writable caller-owned stream; bytes written before a failure must be discarded.</param>
    /// <param name="cancellationToken">Cooperative cancellation; partial destination output is possible.</param>
    /// <returns>The metadata for the fully copied and verified capture.</returns>
    /// <exception cref="KeyNotFoundException">No committed capture has this ID.</exception>
    /// <exception cref="InvalidDataException">The stored length or digest is corrupt.</exception>
    Task<ArtifactRecord> CopyToAsync(Guid artifactId, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>Find committed traces and screenshots sharing an acquisition without loading their bytes.</summary>
    /// <param name="runId">Exact run identifier.</param>
    /// <param name="acquisitionId">Exact acquisition identifier within the run.</param>
    /// <param name="limit">Maximum results, from 1 to 1000; reaching the limit may mean more results exist.</param>
    /// <param name="cancellationToken">Cancellation before database operations.</param>
    /// <returns>Captures ordered by artifact ID, with no implicit remote availability claim.</returns>
    Task<IReadOnlyList<ArtifactRecord>> FindAsync(string runId, string acquisitionId, int limit = 100,
        CancellationToken cancellationToken = default);
}
