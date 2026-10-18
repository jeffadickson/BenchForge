using System.Diagnostics;
using System.Text.Json;

// Shared Make-free application tasks. Arguments never pass through a shell.
internal static class DeveloperTasks
{
    internal sealed record Step(string Executable, string[] Arguments);
    private sealed record Stage(string Executable, string[] Arguments, double Milliseconds, int ExitCode);

    internal static Step[] Plan(string task, string dotnet, string configuration)
    {
        if (configuration is not ("Debug" or "Release")) throw new ArgumentException("Invalid configuration");
        Step Dotnet(params string[] args) => new(dotnet, args);
        var generator = "tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj";
        Step[] Generate() => [
            new(Program.Python, ["scripts/check_instrument_catalog.py"]),
            Dotnet("run", "--project", generator, "--configuration", configuration, "--", "instrument-definitions", "src/BenchDouble/Generated"),
            Dotnet("run", "--project", generator, "--configuration", configuration, "--", "--preview-v2", "instrument-definitions/drafts", "generated/BenchForge.Instruments/Code")];
        string[] Projects(string folder)
        {
            var root = Path.Combine(Program.Root, folder);
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return [];
            var pending = new Stack<string>(); pending.Push(root);
            var projects = new List<string>();
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var file in Directory.EnumerateFiles(directory, "*.csproj"))
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) projects.Add(Path.GetRelativePath(Program.Root, file).Replace('\\', '/'));
                foreach (var child in Directory.EnumerateDirectories(directory))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0 && !new[] { "bin", "obj" }.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) pending.Push(child);
            }
            return projects.Order(StringComparer.Ordinal).ToArray();
        }
        return task switch
        {
            "generate" => Generate(),
            "build" => Generate().Concat(new[] { "src", "examples", "tools", "generated" }.SelectMany(Projects)
                .Select(p => Dotnet("build", p, "--configuration", configuration))).ToArray(),
            "test" => Generate().Concat(Projects("tests").Where(p => !p.EndsWith("BenchDouble.Browser.Tests.csproj", StringComparison.OrdinalIgnoreCase))
                .Select(p => Dotnet("test", p, "--configuration", configuration))).ToArray(),
            _ => throw new ArgumentException($"Unknown development task {task}")
        };
    }

    internal static async Task<int> Run(string task, string dotnet, string configuration, bool planOnly, int timeoutSeconds)
    {
        var steps = Plan(task, dotnet, configuration);
        if (planOnly) { Console.WriteLine(JsonSerializer.Serialize(steps)); return 0; }
        var results = new List<Stage>();
        try
        {
        foreach (var step in steps)
        {
            var watch = Stopwatch.StartNew();
            Console.WriteLine($"Running {step.Executable} {string.Join(' ', step.Arguments)}");
            var info = new ProcessStartInfo(step.Executable) { WorkingDirectory = Program.Root, UseShellExecute = false };
            foreach (var argument in step.Arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start development task");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Console.Error.WriteLine($"Development task timed out after {timeoutSeconds} seconds.");
                results.Add(new(step.Executable, step.Arguments, watch.Elapsed.TotalMilliseconds, 124));
                return 124;
            }
            results.Add(new(step.Executable, step.Arguments, watch.Elapsed.TotalMilliseconds, process.ExitCode));
            if (process.ExitCode != 0) return process.ExitCode;
        }
        return 0;
        }
        finally
        {
            var directory = Path.Combine(Program.Root, "build");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "developer-workflow.json"), JsonSerializer.Serialize(new { task, stages = results }));
        }
    }
}
