using System.Diagnostics;
using System.Text.Json;

// Shared Make-free application tasks. Arguments never pass through a shell.
internal static class DeveloperTasks
{
    internal sealed record Step(string Executable, string[] Arguments);

    internal static Step[] Plan(string task, string dotnet, string configuration)
    {
        if (configuration is not ("Debug" or "Release")) throw new ArgumentException("Invalid configuration");
        Step Dotnet(params string[] args) => new(dotnet, args);
        var generator = "tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj";
        Step[] Generate() => [
            new(Program.Python, ["scripts/check_instrument_catalog.py"]),
            Dotnet("run", "--project", generator, "--configuration", configuration, "--", "instrument-definitions", "src/BenchDouble/Generated"),
            Dotnet("run", "--project", generator, "--configuration", configuration, "--", "--preview-v2", "instrument-definitions/drafts", "generated/BenchForge.Instruments/Code")];
        string[] Projects(string folder) => Directory.GetFiles(Path.Combine(Program.Root, folder), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(Program.Root, p).Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin"))
            .Select(p => Path.GetRelativePath(Program.Root, p).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        return task switch
        {
            "generate" => Generate(),
            "build" => Generate().Concat(new[] { "src", "examples", "tools", "generated" }.SelectMany(Projects)
                .Select(p => Dotnet("build", p, "--configuration", configuration))).ToArray(),
            "test" => Generate().Concat(Projects("tests").Where(p => !p.EndsWith("BenchDouble.Browser.Tests.csproj", StringComparison.Ordinal))
                .Select(p => Dotnet("test", p, "--configuration", configuration))).ToArray(),
            _ => throw new ArgumentException($"Unknown development task {task}")
        };
    }

    internal static async Task<int> Run(string task, string dotnet, string configuration, bool planOnly)
    {
        var steps = Plan(task, dotnet, configuration);
        if (planOnly) { Console.WriteLine(JsonSerializer.Serialize(steps)); return 0; }
        foreach (var step in steps)
        {
            Console.WriteLine($"Running {step.Executable} {string.Join(' ', step.Arguments)}");
            var info = new ProcessStartInfo(step.Executable) { WorkingDirectory = Program.Root, UseShellExecute = false };
            foreach (var argument in step.Arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start development task");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) return process.ExitCode;
        }
        return 0;
    }
}
