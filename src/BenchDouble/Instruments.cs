using System.Globalization;

namespace BenchDouble;

/// <summary>Synchronous SCPI command transport; callers must coordinate shared-session access.</summary>
public interface IScpiSession
{
    /// <summary>Write a SCPI command without expecting a response.</summary>
    /// <param name="command">Complete SCPI command text.</param>
    void Write(string command);
    /// <summary>Write a SCPI query and read its response.</summary>
    /// <param name="command">Complete SCPI query command.</param>
    /// <returns>Raw instrument response text.</returns>
    string Query(string command);
}

/// <summary>Report an unexpected SCPI command, response, or transcript mismatch.</summary>
/// <param name="message">Protocol mismatch description.</param>
public sealed class ScpiProtocolException(string message) : Exception(message);

/// <summary>Create a strict simulated SCPI session that enforces an ordered transcript.</summary>
/// <param name="exchanges">Expected SCPI command and response sequence.</param>
public sealed class StrictSession(IEnumerable<ExpectedExchange> exchanges) : IScpiSession
{
    private readonly Queue<ExpectedExchange> _pending = new(exchanges);
    /// <summary>Get the number of unconsumed expected SCPI exchanges.</summary>
    public int Remaining => _pending.Count;

    /// <summary>Match a write against the next expected SCPI exchange.</summary>
    /// <param name="command">Command to validate against the transcript.</param>
    public void Write(string command) => Match(command, false);
    /// <summary>Match a query and return its expected response.</summary>
    /// <param name="command">Query to validate against the transcript.</param>
    /// <returns>Expected response from the matching exchange.</returns>
    public string Query(string command) => Match(command, true);

    private string Match(string command, bool query)
    {
        if (_pending.Count == 0)
            throw new ScpiProtocolException($"Unexpected command: {command}");
        var expected = _pending.Peek();
        if (expected.Command != command || expected.IsQuery != query)
            throw new ScpiProtocolException($"Expected {(expected.IsQuery ? "query" : "write")} '{expected.Command}', received {(query ? "query" : "write")} '{command}'.");
        _pending.Dequeue();
        return expected.Response ?? "";
    }

    /// <summary>Ensure every expected SCPI exchange has been consumed.</summary>
    public void VerifyComplete()
    {
        if (_pending.Count != 0)
            throw new ScpiProtocolException($"{_pending.Count} expected operations were not observed. Next: {_pending.Peek().Command}");
    }
}

/// <summary>Describe one expected SCPI command and optional response.</summary>
/// <param name="Command">Expected SCPI command text.</param>
/// <param name="IsQuery">Whether the operation expects a response.</param>
/// <param name="Response">Optional query response text.</param>
public sealed record ExpectedExchange(string Command, bool IsQuery = false, string? Response = null);

/// <summary>Coordinate ordered SCPI expectations across multiple simulated instruments.</summary>
public sealed class SharedTranscript
{
    private readonly Queue<(string Instrument, ExpectedExchange Exchange)> _queue;
    /// <summary>Initialize an ordered cross-instrument transcript.</summary>
    /// <param name="entries">Ordered pairs of instrument identity and expected exchange.</param>
    public SharedTranscript(params (string Instrument, ExpectedExchange Exchange)[] entries) => _queue = new(entries);
    /// <summary>Create an instrument endpoint sharing the global transcript.</summary>
    /// <param name="name">Instrument identity used to match exchanges.</param>
    /// <returns>Endpoint that validates SCPI operations against the shared transcript.</returns>
    public IScpiSession Instrument(string name) => new Endpoint(this, name);
    public void VerifyComplete()
    {
        if (_queue.Count != 0) throw new ScpiProtocolException($"{_queue.Count} shared expectations remain.");
    }
    private sealed class Endpoint(SharedTranscript owner, string name) : IScpiSession
    {
        public void Write(string command) => Execute(command, false);
        public string Query(string command) => Execute(command, true);
        private string Execute(string cmd, bool query)
        {
            if (owner._queue.Count == 0) throw new ScpiProtocolException($"Unexpected {name}: {cmd}");
            var (instrument, expected) = owner._queue.Peek();
            if (instrument != name || expected.Command != cmd || expected.IsQuery != query)
                throw new ScpiProtocolException($"Expected {instrument}: {expected.Command}; received {name}: {cmd}");
            owner._queue.Dequeue();
            return expected.Response ?? "";
        }
    }
}

/// <summary>Control a two-channel SCPI function generator through a caller-owned session.</summary>
/// <param name="session">SCPI session; no ownership transfer or automatic locking.</param>
public sealed class FunctionGenerator(IScpiSession session)
{
    /// <summary>Query the instrument identification string.</summary>
    /// <returns>Raw SCPI identification response.</returns>
    public string Identify() => session.Query("*IDN?");
    /// <summary>Configure a sine waveform on one of the two supported channels.</summary>
    /// <param name="frequencyHz">Positive finite frequency in hertz.</param>
    /// <param name="amplitudeVpp">Positive finite peak-to-peak amplitude in volts.</param>
    /// <param name="channel">One-based output channel, 1 or 2.</param>
    public void SetSine(double frequencyHz, double amplitudeVpp, int channel = 1)
    {
        if (channel is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        if (!double.IsFinite(amplitudeVpp) || amplitudeVpp <= 0) throw new ArgumentOutOfRangeException(nameof(amplitudeVpp));
        session.Write($"SOUR{channel}:FUNC SIN");
        session.Write($"SOUR{channel}:FREQ {frequencyHz.ToString("G17", CultureInfo.InvariantCulture)}");
        session.Write($"SOUR{channel}:VOLT {amplitudeVpp.ToString("G17", CultureInfo.InvariantCulture)}");
    }
    /// <summary>Enable or disable one function-generator output channel.</summary>
    /// <param name="enabled">True to enable output; false to disable it.</param>
    /// <param name="channel">One-based output channel, 1 or 2.</param>
    public void Output(bool enabled, int channel = 1)
    {
        if (channel is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        session.Write($"OUTP{channel} {(enabled ? "ON" : "OFF")}");
    }
}

/// <summary>Control DC voltage measurements using a caller-owned SCPI session.</summary>
/// <param name="session">SCPI session; no ownership transfer or automatic locking.</param>
public sealed class DigitalMultimeter(IScpiSession session)
{
    /// <summary>Query the multimeter identification string.</summary>
    /// <returns>Raw SCPI identification response.</returns>
    public string Identify() => session.Query("*IDN?");
    /// <summary>Configure the multimeter for DC voltage measurement.</summary>
    public void ConfigureDcVoltage() => session.Write("CONF:VOLT:DC");
    /// <summary>Measure DC voltage using a SCPI query.</summary>
    /// <returns>Measured DC potential in volts.</returns>
    public double MeasureDcVoltage() => double.Parse(session.Query("MEAS:VOLT:DC?"), CultureInfo.InvariantCulture);
}

/// <summary>Base for stateful simulated SCPI transports with optional injected timeouts.</summary>
public abstract class StatefulScpiDevice : IScpiSession
{
    /// <summary>Get the mutable transcript of simulated SCPI commands.</summary>
    public List<string> Transcript { get; } = new();
    /// <summary>Get or set whether the next simulated operation throws a timeout.</summary>
    public bool FailNextOperation { get; set; }
    /// <summary>Record an incoming command and inject an optional simulated timeout.</summary>
    /// <param name="cmd">SCPI command about to execute.</param>
    protected void Before(string cmd)
    {
        Transcript.Add(cmd);
        if (FailNextOperation)
        {
            FailNextOperation = false;
            throw new TimeoutException($"Simulated communication timeout: {cmd}");
        }
    }
    /// <summary>Execute a simulated SCPI write command.</summary>
    /// <param name="command">SCPI command text.</param>
    public abstract void Write(string command);
    /// <summary>Execute a simulated SCPI query command.</summary>
    /// <param name="command">SCPI query command.</param>
    /// <returns>Simulated SCPI response text.</returns>
    public abstract string Query(string command);
}

/// <summary>Simulate two function-generator channels with frequency, amplitude, and output state.</summary>
public sealed class GeneratorSimulator : StatefulScpiDevice
{
    /// <summary>Get per-channel simulated frequency values in hertz.</summary>
    public readonly Dictionary<int, double> Frequencies = new() { [1] = 1000, [2] = 1000 };
    /// <summary>Get per-channel simulated peak-to-peak amplitudes in volts.</summary>
    public readonly Dictionary<int, double> Amplitudes = new() { [1] = 1, [2] = 1 };
    /// <summary>Get per-channel simulated output enable states.</summary>
    public readonly Dictionary<int, bool> Outputs = new() { [1] = false, [2] = false };
    /// <summary>Apply a supported generator SCPI command to simulated state.</summary>
    /// <param name="command">Generator command, including optional reset.</param>
    public override void Write(string command)
    {
        Before(command);
        if (command == "*RST")
        {
            foreach (int ch in Outputs.Keys.ToArray())
            {
                Frequencies[ch] = 1000;
                Amplitudes[ch] = 1;
                Outputs[ch] = false;
            }
            return;
        }
        var words = command.Split(' ', 2);
        if (words.Length != 2) throw new ScpiProtocolException($"Unrecognized generator write: {command}");
        var path = words[0].ToUpperInvariant();
        var value = words[1].Trim();
        if (path.Length == 5 && path.StartsWith("OUTP") && int.TryParse(path[4..], out int outChannel) && Outputs.ContainsKey(outChannel))
        {
            if (value != "ON" && value != "OFF") throw new ScpiProtocolException("Invalid output state");
            Outputs[outChannel] = value == "ON";
            return;
        }
        if (path.Length < 7 || !path.StartsWith("SOUR") || !int.TryParse(path[4..5], out int channel) || !Outputs.ContainsKey(channel))
            throw new ScpiProtocolException($"Unrecognized generator write: {command}");
        var suffix = path[5..];
        if (suffix == ":FUNC" && value == "SIN") return;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric) || !double.IsFinite(numeric) || numeric <= 0)
            throw new ScpiProtocolException($"Invalid numeric value: {value}");
        if (suffix == ":FREQ") Frequencies[channel] = numeric;
        else if (suffix == ":VOLT") Amplitudes[channel] = numeric;
        else throw new ScpiProtocolException($"Unrecognized generator write: {command}");
    }
    /// <summary>Return the supported generator identification response.</summary>
    /// <param name="command">Generator SCPI query.</param>
    /// <returns>Simulated instrument identification string.</returns>
    public override string Query(string command)
    {
        Before(command);
        if (command == "*IDN?") return "Keysight Technologies,33522B,SIM0001,0.1";
        throw new ScpiProtocolException($"Unsupported generator query: {command}");
    }
}

/// <summary>Simulate DC voltage measurement and configuration state.</summary>
public sealed class MultimeterSimulator : StatefulScpiDevice
{
    /// <summary>Get or set the simulated DC voltage reading in volts.</summary>
    public double InjectedVoltage { get; set; }
    /// <summary>Get whether DC voltage configuration has been selected.</summary>
    public bool Configured { get; private set; }
    /// <summary>Apply a multimeter configuration or reset command.</summary>
    /// <param name="command">Supported DMM SCPI write command.</param>
    public override void Write(string command)
    {
        Before(command);
        if (command == "*RST") Configured = false;
        else if (command == "CONF:VOLT:DC") Configured = true;
        else throw new ScpiProtocolException($"Unsupported DMM command: {command}");
    }
    /// <summary>Return the DMM identity or simulated DC voltage reading.</summary>
    /// <param name="command">Supported DMM SCPI query.</param>
    /// <returns>Simulated SCPI response.</returns>
    public override string Query(string command)
    {
        Before(command);
        if (command == "*IDN?") return "Keysight Technologies,34461A,SIM0002,0.1";
        if (command == "MEAS:VOLT:DC?")
            return InjectedVoltage.ToString("G17", CultureInfo.InvariantCulture);
        throw new ScpiProtocolException($"Unsupported DMM query: {command}");
    }
}
