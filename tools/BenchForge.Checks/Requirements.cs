using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class Requirements
{
    private static string[] Matches(string source, string pattern) => Regex.Matches(source, pattern, RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
    private static bool Full(string value, string pattern) => Regex.IsMatch(value, "\\A(?:" + pattern + ")\\z");

    internal static int Run()
    {
        var source = Data.Read(Data.At("requirements/benchdouble.sdoc"));
        var ids = Matches(source, @"^UID:\s+([A-Z][A-Z0-9-]+)$").Where(x => x != "BD-BASELINE").ToArray();
        var errors = new List<string>();
        if (ids.Length != ids.Distinct().Count()) errors.Add("Duplicate requirement UID in StrictDoc source");
        // Independent CSV reads and evidence existence probes share a bounded worker budget.
        var tables = Program.Map(new[] { "requirements/traceability.csv", "requirements/legacy-runner-audit.csv" }, p => Data.Csv(Data.At(p)));
        var rows = tables[0]; var audited = tables[1];
        var byId = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var paths = rows.SelectMany(r => new[] { r["implementation_file"].Trim(), r["verification_file"].Trim() })
            .Concat(audited.SelectMany(r => new[] { r["source_evidence"].Trim(), r["test_evidence"].Trim() })).Where(p => p.Length > 0).Distinct().ToArray();
        var exists = Program.Map(paths, p => File.Exists(Data.At(p)));
        var files = paths.Select((p, i) => (p, exists[i])).ToDictionary(x => x.p, x => x.Item2);
        var valid = new HashSet<string>(["Planned", "Unverified", "SourceCommittedUnverified", "PartialSourceUnverified", "DesignOnly", "NoImplementationEvidence", "Verified", "Blocked"]);
        foreach (var row in rows)
        {
            var uid = row["requirement_id"]; var status = row["implementation_status"];
            if (byId.ContainsKey(uid)) errors.Add($"Duplicate traceability row: {uid}");
            byId[uid] = row;
            if (!valid.Contains(status)) errors.Add($"{uid}: invalid status {status}");
            foreach (var column in new[] { "implementation_file", "verification_file" })
            {
                var path = row[column].Trim();
                if (path.Length > 0 && !files[path]) errors.Add($"{uid}: missing {column}: {path}");
            }
            var issue = row["github_issue"];
            if (issue.Length > 0 && !Full(issue, @"https://github\.com/jeffadickson/BenchDouble/issues/[0-9]+")) errors.Add($"{uid}: malformed GitHub issue URL");
            if (issue.Length == 0) errors.Add($"{uid}: requirement lacks GitHub issue");
            if (status != "Verified") continue;
            var evidence = row.GetValueOrDefault("verification_evidence", "").Trim();
            if (row["verification_file"].Length == 0 || evidence.Length == 0) errors.Add($"{uid}: cannot mark Verified without test and execution evidence");
            else
            {
                try
                {
                    var report = Data.Json(Data.At(evidence));
                    if (report is not JsonObject obj) throw new InvalidDataException();
                    bool Includes(JsonNode? node) => node switch
                    {
                        JsonArray a => a.Any(n => n is JsonValue v && v.TryGetValue<string>(out var s) && s == uid),
                        JsonObject o => o.ContainsKey(uid),
                        JsonValue v when v.TryGetValue<string>(out var s) => s.Contains(uid, StringComparison.Ordinal),
                        _ => throw new InvalidDataException()
                    };
                    bool Truth(JsonNode? n) => n switch
                    {
                        null => false, JsonArray a => a.Count > 0, JsonObject o => o.Count > 0,
                        JsonValue v when v.TryGetValue<string>(out var text) => text.Length > 0,
                        _ => n.ToJsonString() is not ("false" or "0" or "0.0" or "-0.0")
                    };
                    bool Passed(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var text) && text == "passed";
                    string Commit()
                    {
                        if (!obj.ContainsKey("tested_commit")) return "";
                        if (obj["tested_commit"] is not JsonValue v || !v.TryGetValue<string>(out var text)) throw new InvalidDataException();
                        return text;
                    }
                    if (!Passed(obj["result"]) || !Full(Commit(), "[0-9a-f]{40}") || !Truth(obj["platform"]) || !Truth(obj["command"]) || !Includes(obj.ContainsKey("requirements") ? obj["requirements"] : new JsonArray()))
                        errors.Add($"{uid}: incomplete passing execution evidence");
                }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                { errors.Add($"{uid}: invalid execution evidence report"); }
            }
        }
        foreach (var uid in ids.Except(byId.Keys).Order(Data.Order)) errors.Add($"Missing traceability row: {uid}");
        foreach (var uid in byId.Keys.Except(ids).Order(Data.Order)) errors.Add($"Unknown traceability requirement: {uid}");
        foreach (var (uid, row) in byId)
            if (row["implementation_status"] == "Planned" && row["github_issue"].Length == 0) errors.Add($"{uid}: planned work lacks GitHub issue");
        var expectedLegacy = Enumerable.Range(1, 90).Select(n => $"TR-{n:000}").ToHashSet();
        var auditedIds = audited.Select(r => r["requirement_id"]).ToArray();
        if (auditedIds.Length != auditedIds.Distinct().Count()) errors.Add("Duplicate requirement in legacy runner audit");
        if (!expectedLegacy.SetEquals(auditedIds)) errors.Add("Legacy runner audit must contain exactly TR-001 through TR-090");
        var statuses = new Dictionary<string, string> { ["PartialSource"] = "PartialSourceUnverified", ["DesignOnly"] = "DesignOnly", ["NoImplementationEvidence"] = "NoImplementationEvidence" };
        foreach (var audit in audited)
        {
            var uid = audit["requirement_id"];
            if (!statuses.TryGetValue(audit["audit_assessment"], out var status)) { errors.Add($"{uid}: unknown audit assessment"); continue; }
            if (!byId.TryGetValue(uid, out var row)) { errors.Add($"{uid}: audited requirement missing from traceability"); continue; }
            if (row["implementation_status"] != status) errors.Add($"{uid}: audit/traceability status mismatch");
            foreach (var (a, b, label) in new[] { ("implementation_file", "source_evidence", "source"), ("verification_file", "test_evidence", "test"), ("github_issue", "tracking_issue", "issue") })
                if (row[a] != audit[b]) errors.Add($"{uid}: audit/traceability {label} mismatch");
            if (audit["remaining_gap"].Trim().Length == 0) errors.Add($"{uid}: missing specific audit gap");
            foreach (var field in new[] { "source_evidence", "test_evidence" })
            {
                var path = audit[field].Trim();
                if (path.Length > 0 && !files[path]) errors.Add($"{uid}: missing audited evidence path {path}");
            }
            if (status == "PartialSourceUnverified" && audit["source_evidence"].Length == 0) errors.Add($"{uid}: partial source claim lacks source evidence");
        }
        if (rows.Any(r => r["implementation_status"] == "Verified")) Console.WriteLine("NOTICE: Verified statuses require separately reviewed passing CI evidence.");
        var statements = new Dictionary<string, string>();
        foreach (var block in source.Split("[REQUIREMENT]").Skip(1))
        {
            var uid = Matches(block, "^UID: (.+)$"); var statement = Matches(block, "^STATEMENT: (.+)$");
            if (uid.Length > 0 && statement.Length > 0) statements[uid[0]] = statement[0];
        }
        var fullPath = Data.At("requirements/full-audit.csv");
        if (!File.Exists(fullPath)) errors.Add("Missing complete requirements audit");
        else
        {
            var full = Data.Csv(fullPath); var fullIds = full.Select(r => r["requirement_id"]).ToArray();
            if (fullIds.Length != fullIds.Distinct().Count() || !ids.ToHashSet().SetEquals(fullIds)) errors.Add("Full audit must contain every requirement exactly once");
            foreach (var audit in full)
            {
                var uid = audit["requirement_id"]; var row = byId.GetValueOrDefault(uid, []);
                foreach (var field in new[] { "priority", "implementation_status", "implementation_file", "verification_file", "github_issue" })
                    if (audit.GetValueOrDefault(field) != row.GetValueOrDefault(field)) errors.Add($"{uid}: full audit/traceability {field} mismatch");
                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(statements.GetValueOrDefault(uid, ""))));
                if (audit.GetValueOrDefault("requirement_sha256") != hash) errors.Add($"{uid}: requirement changed since full audit");
                if (audit.GetValueOrDefault("finding", "").Trim().Length == 0 || audit.GetValueOrDefault("acceptance_criteria", "").Trim().Length == 0) errors.Add($"{uid}: full audit lacks finding or acceptance criteria");
            }
        }
        if (errors.Count > 0) { foreach (var error in errors) Console.Error.WriteLine("ERROR: " + error); return 1; }
        Console.WriteLine($"Traceability OK: {ids.Length} requirements, {rows.Count(r => r["implementation_status"] == "Verified")} verified, {rows.Count(r => r["implementation_status"] is "SourceCommittedUnverified" or "PartialSourceUnverified")} source-linked unverified, {audited.Count} original runner requirements audited.");
        return 0;
    }
}
