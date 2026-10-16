using System.Text;

/// <summary>Generate opt-in, typed behavior decorators around simulation-only instrument previews.</summary>
internal static class V2MockEmitter
{
    /// <summary>Render a mock decorator without inventing any instrument readings.</summary>
    internal static string Render(string name, IReadOnlyList<V2Emitter.Capability> capabilities)
    {
        var s = new StringBuilder();
        s.AppendLine();
        s.AppendLine($"/// <summary>Injectable, simulation-only mock decorator for {name}.</summary>");
        s.AppendLine("/// <remarks>Preserves the preview's mode validation. Overrides are explicitly supplied by tests; no physical I/O.</remarks>");
        s.AppendLine($"public sealed class {name}MockDecorator");
        s.AppendLine("{");
        s.AppendLine($"  private readonly {name}Preview _inner;");
        s.AppendLine("  private readonly object _stateGate = new();");
        s.AppendLine($"  /// <summary>Wrap an existing generated preview and preserve its mode state.</summary>");
        s.AppendLine($"  /// <param name=\"inner\">The caller-owned simulation preview.</param>");
        s.AppendLine($"  public {name}MockDecorator({name}Preview inner) => _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));");
        s.AppendLine("  /// <summary>Configure the underlying simulation mode.</summary>");
        s.AppendLine("  /// <param name=\"mode\">Declared mode name.</param>");
        s.AppendLine("  /// <param name=\"ct\">Cancellation while waiting for the mode lock.</param>");
        s.AppendLine("  /// <returns>Completion of the mode transition.</returns>");
        s.AppendLine("  public System.Threading.Tasks.ValueTask ConfigureModeAsync(string mode, System.Threading.CancellationToken ct = default) => _inner.ConfigureModeAsync(mode, ct);");
        foreach (var cap in capabilities.Where(c => c.Initial.HasValue))
        {
            var type = cap.Quantity;
            var property = cap.Name;
            s.AppendLine($"  private System.Func<System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<{type}>>? _{property}Behavior;");
            s.AppendLine($"  private {type}? _{property}State;");
            s.AppendLine($"  /// <summary>Set a deterministic typed mock value for {System.Security.SecurityElement.Escape(property)}.</summary>");
            s.AppendLine($"  /// <param name=\"value\">Finite simulated {System.Security.SecurityElement.Escape(cap.Quantity)} in {System.Security.SecurityElement.Escape(cap.Unit)}; no physical I/O.</param>");
            s.AppendLine("  /// <exception cref=\"System.ArgumentOutOfRangeException\">The supplied value is not finite.</exception>");
            s.AppendLine($"  public void SetMock{property}({type} value)");
            s.AppendLine("  {");
            s.AppendLine($"    if (!double.IsFinite(value.{V2Emitter.UnitProperty(cap.Quantity, cap.Unit)})) throw new System.ArgumentOutOfRangeException(nameof(value));");
            s.AppendLine($"    lock (_stateGate) _{property}State = value;");
            s.AppendLine("  }");
            s.AppendLine($"  /// <summary>Clear an explicitly assigned {System.Security.SecurityElement.Escape(property)} value.</summary>");
            s.AppendLine($"  public void ResetMock{property}() {{ lock (_stateGate) _{property}State = null; }}");
            s.AppendLine($"  /// <summary>Inject a test-specific {System.Security.SecurityElement.Escape(property)} measurement behavior.</summary>");
            s.AppendLine("  /// <param name=\"behavior\">Explicit async behavior; null removes the injected behavior.</param>");
            s.AppendLine($"  public void Set{property}Behavior(System.Func<System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<{type}>>? behavior)");
            s.AppendLine($"    => System.Threading.Volatile.Write(ref _{property}Behavior, behavior);");
            s.AppendLine($"  /// <summary>Measure {System.Security.SecurityElement.Escape(property)} with mode checks and optional injected behavior.</summary>");
            s.AppendLine("  /// <param name=\"ct\">Cancellation forwarded to both the preview and injected behavior.</param>");
            s.AppendLine($"  /// <returns>A typed {type} simulated measurement.</returns>");
            s.AppendLine("  /// <exception cref=\"System.InvalidOperationException\">The selected mode does not permit this measurement.</exception>");
            s.AppendLine($"  public async System.Threading.Tasks.ValueTask<{type}> Get{property}Async(System.Threading.CancellationToken ct = default)");
            s.AppendLine("  {");
            s.AppendLine($"    var defaultValue = await _inner.Get{property}Async(ct).ConfigureAwait(false);");
            s.AppendLine($"    var behavior = System.Threading.Volatile.Read(ref _{property}Behavior);");
            s.AppendLine($"    {type}? state;");
            s.AppendLine($"    lock (_stateGate) state = _{property}State;");
            s.AppendLine("    return behavior is not null ? await behavior(ct).ConfigureAwait(false) : state ?? defaultValue;");
            s.AppendLine("  }");
        }
        s.AppendLine("  /// <summary>Clear all injected scalar readings and restore definition defaults.</summary>");
        s.AppendLine("  public void ResetAllMockReadings()");
        s.AppendLine("  {");
        s.AppendLine("    lock (_stateGate)");
        s.AppendLine("    {");
        foreach (var cap in capabilities.Where(c => c.Initial.HasValue))
            s.AppendLine($"      _{cap.Name}State = null;");
        s.AppendLine("    }");
        s.AppendLine("  }");
        s.AppendLine("}");
        return s.ToString();
    }
}
