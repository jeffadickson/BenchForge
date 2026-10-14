using System.Globalization;

namespace BenchDouble.Ivi;

/// <summary>IVI-class-inspired adapter to the existing selected-subset Keysight generator SCPI driver.</summary>
public sealed class ScpiFunctionGeneratorAdapter : IIviFunctionGenerator, IInstrumentCapabilities
{
    private readonly IScpiSession _session;
    private readonly FunctionGenerator _generator;
    public ScpiFunctionGeneratorAdapter(IScpiSession session, bool simulationEnabled = false)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _generator = new FunctionGenerator(session);
        SimulationEnabled = simulationEnabled;
    }
    public bool SimulationEnabled { get; }
    public string Identity => _generator.Identify();
    public InstrumentCapabilities Capabilities => new("IviFgen", 2);

    public void ConfigureWaveform(int channel, IviWaveform shape, double frequencyHz, double amplitudeVpp)
    {
        if (shape != IviWaveform.Sine)
            throw new NotSupportedException($"The current SCPI profile does not implement {shape}.");
        _generator.SetSine(frequencyHz, amplitudeVpp, channel);
    }
    public void SetOutputEnabled(int channel, bool enabled) => _generator.Output(enabled, channel);
    public void Reset() => _session.Write("*RST");
    public void Dispose() { } // Session ownership remains with the caller.
}

/// <summary>IVI-class-inspired adapter to the selected-subset DC-voltage DMM driver.</summary>
public sealed class ScpiDigitalMultimeterAdapter : IIviDigitalMultimeter, IInstrumentCapabilities
{
    private readonly IScpiSession _session;
    private readonly DigitalMultimeter _meter;
    private bool _configured;
    public ScpiDigitalMultimeterAdapter(IScpiSession session, bool simulationEnabled = false)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _meter = new DigitalMultimeter(session);
        SimulationEnabled = simulationEnabled;
    }
    public bool SimulationEnabled { get; }
    public string Identity => _meter.Identify();
    public InstrumentCapabilities Capabilities => new("IviDmm", 1);

    public void ConfigureMeasurement(IviMeasurementFunction function, double? range = null, double? resolution = null)
    {
        if (function != IviMeasurementFunction.DcVoltage)
            throw new NotSupportedException($"The current DMM SCPI profile does not implement {function}.");
        if (range is not null || resolution is not null)
            throw new NotSupportedException("Explicit range and resolution are not yet implemented by this SCPI profile.");
        _meter.ConfigureDcVoltage();
        _configured = true;
    }
    public double Measure()
    {
        if (!_configured) throw new InvalidOperationException("Configure the DMM before measuring.");
        return _meter.MeasureDcVoltage();
    }
    public void Reset()
    {
        _session.Write("*RST");
        _configured = false;
    }
    public void Dispose() { }
}
