using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

internal static class Reports
{
    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#x27;");

    internal static int Scoreboard(string? output)
    {
        var rows = Data.Csv(Data.At("requirements/traceability.csv"));
        var cards = string.Concat(rows.GroupBy(r => r["implementation_status"]).OrderBy(g => g.Key, Data.Order).Select(g => $"<div class=\"stat\"><strong>{g.Count()}</strong>{Escape(g.Key)}</div>"));
        var body = string.Concat(rows.Select(row => "<tr>" + string.Concat(new[] { "requirement_id", "priority", "implementation_status" }.Select(k => "<td>" + Escape(row[k]) + "</td>")) + $"<td><a href=\"{Escape(row["github_issue"])}\">Tracked work</a></td></tr>"));
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BenchForge.Checks.scoreboard.html")!;
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Replace("\r\n", "\n").Replace("\r", "\n").Replace("@@CARDS@@", cards).Replace("@@TOTAL@@", rows.Count.ToString()).Replace("@@BODY@@", body);
        var path = Path.GetFullPath(output ?? Data.At("build/requirements-scoreboard.html"));
        Data.WriteText(path, text); Console.WriteLine(path); return 0;
    }

    internal static int Neutrality(string? output, bool strict)
    {
        const string legal = "THIRD_PARTY_REVIEW.md";
        var term = "Py" + "Measure";
        bool Contains(string s) => s.Replace("ſ", "s").Contains(term, StringComparison.OrdinalIgnoreCase);
        var names = Data.Run("git", ["ls-files", "-z"]).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var batches = Program.Map(names, name =>
        {
            var rows = new List<JsonNode>();
            if (name == legal) return rows;
            JsonObject Finding(int line, string kind) => new() { ["path"] = name, ["line"] = line, ["kind"] = kind };
            if (Contains(name)) rows.Add(Finding(0, "filename"));
            try
            {
                var raw = File.ReadAllBytes(Data.At(name));
                // Preserve the reference's literal backslash-zero binary exclusion.
                if (raw.Zip(raw.Skip(1)).Any(p => p.First == 92 && p.Second == 48)) return rows;
                var text = new UTF8Encoding(false, true).GetString(raw);
                var lines = System.Text.RegularExpressions.Regex.Split(text, "\\r\\n|[\\n\\r\\v\\f\\x1c-\\x1e\\x85\\u2028\\u2029]");
                for (int i = 0; i < lines.Length; i++) if (Contains(lines[i])) rows.Add(Finding(i + 1, "content"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException) { }
            return rows;
        });
        var findings = batches.SelectMany(x => x).ToArray();
        var report = new JsonObject { ["allowedRegister"] = legal, ["violations"] = findings.Length, ["findings"] = new JsonArray(findings) };
        if (output is not null) Data.Write(output, report);
        Console.WriteLine($"Source-neutrality audit: {findings.Length} violation(s)");
        foreach (var row in findings.Take(40)) Console.WriteLine($"  {row["path"].Text()}:{row["line"]} ({row["kind"].Text()})");
        return strict && findings.Length > 0 ? 1 : 0;
    }

    private sealed record Issue(string Path, int Line, string Kind)
    {
        internal JsonObject ToJson() => new() { ["path"] = Path, ["line"] = Line, ["issue"] = Kind };
    }

    internal static int Compare(string roslynPath, string portablePath, string output)
    {
        static Issue[] Expand(JsonNode rows) => rows.AsArray().Where(r => r!["path"].Text().EndsWith(".cs", StringComparison.Ordinal)).SelectMany(r => r!["issues"]?.AsArray().Select(i => new Issue(r["path"].Text(), r["line"]!.GetValue<int>(), i.Text())) ?? []).ToArray();
        var expected = Expand(Data.Json(roslynPath)); var observed = Expand(Data.Json(portablePath)["findings"]!);
        var index = observed.Select((x, i) => (x, i)).GroupBy(p => (p.x.Path, p.x.Kind)).ToDictionary(g => g.Key, g => g.ToArray());
        var used = new HashSet<int>(); var matched = new List<Issue>(); var missed = new List<Issue>();
        foreach (var row in expected)
        {
            var candidate = index.GetValueOrDefault((row.Path, row.Kind), []).Where(p => !used.Contains(p.i) && Math.Abs(row.Line - p.x.Line) <= 3).OrderBy(p => Math.Abs(row.Line - p.x.Line)).ThenBy(p => p.i).ToArray();
            if (candidate.Length > 0) { used.Add(candidate[0].i); matched.Add(row); } else missed.Add(row);
        }
        var extra = observed.Where((_, i) => !used.Contains(i)).ToArray();
        var files = new SortedDictionary<string, JsonObject>(Data.Order);
        void Count(IEnumerable<Issue> rows, string kind)
        {
            foreach (var row in rows)
            {
                if (!files.TryGetValue(row.Path, out var value)) files[row.Path] = value = new() { ["roslyn"] = 0, ["matched"] = 0, ["missed"] = 0, ["portableOnly"] = 0 };
                value[kind] = value[kind]!.GetValue<int>() + 1;
            }
        }
        Count(matched.Concat(missed), "roslyn"); Count(matched, "matched"); Count(missed, "missed"); Count(extra, "portableOnly");
        var byFile = new JsonObject(); foreach (var (path, counts) in files) byFile[path] = counts;
        var kinds = new JsonObject(); foreach (var group in expected.GroupBy(x => x.Kind)) kinds[group.Key] = group.Count();
        double recall = expected.Length > 0 ? (double)matched.Count / expected.Length : 1.0;
        double precision = observed.Length > 0 ? (double)matched.Count / observed.Length : expected.Length == 0 ? 1.0 : 0.0;
        JsonArray Array(IEnumerable<Issue> rows) => new(rows.Select(r => (JsonNode)r.ToJson()).ToArray());
        var report = new JsonObject
        {
            ["method"] = "C# issue-level path/line/category comparison, tolerance +/- 3 lines",
            ["limitations"] = new JsonArray("Portable scanner is lexical and may miss C# syntax constructs.", "Line-tolerant matching is diagnostic and not proof of parser equivalence.", "Python docstrings are checked independently by Python AST."),
            ["roslynIssueCount"] = expected.Length, ["portableIssueCount"] = observed.Length, ["matched"] = matched.Count,
            ["recall"] = recall, ["precision"] = precision, ["missed"] = Array(missed), ["portableOnly"] = Array(extra), ["byFile"] = byFile, ["roslynIssuesByKind"] = kinds
        };
        Data.Write(output, report);
        Console.WriteLine($"Portable/Roslyn parity: recall={recall * 100:F1}% precision={precision * 100:F1}%, {missed.Count} missed, {extra.Length} portable-only");
        foreach (var row in missed.Take(20)) Console.WriteLine($"  missed: {row.Path}:{row.Line} {row.Kind}");
        return 0;
    }
}
