using System.Globalization;

namespace BenchDouble;

public interface IScpiSession
{
    void Write(string command);
    string Query(string command);
}

public sealed class ScpiProtocolException(string message) : Exception(message);

public sealed class StrictSession(IEnumerable<ExpectedExchange> exchanges) : IScpiSession
{
    private readonly Queue<ExpectedExchange> _pending = new(exchanges);
    public int Remaining => _pending.Count;

    public void Write(string command) => Match(command, false);
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

    public void VerifyComplete()
    {
        if (_pending.Count != 0)
            throw new ScpiProtocolException($"{_pending.Count} expected operations were not observed. Next: {_pending.Peek().Command}");
    }
}

public sealed record ExpectedExchange(string Command, bool IsQuery = false, string? Response = null);

public sealed class SharedTranscript
{
    private readonly Queue<(string Instrument, ExpectedExchange Exchange)> _queue;
    public SharedTranscript(params (string Instrument, ExpectedExchange Exchange)[] entries) => _queue = new(entries);
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

public sealed class FunctionGenerator(IScpiSession session)
{
    public string Identify() => session.Query("*IDN?");
    public void SetSine(double frequencyHz, double amplitudeVpp, int channel = 1)
    {
        if (channel is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        if (!double.IsFinite(amplitudeVpp) || amplitudeVpp <= 0) throw new ArgumentOutOfRangeException(nameof(amplitudeVpp));
        session.Write($"SOUR{channel}:FUNC SIN");
        session.Write($"SOUR{channel}:FREQ {frequencyHz.ToString("G17", CultureInfo.InvariantCulture)}");
        session.Write($"SOUR{channel}:VOLT {amplitudeVpp.ToString("G17", CultureInfo.InvariantCulture)}");
    }
    public void Output(bool enabled, int channel = 1)
    {
        if (channel is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        session.Write($"OUTP{channel} {(enabled ? "ON" : "OFF")}");
    }
}

public sealed class DigitalMultimeter(IScpiSession session)
{
    public string Identify() => session.Query("*IDN?");
    public void ConfigureDcVoltage() => session.Write("CONF:VOLT:DC");
    public double MeasureDcVoltage() => double.Parse(session.Query("MEAS:VOLT:DC?"), CultureInfo.InvariantCulture);
}

public abstract class StatefulScpiDevice : IScpiSession
{
    public List<string> Transcript { get; } = new();
    public bool FailNextOperation { get; set; }
    protected void Before(string cmd)
    {
        Transcript.Add(cmd);
        if (FailNextOperation)
        {
            FailNextOperation = false;
            throw new TimeoutException($"Simulated communication timeout: {cmd}");
        }
    }
    public abstract void Write(string command);
    public abstract string Query(string command);
}

public sealed class GeneratorSimulator : StatefulScpiDevice
{
    public readonly Dictionary<int, double> Frequencies = new() { [1] = 1000, [2] = 1000 };
    public readonly Dictionary<int, double> Amplitudes = new() { [1] = 1, [2] = 1 };
    public readonly Dictionary<int, bool> Outputs = new() { [1] = false, [2] = false };
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
    public override string Query(string command)
    {
        Before(command);
        if (command == "*IDN?") return "Keysight Technologies,33522B,SIM0001,0.1";
        throw new ScpiProtocolException($"Unsupported generator query: {command}");
    }
}

public sealed class MultimeterSimulator : StatefulScpiDevice
{
    public double InjectedVoltage { get; set; }
    public bool Configured { get; private set; }
    public override void Write(string command)
    {
        Before(command);
        if (command == "*RST") Configured = false;
        else if (command == "CONF:VOLT:DC") Configured = true;
        else throw new ScpiProtocolException($"Unsupported DMM command: {command}");
    }
    public override string Query(string command)
    {
        Before(command);
        if (command == "*IDN?") return "Keysight Technologies,34461A,SIM0002,0.1";
        if (command == "MEAS:VOLT:DC?")
            return InjectedVoltage.ToString("G17", CultureInfo.InvariantCulture);
        throw new ScpiProtocolException($"Unsupported DMM query: {command}");
    }
}
