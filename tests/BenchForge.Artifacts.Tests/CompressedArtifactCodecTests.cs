using BenchForge.Artifacts;
using BenchForge.Artifacts.Sqlite;
using Xunit;

namespace BenchForge.Artifacts.Tests;

/// <summary>Regression tests for zero-dependency .NET 11 Brotli CompressedArtifactCodec.</summary>
public sealed class CompressedArtifactCodecTests
{
    /// <summary>Verify waveform payload compresses tightly, round-trips byte-for-byte, and checks raw SHA-256 and size caps.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task CompressesWaveformsVerifiesSha256AndEnforcesDecompressionCap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "benchforge-br-artifacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = SqliteArtifactStore.Open(Path.Combine(dir, "captures.sqlite"));
            var raw = new byte[16384];
            for (int i = 0; i < raw.Length; i++)
                raw[i] = (byte)(i % 32);

            var descriptor = new CaptureDescriptor(
                "run-br-1",
                "acq-1",
                "SCOPE1",
                DateTimeOffset.UtcNow,
                "application/octet-stream",
                "waveform",
                "Tektronix,MSO24,SIM,1.0",
                new WaveformDescription("CH1", "uint8-raw", raw.Length, "V", 1e-6));

            var id = Guid.NewGuid();
            var saved = await CompressedArtifactCodec.SaveCompressedAsync(store, id, descriptor, raw);

            Assert.Equal(raw.Length, saved.RawByteLength);
            Assert.True(saved.Record.ByteLength < raw.Length / 4, $"Expected strong compression, got {saved.Record.ByteLength} B.");
            Assert.EndsWith(CompressedArtifactCodec.BrotliMediaTypeSuffix, saved.Record.Descriptor.MediaType);

            using var output = new MemoryStream();
            var (_, len, sha) = await CompressedArtifactCodec.CopyDecompressedToAsync(
                store, id, output, expectedRawSha256: saved.RawSha256);

            Assert.Equal(raw.Length, len);
            Assert.Equal(saved.RawSha256, sha);
            Assert.Equal(raw, output.ToArray());

            // Wrong expected SHA-256 fails closed.
            using var badHashOut = new MemoryStream();
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CompressedArtifactCodec.CopyDecompressedToAsync(store, id, badHashOut, expectedRawSha256: "0000"));

            // Decompression ceiling protects against oversized/zip-bomb payloads.
            using var cappedOut = new MemoryStream();
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CompressedArtifactCodec.CopyDecompressedToAsync(store, id, cappedOut, maxDecompressedBytes: 1024));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
