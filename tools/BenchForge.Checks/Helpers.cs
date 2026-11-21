using System.Diagnostics;
using System.Text.Json.Nodes;

// C# ports of the remaining standalone Python helper scripts (catalog, clean-generated,
// artifact-recovery, ownership-restart, ownership-shutdown, owner-heartbeat).
internal static class Helpers
{
    private static readonly HashSet<string> AllowedEvidence = ["sourceChecked", "catalogListed", "manualReferenced"];
    private static readonly HashSet<string> AllowedDefinition = ["catalogOnly", "manualDriverPartial", "definitionDraft", "definitionReviewed"];
    private static readonly HashSet<string> AllowedImplementation = ["notPorted", "partialDcSourceMeasure", "generatedPrototype", "hardwareValidated"];

    internal static int Catalog()
    {
        var catalog = Data.Json(Data.At("instrument-definitions/instrument-catalog.json")).AsObject();
        if (catalog["schemaVersion"]?.GetValue<int>() != 1)
            throw new InvalidDataException("Unsupported instrument catalog schema");
        var instruments = catalog["instruments"]?.AsArray()
            ?? throw new InvalidDataException("Catalog must contain an instruments array");
        if (instruments.Count < 26)
            throw new InvalidDataException("Catalog must contain at least 25 catalog entries and Keithley 236");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var models = new HashSet<(string Manufacturer, string Model)>();
        var items = instruments.Select(node => node!.AsObject()).ToArray();
        var definitionPaths = new List<(string Id, string Path)>();
        var implementationFiles = new List<(string Id, string Field, string Path)>();

        foreach (var item in items)
        {
            var id = item["id"].Text();
            var identity = (item["manufacturer"].Text(), item["model"].Text());
            if (!ids.Add(id) || !models.Add(identity))
                throw new InvalidDataException($"Duplicate instrument: {id}");
            if (!AllowedEvidence.Contains(item["sourceEvidence"].Text()))
                throw new InvalidDataException($"Unknown evidence state: {id}");
            var defStatus = item["definitionStatus"].Text();
            if (!AllowedDefinition.Contains(defStatus))
                throw new InvalidDataException($"Unknown definition state: {id}");
            var implStatus = item["implementationStatus"].Text();
            if (!AllowedImplementation.Contains(implStatus))
                throw new InvalidDataException($"Unknown implementation state: {id}");
            var hwValidated = item["hardwareValidated"]?.GetValue<bool>() ?? false;
            if (hwValidated && implStatus != "hardwareValidated")
                throw new InvalidDataException($"Hardware validation claim not supported: {id}");
            if (defStatus is "definitionDraft" or "definitionReviewed")
            {
                var defPath = item["definitionPath"]?.Text() ?? "";
                if (defPath.Length == 0)
                    throw new InvalidDataException($"Missing definition draft for {id}");
                definitionPaths.Add((id, defPath));
            }
            if (implStatus != "notPorted")
            {
                foreach (var field in new[] { "driverPath", "mockPath" })
                {
                    var path = item[field]?.Text() ?? "";
                    if (path.Length > 0)
                        implementationFiles.Add((id, field, path));
                }
            }
        }

        // Validate definition schemas and generated files in parallel across workers.
        var defChecks = Program.Map(definitionPaths.Select(x => x.Path).ToArray(), rel =>
        {
            var full = Data.At(rel);
            if (!File.Exists(full)) return "missing";
            var schema = Data.Json(full).AsObject()["schemaVersion"]?.GetValue<int>();
            return schema is 1 or 2 ? "ok" : "schema";
        });
        for (int i = 0; i < definitionPaths.Count; i++)
        {
            if (defChecks[i] == "missing")
                throw new InvalidDataException($"Missing definition draft for {definitionPaths[i].Id}");
            if (defChecks[i] == "schema")
                throw new InvalidDataException($"Unsupported definition schema for {definitionPaths[i].Id}");
        }

        var fileChecks = Program.Map(implementationFiles.Select(x => x.Path).ToArray(), rel => File.Exists(Data.At(rel)));
        for (int i = 0; i < implementationFiles.Count; i++)
        {
            if (!fileChecks[i])
                throw new InvalidDataException($"Missing {implementationFiles[i].Field} for {implementationFiles[i].Id}: {implementationFiles[i].Path}");
        }

        int catalogCandidates = items.Count(x => x["id"].Text().StartsWith("CAT-", StringComparison.Ordinal));
        if (catalogCandidates != 25 || !ids.Contains("LEGACY-KEITHLEY-236"))
            throw new InvalidDataException("Expected 25 catalog entries plus Keithley 236");
        int implemented = items.Count(x => x["implementationStatus"].Text() != "notPorted");
        Console.WriteLine($"Catalog OK: {items.Length} entries, {catalogCandidates} catalog candidates, {implemented} partial/implemented");
        return 0;
    }

    internal static int CleanGenerated()
    {
        var parent = Path.GetFullPath(Data.At("generated/BenchForge.Instruments"));
        var target = Path.GetFullPath(Path.Combine(parent, "Code"));
        if (!string.Equals(Path.GetDirectoryName(target), parent, StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe generated directory");
        if (File.Exists(target) || Directory.Exists(target))
        {
            var attributes = File.GetAttributes(target);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Refusing to follow generated source symlink");
            if ((attributes & FileAttributes.Directory) == 0)
                throw new InvalidOperationException("Generated source path is not a directory");
            Directory.Delete(target, recursive: true);
        }
        Console.WriteLine("Removed only generated/BenchForge.Instruments/Code");
        return 0;
    }

    internal static int ArtifactRecovery(string dotnet, string? probeArg)
    {
        var probe = Path.GetFullPath(probeArg ?? Data.At("tools/BenchForge.ArtifactCrashProbe/bin/Debug/net11.0/BenchForge.ArtifactCrashProbe.dll"));
        if (!File.Exists(probe))
            throw new FileNotFoundException("Build tools/BenchForge.ArtifactCrashProbe before running recovery checks.", probe);
        var directory = Path.Combine(Path.GetTempPath(), "benchforge-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, "captures.sqlite");
            var crash = RunProcess(dotnet, ["exec", probe, database], 30_000);
            if (crash.ExitCode != 97)
                throw new InvalidOperationException($"Probe failed to reach injected exit (code {crash.ExitCode}): {crash.Stdout}\n{crash.Stderr}");
            var verify = RunProcess(dotnet, ["exec", probe, "--verify", database], 30_000);
            if (verify.ExitCode != 0)
                throw new InvalidOperationException($"Recovery verification failed (code {verify.ExitCode}): {verify.Stderr}");
        }
        finally { DeleteTempDirectory(directory); }
        Console.WriteLine("Process-exit recovery passed: prior capture intact, interrupted transaction absent, outbox consistent.");
        return 0;
    }

    internal static int OwnershipRestart(string dotnet, string? probeArg)
    {
        var probe = ResolveRecoveryProbe(probeArg);
        var directory = Path.Combine(Path.GetTempPath(), "bench-ownership-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var child = StartProbe(dotnet, [probe, "hold", directory]);
            try
            {
                WaitForReady(child, 20_000, "Owner did not persist its grant before the deadline");
                child.Kill(entireProcessTree: true);
                if (!child.WaitForExit(10_000))
                    throw new TimeoutException("Killed owner process did not exit within timeout");
            }
            finally
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(10_000); }
            }
            var verify = RunProcess(dotnet, [probe, "verify", directory], 20_000);
            if (verify.ExitCode != 0)
                throw new InvalidOperationException($"Ownership restart verify failed: {verify.Stderr}");
            Console.WriteLine(verify.Stdout.Trim());
        }
        finally { DeleteTempDirectory(directory); }
        return 0;
    }

    internal static int OwnershipShutdown(string dotnet, string? probeArg)
    {
        var probe = ResolveRecoveryProbe(probeArg);
        foreach (var (mode, expectedRecords) in new[] { ("graceful", 0), ("graceful-retained", 1) })
        {
            var directory = Path.Combine(Path.GetTempPath(), "bench-shutdown-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using var child = StartProbe(dotnet, [probe, mode, directory]);
                try
                {
                    WaitForReady(child, 20_000, "Shutdown probe did not become ready");
                    SendGracefulTerminate(child);
                    if (!child.WaitForExit(10_000) || child.ExitCode != 0)
                        throw new InvalidOperationException($"Host shutdown failed (exit {child.ExitCode})");
                    var document = Data.Json(Path.Combine(directory, "ownership.json")).AsObject();
                    var count = document["records"]?.AsArray().Count ?? -1;
                    if (count != expectedRecords)
                        throw new InvalidOperationException("Shutdown erased quarantine or stranded a clean reservation");
                }
                finally
                {
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(10_000); }
                }
            }
            finally { DeleteTempDirectory(directory); }
        }
        Console.WriteLine("PASS: SIGTERM closes a plain canonical reservation and retains startup quarantine; no vendor I/O.");
        return 0;
    }

    internal static int OwnerHeartbeat(string dotnet, string? probeArg)
    {
        var probe = ResolveRecoveryProbe(probeArg);
        var directory = Path.Combine(Path.GetTempPath(), "bench-heartbeat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var result = RunProcess(dotnet, [probe, "heartbeat-expire", directory], 20_000);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"Heartbeat probe failed: {result.Stderr}");
            var document = Data.Json(Path.Combine(directory, "ownership.json")).AsObject();
            var count = document["records"]?.AsArray().Count ?? -1;
            if (count != 1)
                throw new InvalidOperationException("Heartbeat expiry erased unresolved ownership");
            Console.WriteLine(result.Stdout.Trim());
        }
        finally { DeleteTempDirectory(directory); }
        return 0;
    }

    private static string ResolveRecoveryProbe(string? probeArg)
    {
        var probe = Path.GetFullPath(probeArg ?? Data.At("tools/BenchForge.RecoveryProbe/bin/Release/net11.0/BenchForge.RecoveryProbe.dll"));
        if (!File.Exists(probe))
            throw new FileNotFoundException("Build tools/BenchForge.RecoveryProbe in Release before running ownership probes.", probe);
        return probe;
    }

    private static Process StartProbe(string dotnet, string[] arguments)
    {
        var info = new ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new InvalidOperationException("Failed to start probe process.");
    }

    private static void WaitForReady(Process child, int timeoutMs, string errorMessage)
    {
        var lineTask = child.StandardOutput.ReadLineAsync();
        if (!lineTask.Wait(timeoutMs) || !string.Equals(lineTask.Result?.Trim(), "READY", StringComparison.Ordinal))
            throw new TimeoutException(errorMessage);
    }

    private static void SendGracefulTerminate(Process child)
    {
        if (OperatingSystem.IsWindows())
        {
            child.CloseMainWindow();
            return;
        }
        var signal = RunProcess("kill", ["-TERM", child.Id.ToString()], 5_000);
        if (signal.ExitCode != 0)
            throw new InvalidOperationException($"Failed to send SIGTERM to pid {child.Id}: {signal.Stderr}");
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(string fileName, string[] arguments, int timeoutMs)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            throw new TimeoutException($"{fileName} timed out after {timeoutMs} ms");
        }
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    private static void DeleteTempDirectory(string directory)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 14) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) when (attempt < 14) { Thread.Sleep(200); }
        }
    }
}
