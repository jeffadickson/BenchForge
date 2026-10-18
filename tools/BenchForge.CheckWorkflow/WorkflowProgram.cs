using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

// One managed build and one host for all C# checks; never compile native tools on the critical path.
internal static class WorkflowProgram
{
    private sealed record Result(string Name, double Milliseconds, int ExitCode, string Stdout, string Stderr);
    private static readonly AsyncLocal<TextWriter?> Output = new();
    private static readonly AsyncLocal<TextWriter?> Errors = new();

    private sealed class RoutedWriter(AsyncLocal<TextWriter?> slot, TextWriter fallback) : TextWriter
    {
        /// <inheritdoc/>
        public override Encoding Encoding => Encoding.UTF8;
        /// <inheritdoc/>
        public override void Write(char value) => (slot.Value ?? fallback).Write(value);
        /// <inheritdoc/>
        public override void Write(string? value) => (slot.Value ?? fallback).Write(value);
        /// <inheritdoc/>
        public override void WriteLine(string? value) => (slot.Value ?? fallback).WriteLine(value);
    }

    private static async Task<int> Main(string[] args)
    {
        try { return await Run(args); }
        catch (Exception error) { Console.Error.WriteLine("ERROR: " + error.Message); return 2; }
    }

    private static async Task<int> Run(string[] args)
    {
        var keys = new HashSet<string> { "--root", "--python", "--jobs", "--timing", "--command", "--expected-architecture", "--timeout-seconds" };
        var seen = new HashSet<string>();
        for (int index = 0; index < args.Length; index += 2)
        {
            if (!keys.Contains(args[index])) throw new ArgumentException($"Unknown option {args[index]}");
            if (!seen.Add(args[index])) throw new ArgumentException($"Duplicate option {args[index]}");
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Missing {args[index]}");
        }
        string Value(string key, string fallback)
        {
            var index = Array.IndexOf(args, key);
            return index < 0 ? fallback : index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing {key}");
        }
        Console.OutputEncoding = new UTF8Encoding(false);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Program.Root = Path.GetFullPath(Value("--root", "."));
        Program.Python = Value("--python", OperatingSystem.IsWindows() ? "python" : "python3");
        if (!int.TryParse(Value("--jobs", "8"), out var jobs) || jobs is < 1 or > 256) throw new ArgumentException("--jobs must be 1..256");
        Program.Jobs = jobs;
        if (!int.TryParse(Value("--timeout-seconds", "300"), out var timeout) || timeout is < 1 or > 3600) throw new ArgumentException("--timeout-seconds must be 1..3600");
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        if (Value("--expected-architecture", architecture) != architecture) throw new ArgumentException("Compiled check host architecture does not match the requested build");
        var command = Value("--command", "checks");
        if (command == "identity")
        {
            Console.WriteLine(new JsonObject { ["architecture"] = architecture, ["os_architecture"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), ["runtime"] = RuntimeInformation.RuntimeIdentifier, ["framework"] = RuntimeInformation.FrameworkDescription }.ToJsonString());
            return 0;
        }
        if (!new[] { "checks", "checks-only", "requirements", "scoreboard", "neutrality", "neutrality-strict", "roslyn", "preflight", "docs", "compare" }.Contains(command)) throw new ArgumentException($"Unknown command {command}");
        Directory.SetCurrentDirectory(Program.Root);
        if (command is not ("checks" or "checks-only")) return command switch
        {
            "requirements" => Requirements.Run(),
            "scoreboard" => Reports.Scoreboard(null),
            "neutrality" => Reports.Neutrality("build/source-neutrality-audit.json", false),
            "neutrality-strict" => Reports.Neutrality("build/source-neutrality-audit.json", true),
            "roslyn" => RoslynAudit.Run(Program.Root, "build/api-audit.json", Program.Jobs),
            "preflight" => Documentation.Preflight("build/docs-preflight.json", false),
            "docs" => Documentation.Gate(null),
            "compare" => Reports.Compare("build/api-audit.json", "build/docs-preflight.json", "build/docs-parity.json"),
            _ => throw new InvalidOperationException()
        };
        var originalOut = Console.Out; var originalErr = Console.Error;
        Console.SetOut(new RoutedWriter(Output, originalOut));
        Console.SetError(new RoutedWriter(Errors, originalErr));

        Task<Result> Check(string name, Func<int> action) => Task.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            using var stdout = new StringWriter(CultureInfo.InvariantCulture);
            using var stderr = new StringWriter(CultureInfo.InvariantCulture);
            Output.Value = stdout; Errors.Value = stderr;
            int code;
            try { code = action(); }
            catch (Exception error) { stderr.WriteLine("ERROR: " + error.Message); code = 2; }
            finally { Output.Value = null; Errors.Value = null; }
            return new Result(name, timer.Elapsed.TotalMilliseconds, code, stdout.ToString(), stderr.ToString());
        });
        Task<Result> Python(string name, params string[] arguments) => Task.Run(async () =>
        {
            var timer = Stopwatch.StartNew();
            var info = new ProcessStartInfo(Program.Python) { WorkingDirectory = Program.Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            info.Environment["PYTHONIOENCODING"] = "utf-8";
            info.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            try
            {
                using var process = Process.Start(info)!;
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                try { await process.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    return new Result(name, timer.Elapsed.TotalMilliseconds, 124, await stdout, await stderr + $"Timed out after {timeout} seconds.{Environment.NewLine}");
                }
                return new Result(name, timer.Elapsed.TotalMilliseconds, process.ExitCode, await stdout, await stderr);
            }
            catch (Exception error) { return new Result(name, timer.Elapsed.TotalMilliseconds, 2, "", "ERROR: " + error.Message + Environment.NewLine); }
        });
        async Task<Result> After(string name, Task<Result>[] dependencies, Func<Task<Result>> action)
        {
            var completed = await Task.WhenAll(dependencies);
            return completed.Any(r => r.ExitCode != 0) ? new Result(name, 0, 1, "", $"Skipped {name}: prerequisite failed.{Environment.NewLine}") : await action();
        }

        var watch = Stopwatch.StartNew();
        var requirements = Check("requirements", Requirements.Run);
        var scoreboard = Check("scoreboard", () => Reports.Scoreboard(null));
        var neutrality = Check("neutrality", () => Reports.Neutrality("build/source-neutrality-audit.json", true));
        var roslyn = Check("roslyn", () => RoslynAudit.Run(Program.Root, "build/api-audit.json", Program.Jobs));
        var preflight = Check("preflight", () => Documentation.Preflight("build/docs-preflight.json", false));
        var docs = After("docs", [roslyn], () => Check("docs", () => Documentation.Gate(null)));
        var compare = After("compare", [roslyn, preflight], () => Check("compare", () => Reports.Compare("build/api-audit.json", "build/docs-preflight.json", "build/docs-parity.json")));
        var pending = new List<Task<Result>> { requirements, scoreboard, neutrality, roslyn, preflight, docs, compare };
        if (command == "checks")
        {
            pending.Add(Python("tracker-tests", "-m", "unittest", "discover", "-s", "tests/requirements", "-v"));
            pending.Add(Python("documentation-tests", "-m", "unittest", "discover", "-s", "tests/documentation", "-v"));
        }
        var results = await Task.WhenAll(pending);
        Console.SetOut(originalOut); Console.SetError(originalErr);
        foreach (var result in results) { Console.Out.Write(result.Stdout); Console.Error.Write(result.Stderr); }
        using var currentProcess = Process.GetCurrentProcess();
        var report = new JsonObject
        {
            ["milliseconds"] = watch.Elapsed.TotalMilliseconds,
            ["cpu_milliseconds"] = currentProcess.TotalProcessorTime.TotalMilliseconds,
            ["peak_working_set_bytes"] = currentProcess.PeakWorkingSet64,
            ["jobs"] = Program.Jobs,
            ["logical_processors"] = Environment.ProcessorCount,
            ["architecture"] = architecture,
            ["runtime"] = RuntimeInformation.RuntimeIdentifier,
            ["command"] = command,
            ["stages"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject { ["name"] = r.Name, ["milliseconds"] = r.Milliseconds, ["exit_code"] = r.ExitCode }).ToArray()),
            ["scheduling"] = "Independent checks and the two focused suites overlap; docs waits for Roslyn, comparison waits for Roslyn and preflight. Stage durations overlap and must not be summed."
        };
        Data.Write(Value("--timing", "build/check-workflow-stages.json"), report);
        return results.FirstOrDefault(r => r.ExitCode != 0)?.ExitCode ?? 0;
    }
}
