using System.Globalization;
using BenchDouble.Contracts;

namespace BenchDouble;

/// <summary>
/// Generic instrument contract backed by the existing SCPI DMM session.
/// The supported subset is intentionally explicit, with no invented capabilities.
/// </summary>
public sealed class BasicDmmAdapter : IDigitalMultimeter, IRawScpiInstrument
{
    private readonly IScpiSession _session;
    private readonly DigitalMultimeter _driver;
    public BasicDmmAdapter(IScpiSession session, InstrumentBackend backend = InstrumentBackend.Simulated)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _driver = new DigitalMultimeter(session);
        Backend = backend;
    }

    public InstrumentIdentity Identity
    {
        get
        {
            var values = _driver.Identify().Split(',');
            return new InstrumentIdentity(values.ElementAtOrDefault(0) ?? "Unknown",
                values.ElementAtOrDefault(1) ?? "Unknown", values.ElementAtOrDefault(2) ?? "",
                values.ElementAtOrDefault(3) ?? "");
        }
    }

    public InstrumentBackend Backend { get; }
    public IReadOnlyDictionary<string, InstrumentFeature> Features { get; } =
        new Dictionary<string, InstrumentFeature>
        {
            ["dmm.voltage.dc"] = new("dmm.voltage.dc", "Measure DC voltage", "V")
        };

    public void Configure(MeasurementFunction function, double? range = null, double? resolution = null)
    {
        if (function != MeasurementFunction.DcVoltage)
            throw new NotSupportedException($"Measurement function {function} is unavailable in this profile.");
        if (range is not null || resolution is not null)
            throw new NotSupportedException("Explicit range and resolution are not implemented.");
        _driver.ConfigureDcVoltage();
    }

    public double Measure() => _driver.MeasureDcVoltage();
    public void Reset() => _session.Write("*RST");
    public void WriteScpi(string command) => _session.Write(command);
    public string QueryScpi(string command) => _session.Query(command);
    public void Dispose() { } // Caller owns the underlying session.
}

/// <summary>
/// Wraps the minimal sine-wave SCPI driver with generic channel-based operations.
/// </summary>
public sealed class BasicFunctionGeneratorAdapter : IFunctionGenerator, IRawScpiInstrument
{
    private readonly IScpiSession _session;
    public BasicFunctionGeneratorAdapter(IScpiSession session, InstrumentBackend backend = InstrumentBackend.Simulated)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Backend = backend;
        var driver = new FunctionGenerator(session);
        Channels = new IFunctionGeneratorChannel[] { new Channel(driver, 1), new Channel(driver, 2) };
    }

    public IReadOnlyList<IFunctionGeneratorChannel> Channels { get; }
    public InstrumentBackend Backend { get; }
    public InstrumentIdentity Identity
    {
        get
        {
            var values = _session.Query("*IDN?").Split(',');
            return new InstrumentIdentity(values.ElementAtOrDefault(0) ?? "Unknown",
                values.ElementAtOrDefault(1) ?? "Unknown", values.ElementAtOrDefault(2) ?? "",
                values.ElementAtOrDefault(3) ?? "");
        }
    }
    public IReadOnlyDictionary<string, InstrumentFeature> Features { get; } =
        new Dictionary<string, InstrumentFeature>
        {
            ["fgen.sine"] = new("fgen.sine", "Configure sine frequency and peak-to-peak voltage", "Hz"),
            ["fgen.channel.output"] = new("fgen.channel.output", "Output enable")
        };
    public void Reset() => _session.Write("*RST");
    public void WriteScpi(string command) => _session.Write(command);
    public string QueryScpi(string command) => _session.Query(command);
    public void Dispose() { } // Caller owns the underlying session.

    private sealed class Channel(FunctionGenerator driver, int number) : IFunctionGeneratorChannel
    {
        public int Number => number;
        public void ConfigureSine(double frequencyHz, double amplitudeVpp, double offsetVolts = 0)
        {
            if (offsetVolts != 0)
                throw new NotSupportedException("The current SCPI profile does not support DC offset.");
            driver.SetSine(frequencyHz, amplitudeVpp, number);
        }
        public void SetEnabled(bool enabled) => driver.Output(enabled, number);
    }
}
