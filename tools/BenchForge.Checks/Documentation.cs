using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static LexicalPatterns;

internal static class Documentation
{
    private static readonly string[] Areas = ["src", "tools", "scripts", "examples", "tests", "generated"];
    private static readonly HashSet<string> Skip = ["bin", "obj", "__pycache__", ".git", ".venv"];
    private static bool Match(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static string[] Files(bool portable) => Areas.Take(portable ? 6 : 5).SelectMany(area =>
        Directory.Exists(Data.At(area)) ? Directory.EnumerateFiles(Data.At(area), "*", SearchOption.AllDirectories)
            .Where(p => !Data.Relative(p).Split('/').Any(x => portable ? Skip.Contains(x) : x is "bin" or "obj" or "__pycache__"))
            .Where(p => portable ? Path.GetExtension(p) is ".cs" or ".py" : Path.GetExtension(p) == ".py").Order(Data.PathOrder) : Enumerable.Empty<string>()).ToArray();

    private static List<JsonNode>[] PythonRows(string[] files, bool portable)
    {
        if (files.Length == 0) return [];
        var request = new JsonArray(files.Select(p => (JsonNode)new JsonObject { ["file"] = p, ["encoding"] = portable ? "utf-8-sig" : "utf-8" }).ToArray());
        var helper = Path.Combine(AppContext.BaseDirectory, "python_ast_inventory.py");
        if (!File.Exists(helper)) helper = Data.At("scripts/python_ast_inventory.py");
        var parsed = JsonNode.Parse(Data.Run(Program.Python, [helper], request.ToJsonString()))!.AsArray();
        return parsed.Select((rows, i) => rows!.AsArray().Where(r => !portable || r!["portable"]!.GetValue<bool>()).Where(r => !portable || !r!["documented"]!.GetValue<bool>())
            .Select(r => (JsonNode)Data.Row(Data.Relative(files[i]), r!["symbol"].Text(), r["line"]!.GetValue<int>(), r["documented"]!.GetValue<bool>() ? [] : ["docstring"])).ToList()).ToArray();
    }

    internal static int Gate(string? baseline)
    {
        var rows = Data.Json(Data.At("build/api-audit.json")).AsArray().Select(r => r!.DeepClone()).ToList();
        rows.AddRange(PythonRows(Files(false), false).SelectMany(x => x));
        rows = rows.OrderBy(r => r["path"].Text(), Data.Order).ThenBy(r => r["symbol"].Text(), Data.Order).ToList();
        var current = rows.SelectMany(r => r["issues"]!.AsArray().Select(i => r["path"].Text() + "::" + r["symbol"].Text() + "::" + i.Text())).ToHashSet();
        var previous = Data.Json(baseline ?? Data.At("docs/api-documentation-baseline.json")).AsArray().Select(x => x.Text()).ToHashSet();
        var generated = current.Where(k => k.Contains("/Generated/", StringComparison.Ordinal) || k.Contains(".g.cs::", StringComparison.Ordinal)).ToHashSet();
        var added = current.Except(previous).ToArray(); var removed = previous.Except(current).Count();
        Data.Write(Data.At("build/documentation-audit.json"), new JsonArray(rows.ToArray()));
        Console.WriteLine($"Documentation: {rows.Count} declarations/modules, {current.Count} gaps, {added.Length} new, {removed} resolved, {generated.Count} generated gaps");
        if (added.Length > 0 || generated.Count > 0) { Console.WriteLine(string.Join(Environment.NewLine, added.Union(generated).Order(Data.Order))); return 1; }
        if (removed > 0) Console.WriteLine("Remove resolved entries from docs/api-documentation-baseline.json after review.");
        return 0;
    }

    private static string Docs(string[] lines, int line)
    {
        var i = line - 2;
        while (i >= 0 && (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith('['))) i--;
        var docs = new List<string>();
        while (i >= 0 && lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal)) { docs.Add(lines[i][(lines[i].IndexOf("///", StringComparison.Ordinal) + 3)..]); i--; }
        docs.Reverse(); return string.Join("\n", docs);
    }

    private static string[] Parameters(string signature)
    {
        var parts = new List<string>(); var chunk = new StringBuilder(); var depth = 0;
        foreach (var c in signature + ",")
        {
            if ("<[(".Contains(c)) depth++; else if (">])".Contains(c)) depth = Math.Max(0, depth - 1);
            if (c == ',' && depth == 0) { parts.Add(chunk.ToString()); chunk.Clear(); } else chunk.Append(c);
        }
        return parts.Select(p => Regex.Matches(p.Split('=')[0], @"\b\w+\b").Select(m => m.Value).LastOrDefault()).Where(p => p is not null).Select(p => p!).ToArray();
    }

    private static List<JsonNode> CSharp(string path)
    {
        var source = Data.Read(path).TrimStart('\uFEFF');
        var masked = LITERALS.Replace(source, m => new string(m.Value.Select(c => c == '\n' ? '\n' : ' ').ToArray()));
        var lines = Regex.Split(source, "\\r\\n|[\\n\\r\\v\\f\\x1c-\\x1e\\x85\\u2028\\u2029]");
        var newlines = masked.Select((c, i) => (c, i)).Where(p => p.c == '\n').Select(p => p.i).ToArray();
        int Line(int offset) { var n = Array.BinarySearch(newlines, offset); return (n < 0 ? ~n : n) + 1; }
        var rows = new List<JsonNode>();
        foreach (Match match in DECL.Matches(masked))
        {
            var line = Line(match.Index); var docs = Docs(lines, line); var issues = new List<string>();
            if (!Match(docs, @"<inheritdoc\s*/?>"))
            {
                if (!Match(docs, @"<summary(?:\s+[^>]*)?>\s*[^<\s]")) issues.Add("summary");
                if (match.Groups[5].Success)
                {
                    var documented = PARAM.Matches(docs).Select(m => m.Groups[1].Value).ToHashSet();
                    issues.AddRange(Parameters(match.Groups[5].Value).Where(p => !documented.Contains(p)).Select(p => "param:" + p));
                    var before = masked[match.Index..match.Groups[4].Index];
                    if (!Match(before, @"\bvoid\s*$") && !docs.Contains("<returns>", StringComparison.Ordinal)) issues.Add("returns");
                }
            }
            if (issues.Count > 0) rows.Add(Data.Row(Data.Relative(path), new[] { 3, 4, 6 }.Select(n => match.Groups[n].Value).First(v => v.Length > 0), line, issues));
        }
        foreach (var (pattern, record) in new[] { (RECORD_PRIMARY, true), (CONSTRUCTOR, false) })
        foreach (Match match in pattern.Matches(masked))
        {
            var line = Line(match.Index); var docs = Docs(lines, line);
            if (Match(docs, @"<inheritdoc\s*/?>")) continue;
            var documented = PARAM.Matches(docs).Select(m => m.Groups[1].Value).ToHashSet();
            var missing = Parameters(match.Groups["params"].Value).Where(p => !documented.Contains(p)).Select(p => "param:" + p).ToList();
            if (!record && !Match(docs, @"<summary(?:\s+[^>]*)?>\s*[^<\s]")) missing.Insert(0, "summary");
            if (missing.Count == 0) continue;
            var name = match.Groups["name"].Value;
            var current = rows.FirstOrDefault(r => r["line"]!.GetValue<int>() == line && r["symbol"].Text() == name);
            if (current is null) rows.Add(Data.Row(Data.Relative(path), name, line, missing));
            else foreach (var issue in missing) if (!current["issues"]!.AsArray().Any(x => x.Text() == issue)) current["issues"]!.AsArray().Add((JsonNode?)JsonValue.Create(issue));
        }
        return rows;
    }

    internal static int Preflight(string? output, bool strict)
    {
        var files = Files(true); var python = files.Where(p => Path.GetExtension(p) == ".py").ToArray();
        // CPython owns grammar parsing only; C# performs the checks and merges reports.
        var pythonTask = Task.Run(() => PythonRows(python, true));
        var cs = files.Where(p => Path.GetExtension(p) == ".cs").ToArray();
        var csRows = Program.Map(cs, CSharp); var pyRows = pythonTask.GetAwaiter().GetResult();
        var byFile = cs.Select((p, i) => (p, rows: csRows[i])).Concat(python.Select((p, i) => (p, rows: pyRows[i]))).ToDictionary(x => x.p, x => x.rows);
        var findings = files.SelectMany(p => byFile[p]).ToArray();
        var gaps = findings.Sum(r => r["issues"]!.AsArray().Count);
        var report = new JsonObject { ["checker"] = "portable-lexical-preflight", ["authoritative"] = false, ["files"] = new JsonObject { ["cs"] = cs.Length, ["py"] = python.Length }, ["gapCount"] = gaps, ["findings"] = new JsonArray(findings) };
        if (output is not null) Data.Write(output, report);
        Console.WriteLine($"Portable documentation preflight: {{'cs': {cs.Length}, 'py': {python.Length}}}, {gaps} gaps; Roslyn CI remains authoritative");
        foreach (var row in findings.Take(50)) Console.WriteLine($"{row["path"].Text()}:{row["line"]}: {string.Join(", ", row["issues"]!.AsArray().Select(x => x.Text()))}");
        return strict && gaps > 0 ? 1 : 0;
    }
}
