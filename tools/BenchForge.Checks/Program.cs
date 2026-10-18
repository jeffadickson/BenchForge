using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

// Every parallel map retains input indices. Reports and diagnostics use reference order.
internal static class Program
{
    internal static int Jobs = Math.Min(Environment.ProcessorCount, 8);
    internal static string Root = Directory.GetCurrentDirectory();
    internal static string Python = OperatingSystem.IsWindows() ? "python" : "python3";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var options = args.ToList();
        string? Take(string key)
        {
            var i = options.IndexOf(key);
            if (i < 0) return null;
            if (i + 1 >= options.Count) throw new ArgumentException($"Missing value for {key}");
            var result = options[i + 1]; options.RemoveRange(i, 2); return result;
        }
        try
        {
            Root = Path.GetFullPath(Take("--root") ?? Root);
            Python = Take("--python") ?? Python;
            if (Take("--jobs") is { } jobs) Jobs = Math.Clamp(int.Parse(jobs), 1, 256);
            var command = options.FirstOrDefault() ?? "help";
            if (options.Count > 0) options.RemoveAt(0);
            var output = Take("--output");
            var strict = options.Remove("--strict");
            return command switch
            {
                "requirements" => Requirements.Run(),
                "scoreboard" => Reports.Scoreboard(output),
                "neutrality" => Reports.Neutrality(output, strict),
                "compare" => Reports.Compare(Take("--roslyn")!, Take("--portable")!, output!),
                "docs" => Documentation.Gate(Take("--baseline")),
                "preflight" => Documentation.Preflight(output, strict),
                _ => Usage()
            };
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"ERROR: {error.Message}"); return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("BenchForge.Checks requirements|scoreboard|neutrality|compare|docs|preflight --root PATH [--jobs N] [--output PATH] [--strict] [--python EXE]");
        return 2;
    }

    internal static T[] Map<T>(string[] paths, Func<string, T> action)
    {
        var results = new T[paths.Length];
        Parallel.For(0, paths.Length, new ParallelOptions { MaxDegreeOfParallelism = Jobs }, i => results[i] = action(paths[i]));
        return results;
    }
}

internal static class Data
{
    internal static readonly StringComparer Order = StringComparer.Ordinal;
    // pathlib orders path components, not slash-joined strings (directory A precedes A.cs).
    internal static readonly IComparer<string> PathOrder = Comparer<string>.Create((left, right) =>
    {
        var a = left.Split(Path.DirectorySeparatorChar); var b = right.Split(Path.DirectorySeparatorChar);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var result = comparer.Compare(a[i], b[i]); if (result != 0) return result;
        }
        return a.Length.CompareTo(b.Length);
    });
    internal static string At(string path) => Path.Combine(Program.Root, path);
    internal static string Read(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path)).Replace("\r\n", "\n").Replace('\r', '\n');
    internal static JsonNode Json(string path) => JsonNode.Parse(Read(path))!;
    internal static string Text(this JsonNode? node) => node?.GetValue<string>() ?? "";
    internal static string Relative(string path) => Path.GetRelativePath(Program.Root, path).Replace('\\', '/');
    internal static JsonObject Row(string path, string symbol, int line, IEnumerable<string> issues) => new()
    {
        ["path"] = path, ["symbol"] = symbol, ["line"] = line,
        ["issues"] = new JsonArray(issues.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray())
    };

    // Python's default json.dumps layout, escaping and Windows text-mode newlines.
    internal static string Dumps(JsonNode? value, int depth = 0)
    {
        string Quote(string text)
        {
            var b = new StringBuilder("\"");
            foreach (var c in text) b.Append(c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                _ => c < 32 || c > 126 ? "\\u" + ((int)c).ToString("x4") : c.ToString()
            });
            return b.Append('"').ToString();
        }
        var indent = new string(' ', depth * 2); var child = indent + "  ";
        if (value is JsonObject obj) return obj.Count == 0 ? "{}" : "{\n" + string.Join(",\n", obj.Select(x => child + Quote(x.Key) + ": " + Dumps(x.Value, depth + 1))) + "\n" + indent + "}";
        if (value is JsonArray array) return array.Count == 0 ? "[]" : "[\n" + string.Join(",\n", array.Select(x => child + Dumps(x, depth + 1))) + "\n" + indent + "]";
        if (value is null) return "null";
        if (value is JsonValue v && v.TryGetValue<string>(out var s)) return Quote(s);
        if (value is JsonValue number)
        {
            var raw = value.ToJsonString();
            var parsed = number.TryGetValue<System.Text.Json.JsonElement>(out _);
            if ((!parsed || raw.Contains('.') || raw.Contains('e') || raw.Contains('E')) && number.TryGetValue<double>(out var d))
            {
                var text = d.ToString("R").Replace("E-", "e-").Replace("E+", "e+");
                return text + (d == Math.Truncate(d) && Math.Abs(d) < 1e16 ? ".0" : "");
            }
        }
        return value.ToJsonString();
    }

    internal static void Write(string path, JsonNode node) => WriteText(path, Dumps(node) + "\n");
    internal static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
    }

    internal static List<Dictionary<string, string>> Csv(string path)
    {
        var records = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); bool quoted = false, start = true;
        var source = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < source.Length && source[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(c);
            }
            else if (c == '"' && start) { quoted = true; start = false; }
            else if (c == ',' || c == '\n' || c == '\r')
            {
                if (c == ',' || row.Count > 0 || cell.Length > 0 || !start) { row.Add(cell.ToString()); cell.Clear(); }
                if (c != ',') { if (row.Count > 0) records.Add(row); row = []; if (c == '\r' && i + 1 < source.Length && source[i + 1] == '\n') i++; }
                start = true;
            }
            else { cell.Append(c); start = false; }
        }
        if (cell.Length > 0 || row.Count > 0 || !start) { row.Add(cell.ToString()); records.Add(row); }
        if (records.Count == 0) return [];
        return records.Skip(1).Select(r => records[0].Select((key, i) => (key, value: i < r.Count ? r[i] : "")).GroupBy(x => x.key).ToDictionary(g => g.Key, g => g.Last().value, StringComparer.Ordinal)).ToList();
    }

    internal static string Run(string exe, IEnumerable<string> args, string? input = null)
    {
        var info = new System.Diagnostics.ProcessStartInfo(exe) { WorkingDirectory = Program.Root, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null, UseShellExecute = false, CreateNoWindow = true };
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (input is not null) { process.StandardInput.Write(input); process.StandardInput.Close(); }
        process.WaitForExit(); var result = stdout.GetAwaiter().GetResult(); var errors = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidDataException(errors.Trim());
        return result;
    }
}
