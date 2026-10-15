using System.Globalization;
using BenchDouble.Instruments.Keithley;

namespace BenchDouble.Simulation;

/// <summary>
/// Simplified 236 bus-level test double. This models a resistive DUT, not calibrated
/// hardware. Commands are the legacy Keithley syntax, not SCPI.
/// </summary>
public sealed class Keithley236MockBus : IKeithley236Bus
{
    public List<string> Transcript { get; } = new();
    public SourceMeasureMode Mode { get; private set; } = SourceMeasureMode.SourceVoltageMeasureCurrent;
    public bool Operate { get; private set; }
    public double Source { get; private set; }
    public double Compliance { get; private set; }
    public double ResistanceOhms { get; set; } = 1000;
    public IntegrationSpeed Integration { get; private set; } = IntegrationSpeed.Medium;
    private bool _readPrepared;

    public ValueTask WriteAsync(string command, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Transcript.Add("W:" + command);
        if (command == "N0X") { Operate = false; return ValueTask.CompletedTask; }
        if (command == "N1X") { if (Compliance <= 0) throw new InvalidOperationException("Compliance not configured."); Operate = true; return ValueTask.CompletedTask; }
        if (command == "F0,0X") { Mode = SourceMeasureMode.SourceVoltageMeasureCurrent; Operate = false; return ValueTask.CompletedTask; }
        if (command == "F1,0X") { Mode = SourceMeasureMode.SourceCurrentMeasureVoltage; Operate = false; return ValueTask.CompletedTask; }
        if (command is "S0X" or "S1X" or "S2X" or "S3X") { Integration = (IntegrationSpeed)(command[1] - '0'); return ValueTask.CompletedTask; }
        if (command == "G4,2,0X") { _readPrepared = true; return ValueTask.CompletedTask; }
        if (command.StartsWith('B') && command.EndsWith('X'))
        {
            var values = command[1..^1].Split(',');
            if (values.Length != 3 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var source) ||
                values[1] != "0" || !double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var delay) ||
                !double.IsFinite(source) || !double.IsFinite(delay) || delay < 0 || delay > 65000)
                throw new FormatException("Invalid B command");
            Source = source;
            return ValueTask.CompletedTask;
        }
        if (command.StartsWith('L') && command.EndsWith(",0X", StringComparison.Ordinal))
        {
            if (!double.TryParse(command[1..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var compliance) ||
                !double.IsFinite(compliance) || compliance <= 0)
                throw new FormatException("Invalid L command");
            Compliance = compliance;
            return ValueTask.CompletedTask;
        }
        throw new NotSupportedException($"Unsupported Keithley 236 command: {command}");
    }

    public ValueTask<string> ReadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_readPrepared) throw new InvalidOperationException("No data format/read prepared.");
        _readPrepared = false;
        if (!double.IsFinite(ResistanceOhms) || ResistanceOhms <= 0) throw new InvalidOperationException("Invalid mock DUT resistance.");
        double measurement = Operate ? Mode == SourceMeasureMode.SourceVoltageMeasureCurrent
            ? Math.Clamp(Source / ResistanceOhms, -Compliance, Compliance)
            : Math.Clamp(Source * ResistanceOhms, -Compliance, Compliance) : 0;
        Transcript.Add("R");
        return ValueTask.FromResult(measurement.ToString("G17", CultureInfo.InvariantCulture));
    }
}
