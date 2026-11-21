using System.IO.Compression;
using System.Security.Cryptography;
using BenchForge.Artifacts;

namespace BenchForge.Artifacts.Sqlite;

/// <summary>
/// Summary returned after persisting a Brotli-compressed waveform or evidence bundle into an <see cref="IArtifactStore"/>.
/// </summary>
/// <param name="Record">Immutable artifact record persisted by the backing store (describing the stored compressed bytes).</param>
/// <param name="RawByteLength">Uncompressed payload size in bytes.</param>
/// <param name="RawSha256">Uppercase hexadecimal SHA-256 digest of the original uncompressed payload.</param>
public sealed record CompressedArtifactResult(
    ArtifactRecord Record,
    long RawByteLength,
    string RawSha256)
{
    /// <summary>Compression ratio (<c>StoredBytes / RawBytes</c>); values below 1.0 indicate space savings.</summary>
    public double CompressionRatio => RawByteLength == 0 ? 1.0 : (double)Record.ByteLength / RawByteLength;
}

/// <summary>
/// Zero-dependency .NET 11 Brotli codec wrapping <see cref="IArtifactStore"/> for high-density binary waveforms and
/// reproducible run evidence bundles while keeping the SQLite schema v1 store completely unchanged.
/// </summary>
public static class CompressedArtifactCodec
{
    /// <summary>Media type suffix indicating Brotli-compressed content stored in <see cref="IArtifactStore"/>.</summary>
    public const string BrotliMediaTypeSuffix = "+br";

    /// <summary>
    /// Compress <paramref name="rawPayload"/> with Brotli and persist it atomically into <paramref name="store"/>,
    /// recording the raw SHA-256 digest and compression ratio.
    /// </summary>
    /// <param name="store">Backing artifact store (for example, <see cref="SqliteArtifactStore"/>).</param>
    /// <param name="artifactId">Stable caller-assigned artifact identifier.</param>
    /// <param name="descriptor">Capture metadata; its <see cref="CaptureDescriptor.MediaType"/> is tagged with <c>+br</c> if not already present.</param>
    /// <param name="rawPayload">Uncompressed waveform or evidence bytes.</param>
    /// <param name="compressionLevel">Brotli compression level (default <see cref="CompressionLevel.Optimal"/>).</param>
    /// <param name="cancellationToken">Cancellation token observed during compression and persistence.</param>
    /// <returns>Persisted artifact record together with raw length and uncompressed SHA-256 digest.</returns>
    public static async Task<CompressedArtifactResult> SaveCompressedAsync(
        IArtifactStore store,
        Guid artifactId,
        CaptureDescriptor descriptor,
        ReadOnlyMemory<byte> rawPayload,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        string rawSha256 = Convert.ToHexString(SHA256.HashData(rawPayload.Span));
        using var compressed = new MemoryStream(Math.Max(256, rawPayload.Length / 4));
        using (var brotli = new BrotliStream(compressed, compressionLevel, leaveOpen: true))
        {
            await brotli.WriteAsync(rawPayload, cancellationToken).ConfigureAwait(false);
        }

        compressed.Position = 0;
        var taggedDescriptor = descriptor.MediaType.EndsWith(BrotliMediaTypeSuffix, StringComparison.OrdinalIgnoreCase)
            ? descriptor
            : descriptor with { MediaType = descriptor.MediaType + BrotliMediaTypeSuffix };

        var record = await store.SaveAsync(artifactId, taggedDescriptor, compressed, compressed.Length, cancellationToken)
            .ConfigureAwait(false);
        return new CompressedArtifactResult(record, rawPayload.Length, rawSha256);
    }

    /// <summary>
    /// Read a Brotli-compressed artifact from <paramref name="store"/>, verify the stored SHA-256, and decompress into
    /// <paramref name="destination"/> under a strict uncompressed byte cap (preventing zip-bomb expansion).
    /// </summary>
    /// <param name="store">Backing artifact store.</param>
    /// <param name="artifactId">Artifact identifier to retrieve.</param>
    /// <param name="destination">Writable stream receiving decompressed payload bytes.</param>
    /// <param name="maxDecompressedBytes">Maximum allowed decompressed output bytes (default 64 MiB).</param>
    /// <param name="expectedRawSha256">Optional expected uncompressed SHA-256 hex digest verified after decompression.</param>
    /// <param name="cancellationToken">Cancellation token observed during read and decompression.</param>
    /// <returns>Tuple of the stored <see cref="ArtifactRecord"/>, decompressed byte count, and computed raw SHA-256 digest.</returns>
    /// <exception cref="InvalidDataException">Decompressed size exceeds <paramref name="maxDecompressedBytes"/> or digest verification fails.</exception>
    public static async Task<(ArtifactRecord Record, long RawByteLength, string RawSha256)> CopyDecompressedToAsync(
        IArtifactStore store,
        Guid artifactId,
        Stream destination,
        long maxDecompressedBytes = 64L * 1024 * 1024,
        string? expectedRawSha256 = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination must be writable.", nameof(destination));
        if (maxDecompressedBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxDecompressedBytes), "Max decompressed bytes must be positive.");

        using var compressed = new MemoryStream();
        var record = await store.CopyToAsync(artifactId, compressed, cancellationToken).ConfigureAwait(false);
        compressed.Position = 0;

        using var brotli = new BrotliStream(compressed, CompressionMode.Decompress, leaveOpen: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await brotli.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxDecompressedBytes)
                throw new InvalidDataException($"Decompressed artifact exceeded the {maxDecompressedBytes}-byte safety ceiling.");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        string rawSha256 = Convert.ToHexString(hash.GetHashAndReset());
        if (!string.IsNullOrWhiteSpace(expectedRawSha256) &&
            !string.Equals(rawSha256, expectedRawSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Decompressed artifact raw SHA-256 digest mismatch.");
        }

        return (record, total, rawSha256);
    }
}
