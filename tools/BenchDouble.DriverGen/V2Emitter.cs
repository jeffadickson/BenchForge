using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;

/// <summary>Schema-v2, simulation-only source emitter. No unreviewed definition can control hardware.</summary>
internal static class V2Emitter
{
    private static readonly Dictionary<string, (string Property, string Factory)> Units = new(StringComparer.Ordinal)
    {
        ["ElectricPotential:Volt"] = ("Volts", "FromVolts"),
        ["ElectricResistance:Ohm"] = ("Ohms", "FromOhms"),
        ["ElectricCurrent:Ampere"] = ("Amperes", "FromAmperes"),
        ["Frequency:Hertz"] = ("Hertz", "FromHertz")
    };
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? "";
    private static string Str(string value) => JsonSerializer.Serialize(value);
    private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private sealed record Capability(string Name, string Quantity, string Unit, string Description,
        string Basis, string[] Modes, double? Initial, string Query);

    /// <summary>Emit reviewed-mode, typed preview classes from v2 definitions in a bounded pipeline.</summary>
    internal static async Task<int> RunAsync(string directory, string output, bool check)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var files = Directory.GetFiles(directory, "*.definition.json", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new InvalidDataException("No schema-v2 definitions.");
        var block = new TransformBlock<string, (string Name, string Source)>(file => Generate(file),
            new ExecutionDataflowBlockOptions { BoundedCapacity = 8, MaxDegreeOfParallelism = 2, EnsureOrdered = true });
        var pending = new List<(string Name, string Source)>();
        var sink = new ActionBlock<(string Name, string Source)>(item => pending.Add(item),
            new ExecutionDataflowBlockOptions { BoundedCapacity = 8, MaxDegreeOfParallelism = 1 });
        block.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        try
        {
            foreach (var file in files)
                if (!await block.SendAsync(file).ConfigureAwait(false))
                    throw new InvalidOperationException("Generator pipeline rejected input.");
            block.Complete();
            await sink.Completion.ConfigureAwait(false);
        }
        catch
        {
            ((IDataflowBlock)block).Fault(new InvalidOperationException("Generator stopped."));
            try { await sink.Completion.ConfigureAwait(false); } catch { }
            throw;
        }
        if (pending.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != pending.Count)
            throw new InvalidDataException("Duplicate generated driver names.");
        // Validate every definition before modifying the output directory.
        if (!check) Directory.CreateDirectory(output);
        foreach (var (name, source) in pending)
        {
            var destination = Path.Combine(output, name + ".Preview.g.cs");
            if (check)
            {
                if (!File.Exists(destination) || File.ReadAllText(destination) != source)
                    throw new InvalidDataException("Stale v2 generated file: " + destination);
            }
            else if (!File.Exists(destination) || File.ReadAllText(destination) != source)
            {
                var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, source, new UTF8Encoding(false));
                    File.Move(temp, destination, overwrite: true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        Console.WriteLine($"{(check ? "Checked" : "Generated")} {pending.Count} simulation-only v2 preview(s).");
        return 0;
    }

    private static (string Name, string Source) Generate(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2)
            throw new InvalidDataException("Expected schema v2: " + file);
        var name = root.GetProperty("identity").GetProperty("driverName").GetString() ?? "";
        if (!Identifier.IsMatch(name)) throw new InvalidDataException("Invalid driver name: " + file);
        var modes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mode in root.GetProperty("modes").EnumerateArray())
        {
            var id = mode.GetProperty("id").GetString() ?? "";
            if (!Identifier.IsMatch(id) || !modes.Add(id))
                throw new InvalidDataException("Invalid or duplicate mode: " + file);
        }
        if (modes.Count == 0) throw new InvalidDataException("No modes: " + file);
        var capabilities = new List<Capability>();
        foreach (var cap in root.GetProperty("capabilities").EnumerateArray())
        {
            string id = cap.GetProperty("name").GetString() ?? "";
            string quantity = cap.TryGetProperty("quantity", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString()! : "";
            string unit = cap.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : "";
            // Unreviewed quantities are never guessed; they are excluded from typed emission.
            if (quantity.Length == 0 || unit.Length == 0) continue;
            if (!Identifier.IsMatch(id) || !Units.ContainsKey(quantity + ":" + unit))
                throw new InvalidDataException("Unsupported identifier or quantity/unit: " + id);
            var access = cap.GetProperty("access").GetString();
            if (access is not ("read" or "readwrite")) continue;
            var allowed = cap.GetProperty("modes").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            if (allowed.Length == 0 || allowed.Any(x => !modes.Contains(x)))
                throw new InvalidDataException("Missing/invalid mode for " + id);
            double? initial = cap.TryGetProperty("mockDefault", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetDouble() : null;
            if (initial.HasValue && !double.IsFinite(initial.Value))
                throw new InvalidDataException("Nonfinite mock default.");
            string description = cap.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
                ? desc.GetString()! : cap.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String
                    ? notes.GetString()! : "Read " + id + ".";
            string basis = cap.TryGetProperty("basis", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "";
            string query = cap.TryGetProperty("query", out var command) && command.ValueKind == JsonValueKind.String ? command.GetString()! : "";
            if (query.Length == 0 || query.Any(char.IsControl))
                throw new InvalidDataException("Missing or invalid query for " + id);
            capabilities.Add(new Capability(id, quantity, unit, description, basis, allowed, initial, query));
        }
        if (capabilities.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != capabilities.Count)
            throw new InvalidDataException("Duplicate capability names.");
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated />");
        s.AppendLine("// Simulation-only schema-v2 preview. Not a hardware driver.");
        s.AppendLine("using UnitsNet;");
        s.AppendLine("namespace BenchDouble.Generated;");
        s.AppendLine($"/// <summary>Typed simulation-only instrument preview for {Xml(name)}.</summary>");
        s.AppendLine($"public sealed class {name}Preview");
        s.AppendLine("{");
        s.AppendLine("  private readonly System.Threading.SemaphoreSlim _gate = new(1, 1);");
        s.AppendLine("  private string? _mode;");
        foreach (var cap in capabilities)
            if (cap.Initial is { } value)
                s.AppendLine($"  private double _{cap.Name} = {Num(value)};");
        s.AppendLine("  /// <summary>Select a declared instrument mode without physical I/O.</summary>");
        s.AppendLine("  /// <param name=\"mode\">Declared operating mode identifier.</param>");
        s.AppendLine("  /// <param name=\"ct\">Cancellation while waiting for the mode lock.</param>");
        s.AppendLine("  /// <returns>Completion of the simulated state transition.</returns>");
        s.AppendLine("  /// <exception cref=\"NotSupportedException\">The mode is not defined.</exception>");
        s.AppendLine("  public async System.Threading.Tasks.ValueTask ConfigureModeAsync(string mode, System.Threading.CancellationToken ct = default)");
        s.AppendLine("  {");
        s.AppendLine("    if (!(" + string.Join(" || ", modes.Order(StringComparer.Ordinal).Select(m => "mode == " + Str(m))) + ")) throw new System.NotSupportedException(\"Unknown mode.\");");
        s.AppendLine("    await _gate.WaitAsync(ct).ConfigureAwait(false);");
        s.AppendLine("    try { _mode = mode; } finally { _gate.Release(); }");
        s.AppendLine("  }");
        foreach (var cap in capabilities.Where(c => c.Initial.HasValue))
        {
            var (_, factory) = Units[cap.Quantity + ":" + cap.Unit];
            s.AppendLine($"  /// <summary>{Xml(cap.Description)}</summary>");
            s.AppendLine($"  /// <remarks>Simulation-only. Quantity: {Xml(cap.Quantity)}; wire unit: {Xml(cap.Unit)}; basis: {Xml(cap.Basis.Length == 0 ? "unspecified" : cap.Basis)}. Original SCPI query: {Xml(cap.Query)}. No physical I/O.</remarks>");
            s.AppendLine("  /// <param name=\"ct\">Cancellation while waiting for the measurement lock.</param>");
            s.AppendLine($"  /// <returns>Simulated {Xml(cap.Quantity)} in {Xml(cap.Unit)}.</returns>");
            s.AppendLine("  /// <exception cref=\"InvalidOperationException\">The selected mode does not support this reading.</exception>");
            s.AppendLine($"  public async System.Threading.Tasks.ValueTask<{cap.Quantity}> Get{cap.Name}Async(System.Threading.CancellationToken ct = default)");
            s.AppendLine("  {");
            s.AppendLine("    await _gate.WaitAsync(ct).ConfigureAwait(false);");
            s.AppendLine("    try");
            s.AppendLine("    {");
            s.AppendLine("      if (!(" + string.Join(" || ", cap.Modes.Select(m => "_mode == " + Str(m))) + ")) throw new System.InvalidOperationException(\"Incompatible mode.\");");
            s.AppendLine($"      return {cap.Quantity}.{factory}(_{cap.Name});");
            s.AppendLine("    }");
            s.AppendLine("    finally { _gate.Release(); }");
            s.AppendLine("  }");
        }
        s.AppendLine("}");
        return (name, s.ToString().Replace("\r\n", "\n"));
    }
}
