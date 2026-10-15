using System.Buffers.Binary;
using BenchDouble.Artifacts;
using BenchDouble.Artifacts.Sqlite;

// Deterministic simulated acquisition: repeated execution verifies the same IDs.
// Real acquisition code must retain its own ID and record actual capture UTC.
var directory = Path.GetFullPath(args.Length > 0 ? args[0] : "capture-demo-output");
Directory.CreateDirectory(directory);
var database = Path.Combine(directory, "captures.sqlite");
IArtifactStore store = SqliteArtifactStore.Open(database);
var traceId = Guid.Parse("39fa8f15-15b3-46c5-a1f8-802a55b59140");
var screenshotId = Guid.Parse("b257432c-1ea5-49fb-93c8-a49aa4e5cd25");
var description = new CaptureDescriptor("SIM-RUN-1", "ACQ-1", "SIM-SCOPE-1",
    new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), "application/octet-stream", "waveform",
    "BenchDouble,SimulatedScope,DEMO,1", new WaveformDescription(
        "CH1", "float64-le/v1", 4, "V", SampleIntervalSeconds: 1e-6, TimeOriginSeconds: -1e-6,
        VerticalScale: 1, VerticalOffset: 0, SettingsJson: "{\"source\":\"synthetic demo\"}"));
var bytes = new byte[4 * sizeof(double)];
var volts = new[] { 0.0, 1.0, -0.5, 0.0 };
for (int index = 0; index < volts.Length; index++)
    BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(index * sizeof(double)), volts[index]);
using var traceInput = new MemoryStream(bytes);
var trace = await store.SaveAsync(traceId, description, traceInput, bytes.Length);
// A valid one-pixel PNG fixture, not a screenshot of physical hardware.
var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAX+XDSwAAAABJRU5ErkJggg==");
using var imageInput = new MemoryStream(png);
await store.SaveAsync(screenshotId, description with { Kind = "screenshot", MediaType = "image/png", Waveform = null }, imageInput, png.Length);
Console.WriteLine($"Local database: {database}");
Console.WriteLine($"Trace {trace.ArtifactId}: {trace.ByteLength} bytes, SHA-256 {trace.Sha256}");
foreach (var artifact in await store.FindAsync("SIM-RUN-1", "ACQ-1"))
    Console.WriteLine($"Acquisition ACQ-1 includes {artifact.Descriptor.Kind}: {artifact.ArtifactId}");

// Publish the exported filename only after the retrieval digest has verified.
var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".partial");
try
{
    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await store.CopyToAsync(screenshotId, output);
    File.Move(temporary, Path.Combine(directory, "capture.png"), overwrite: true);
}
finally { if (File.Exists(temporary)) File.Delete(temporary); }
Console.WriteLine("Verified screenshot exported to capture.png. Remote upload remains pending; no remote replicator is installed.");
