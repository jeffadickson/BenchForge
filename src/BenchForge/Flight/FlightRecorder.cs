using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchForge.Outcomes;
using BenchForge.Simulation;

namespace BenchForge.Flight;

/// <summary>
/// Single tamper-evident, hash-chained flight-recorder event capturing logical simulation time,
/// wall-clock UTC time, target instrument role, operation, outcome, and structured error code.
/// </summary>
/// <param name="Sequence">One-based monotonic event sequence number.</param>
/// <param name="SimulationElapsed">Logical simulation time from <see cref="AdjustableSimulationClock"/> when recorded.</param>
/// <param name="WallUtc">Wall-clock UTC timestamp when recorded.</param>
/// <param name="Role">Target instrument role or <c>bench</c>.</param>
/// <param name="Operation">Canonical operation name.</param>
/// <param name="Outcome">Outcome status (<c>Ok</c>, <c>Denied</c>, <c>Fault</c>, <c>TimeAdvance</c>).</param>
/// <param name="Summary">Human-readable measurement or transition summary.</param>
/// <param name="ErrorCode">Optional structured <see cref="BenchError.Code"/> on failure.</param>
/// <param name="PreviousSha256">SHA-256 hex digest of the preceding event (or genesis zeros).</param>
/// <param name="EntrySha256">SHA-256 hex digest over this event and <paramref name="PreviousSha256"/>.</param>
public sealed record FlightTraceEvent(
    long Sequence,
    TimeSpan SimulationElapsed,
    DateTimeOffset WallUtc,
    string Role,
    string Operation,
    string Outcome,
    string? Summary,
    string? ErrorCode,
    string PreviousSha256,
    string EntrySha256);

/// <summary>Serialized flight-recorder trace bundle persisted in Brotli-compressed <c>*.bftrace.br</c> files.</summary>
/// <param name="SchemaVersion">Trace schema version (<c>1</c>).</param>
/// <param name="BenchName">Logical bench name.</param>
/// <param name="HeadSha256">Latest SHA-256 chain digest across all recorded events.</param>
/// <param name="Events">Ordered recorded trace events.</param>
public sealed record FlightTraceBundle(
    int SchemaVersion,
    string BenchName,
    string HeadSha256,
    IReadOnlyList<FlightTraceEvent> Events);

/// <summary>Summary returned after deterministic time-travel replay of a <see cref="FlightTraceBundle"/>.</summary>
/// <param name="BenchName">Bench name from the trace.</param>
/// <param name="EventCount">Total events replayed.</param>
/// <param name="FaultCount">Number of <c>Denied</c> or <c>Fault</c> events observed.</param>
/// <param name="TotalSimulationElapsed">Final logical simulation time reached during replay.</param>
/// <param name="HeadSha256">Verified SHA-256 chain head digest.</param>
public sealed record FlightReplaySummary(
    string BenchName,
    int EventCount,
    int FaultCount,
    TimeSpan TotalSimulationElapsed,
    string HeadSha256);

/// <summary>
/// Deterministic, hash-chained flight recorder that logs bench operations alongside logical simulation time
/// and persists compact Brotli-compressed <c>*.bftrace.br</c> traces for offline time-travel replay.
/// </summary>
public sealed class FlightRecorder
{
    private const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private readonly Lock _gate = new();
    private readonly List<FlightTraceEvent> _events = new();
    private readonly ISimulationClock _simClock;
    private readonly TimeProvider _wallClock;
    private string _head = GenesisHash;

    /// <summary>Create a flight recorder attached to a bench name and canonical <see cref="ISimulationClock"/>.</summary>
    /// <param name="benchName">Logical bench identifier.</param>
    /// <param name="simClock">Canonical run-scoped simulation clock.</param>
    /// <param name="wallClock">Optional wall-clock provider for UTC timestamps.</param>
    public FlightRecorder(string benchName, ISimulationClock simClock, TimeProvider? wallClock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchName);
        BenchName = benchName;
        _simClock = simClock ?? throw new ArgumentNullException(nameof(simClock));
        _wallClock = wallClock ?? TimeProvider.System;
    }

    /// <summary>Logical bench name recorded in exported traces.</summary>
    public string BenchName { get; }

    /// <summary>Latest SHA-256 chain head digest.</summary>
    public string HeadSha256 { get { using (_gate.EnterScope()) return _head; } }

    /// <summary>Append a new hash-chained event at the current simulation clock elapsed time.</summary>
    /// <param name="role">Instrument role or <c>bench</c>.</param>
    /// <param name="operation">Operation name.</param>
    /// <param name="outcome">Outcome label (<c>Ok</c>, <c>Denied</c>, <c>Fault</c>, <c>TimeAdvance</c>).</param>
    /// <param name="summary">Optional concise summary.</param>
    /// <param name="errorCode">Optional structured error code.</param>
    /// <returns>The immutable recorded <see cref="FlightTraceEvent"/>.</returns>
    public FlightTraceEvent Record(string role, string operation, string outcome, string? summary = null, string? errorCode = null)
    {
        using (_gate.EnterScope())
        {
            long seq = _events.Count + 1;
            var simTime = _simClock.Elapsed;
            var wall = _wallClock.GetUtcNow();
            string prev = _head;
            string hash = ComputeHash(seq, simTime, wall, role, operation, outcome, summary, errorCode, prev);
            var ev = new FlightTraceEvent(seq, simTime, wall, role, operation, outcome, summary, errorCode, prev, hash);
            _events.Add(ev);
            _head = hash;
            return ev;
        }
    }

    /// <summary>Return an immutable snapshot bundle of all recorded trace events.</summary>
    /// <returns>Current <see cref="FlightTraceBundle"/>.</returns>
    public FlightTraceBundle ExportBundle()
    {
        using (_gate.EnterScope())
            return new FlightTraceBundle(1, BenchName, _head, _events.ToArray());
    }

    /// <summary>Verify the SHA-256 hash chain integrity of a <see cref="FlightTraceBundle"/>.</summary>
    /// <param name="bundle">Trace bundle to verify.</param>
    /// <returns>Railroad result passing when zero tampering is detected.</returns>
    public static BenchResult VerifyBundle(FlightTraceBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        string expectedPrev = GenesisHash;
        long expectedSeq = 1;
        foreach (var ev in bundle.Events)
        {
            if (ev.Sequence != expectedSeq || !string.Equals(ev.PreviousSha256, expectedPrev, StringComparison.Ordinal))
            {
                return BenchResult.Fail(new BenchError(
                    "BF-TRACE-TAMPERED",
                    BenchErrorKind.ProtocolFault,
                    $"Flight trace chain broken at sequence {ev.Sequence}.",
                    bundle.BenchName,
                    Remedy: "Discard tampered trace and inspect original lab archive."));
            }
            string computed = ComputeHash(ev.Sequence, ev.SimulationElapsed, ev.WallUtc, ev.Role, ev.Operation, ev.Outcome, ev.Summary, ev.ErrorCode, ev.PreviousSha256);
            if (!string.Equals(computed, ev.EntrySha256, StringComparison.Ordinal))
            {
                return BenchResult.Fail(new BenchError(
                    "BF-TRACE-TAMPERED",
                    BenchErrorKind.ProtocolFault,
                    $"Flight trace digest mismatch at sequence {ev.Sequence}.",
                    bundle.BenchName,
                    Remedy: "Discard tampered trace and inspect original lab archive."));
            }
            expectedPrev = ev.EntrySha256;
            expectedSeq++;
        }
        if (!string.Equals(expectedPrev, bundle.HeadSha256, StringComparison.Ordinal))
        {
            return BenchResult.Fail(new BenchError(
                "BF-TRACE-HEAD",
                BenchErrorKind.ProtocolFault,
                "Flight trace head digest does not match final event.",
                bundle.BenchName,
                Remedy: "Trace file is truncated or modified."));
        }
        return BenchResult.Ok();
    }

    /// <summary>Persist the current flight trace compressed with .NET 11 Brotli to <paramref name="path"/>.</summary>
    /// <param name="path">Destination file path (recommended <c>.bftrace.br</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing compressed byte size on disk.</returns>
    public async Task<BenchResult<long>> SaveBrotliAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bundle = ExportBundle();
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(bundle);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp." + Guid.NewGuid().ToString("N");
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var brotli = new BrotliStream(fs, CompressionLevel.Optimal, leaveOpen: false))
        {
            await brotli.WriteAsync(jsonBytes, cancellationToken).ConfigureAwait(false);
        }
        File.Move(tmp, path, overwrite: true);
        return BenchResult<long>.Ok(new FileInfo(path).Length);
    }

    /// <summary>Load and SHA-256-verify a Brotli-compressed flight trace bundle from disk.</summary>
    /// <param name="path">Path to the <c>.bftrace.br</c> file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Railroad outcome containing the verified <see cref="FlightTraceBundle"/>.</returns>
    public static async Task<BenchResult<FlightTraceBundle>> LoadBrotliAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return BenchResult<FlightTraceBundle>.Fail(new BenchError("BF-TRACE-MISSING", BenchErrorKind.ResourceNotFound, $"Trace file not found: {path}"));

        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var brotli = new BrotliStream(fs, CompressionMode.Decompress);
            var bundle = await JsonSerializer.DeserializeAsync<FlightTraceBundle>(brotli, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (bundle is null)
                return BenchResult<FlightTraceBundle>.Fail(BenchError.Validation("BF-TRACE-NULL", "Flight trace payload was null."));

            var check = VerifyBundle(bundle);
            return check.IsFailure ? BenchResult<FlightTraceBundle>.Fail(check.Error) : BenchResult<FlightTraceBundle>.Ok(bundle);
        }
        catch (Exception ex)
        {
            return BenchResult<FlightTraceBundle>.Fail(new BenchError("BF-TRACE-READ", BenchErrorKind.ProtocolFault, $"Failed to read flight trace: {ex.Message}"));
        }
    }

    private static string ComputeHash(
        long seq,
        TimeSpan simTime,
        DateTimeOffset wall,
        string role,
        string op,
        string outcome,
        string? summary,
        string? errorCode,
        string prev)
    {
        string payload = $"{seq}|{simTime.Ticks}|{wall.ToUnixTimeMilliseconds()}|{role}|{op}|{outcome}|{summary ?? ""}|{errorCode ?? ""}|{prev}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}

/// <summary>Deterministic time-travel replayer reconstructing exact logical timestamps from a verified <see cref="FlightTraceBundle"/>.</summary>
public static class FlightTraceReplayer
{
    /// <summary>Verify and deterministically replay a recorded trace onto a fresh <see cref="AdjustableSimulationClock"/>.</summary>
    /// <param name="bundle">Recorded flight trace bundle.</param>
    /// <param name="onEvent">Optional callback invoked at each replayed event's exact logical simulation timestamp.</param>
    /// <returns>Railroad outcome containing <see cref="FlightReplaySummary"/>.</returns>
    public static BenchResult<FlightReplaySummary> Replay(
        FlightTraceBundle bundle,
        Action<FlightTraceEvent, AdjustableSimulationClock>? onEvent = null)
    {
        var verify = FlightRecorder.VerifyBundle(bundle);
        if (verify.IsFailure)
            return BenchResult<FlightReplaySummary>.Fail(verify.Error);

        var replayClock = new AdjustableSimulationClock();
        int faults = 0;
        foreach (var ev in bundle.Events)
        {
            if (ev.SimulationElapsed < replayClock.Elapsed)
            {
                return BenchResult<FlightReplaySummary>.Fail(new BenchError(
                    "BF-TRACE-NONMONOTONIC",
                    BenchErrorKind.ProtocolFault,
                    $"Trace timestamp moved backwards at sequence {ev.Sequence}.",
                    bundle.BenchName));
            }
            var delta = ev.SimulationElapsed - replayClock.Elapsed;
            if (delta > TimeSpan.Zero)
                replayClock.AdvanceBy(delta);

            if (string.Equals(ev.Outcome, "Fault", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ev.Outcome, "Denied", StringComparison.OrdinalIgnoreCase))
                faults++;

            onEvent?.Invoke(ev, replayClock);
        }

        return BenchResult<FlightReplaySummary>.Ok(new FlightReplaySummary(
            bundle.BenchName,
            bundle.Events.Count,
            faults,
            replayClock.Elapsed,
            bundle.HeadSha256));
    }
}
