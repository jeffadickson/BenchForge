namespace BenchDouble.Contracts;

/// <summary>Identifies an instrument independently of its communication backend.</summary>
public sealed record InstrumentIdentity(string Manufacturer, string Model, string SerialNumber, string FirmwareVersion);

/// <summary>Provenance is explicit so a simulated instrument cannot masquerade as physical hardware.</summary>
public enum InstrumentBackend { Physical, Simulated, Mock }

/// <summary>Describes the standard and vendor-specific capabilities available to a driver.</summary>
public sealed record InstrumentFeature(string Key, string Description, string? Unit = null,
    double? Minimum = null, double? Maximum = null, bool Writable = true);

/// <summary>Common device functionality; no VISA or IVI vendor SDK dependency.</summary>
public interface IInstrument : IDisposable
{
    InstrumentIdentity Identity { get; }
    InstrumentBackend Backend { get; }
    IReadOnlyDictionary<string, InstrumentFeature> Features { get; }
    void Reset();
}

/// <summary>Optional escape hatch for non-standard SCPI. Implementations must validate support.</summary>
public interface IRawScpiInstrument
{
    void WriteScpi(string command);
    string QueryScpi(string command);
}

/// <summary>Function generator channel exposes basic standardized waveform controls.</summary>
public interface IFunctionGeneratorChannel
{
    int Number { get; }
    void ConfigureSine(double frequencyHz, double amplitudeVpp, double offsetVolts = 0);
    void SetEnabled(bool enabled);
}

/// <summary>Generic waveform generator. Advanced burst/sweep functions are optional extensions.</summary>
public interface IFunctionGenerator : IInstrument
{
    IReadOnlyList<IFunctionGeneratorChannel> Channels { get; }
}

/// <summary>DMM common modes; advanced integration/aperture functions live in extensions.</summary>
public enum MeasurementFunction { DcVoltage, AcVoltage, DcCurrent, AcCurrent, Resistance, FourWireResistance }

public interface IDigitalMultimeter : IInstrument
{
    void Configure(MeasurementFunction function, double? range = null, double? resolution = null);
    double Measure();
}

/// <summary>Optional capability: measurement integration using NPLC.</summary>
public interface INplcIntegration
{
    double IntegrationNplc { get; set; }
}

/// <summary>Optional capability: integration aperture in seconds.</summary>
public interface IApertureIntegration
{
    double ApertureSeconds { get; set; }
}

/// <summary>Optional capability: instruments with waveform acquisition.</summary>
public interface IOscilloscope : IInstrument
{
    IReadOnlyList<IOscilloscopeChannel> Channels { get; }
    void SingleAcquisition();
}

public interface IOscilloscopeChannel
{
    int Number { get; }
    void SetVerticalScale(double voltsPerDivision);
    double[] ReadWaveformVolts();
}

/// <summary>A manufacturer-specific feature extension; never required by generic procedures.</summary>
public interface IInstrumentExtension<out TCapabilities>
{
    TCapabilities VendorCapabilities { get; }
}
