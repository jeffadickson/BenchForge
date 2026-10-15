namespace BenchDouble.Contracts;

/// <summary>Identifies an instrument independently of its communication backend.</summary>
/// <param name="Manufacturer">Reported manufacturer; may be empty when the instrument does not supply it.</param>
/// <param name="Model">Reported instrument model.</param>
/// <param name="SerialNumber">Reported serial identifier; not a reservation token.</param>
/// <param name="FirmwareVersion">Reported firmware revision.</param>
public sealed record InstrumentIdentity(string Manufacturer, string Model, string SerialNumber, string FirmwareVersion);

/// <summary>Provenance is explicit so a simulated instrument cannot masquerade as physical hardware.</summary>
public enum InstrumentBackend
{
    /// <summary>A connection intended to communicate with physical hardware.</summary>
    Physical,
    /// <summary>A behavioral simulation of an instrument or experiment.</summary>
    Simulated,
    /// <summary>A test double with configured responses.</summary>
    Mock
}

/// <summary>Describes the standard and vendor-specific capabilities available to a driver.</summary>
/// <param name="Key">Stable driver feature identifier.</param>
/// <param name="Description">Human-readable feature meaning.</param>
/// <param name="Unit">Unit code, or null when not declared.</param>
/// <param name="Minimum">Declared lower bound, or null when unspecified.</param>
/// <param name="Maximum">Declared upper bound, or null when unspecified.</param>
/// <param name="Writable">Whether the driver advertises writes for this feature.</param>
public sealed record InstrumentFeature(string Key, string Description, string? Unit = null,
    double? Minimum = null, double? Maximum = null, bool Writable = true);

/// <summary>Common device functionality; no VISA or IVI vendor SDK dependency.</summary>
public interface IInstrument : IDisposable
{
    /// <summary>Instrument identity supplied by the implementation; not an authorization credential.</summary>
    InstrumentIdentity Identity { get; }
    /// <summary>Whether this instance represents hardware, simulation or a mock.</summary>
    InstrumentBackend Backend { get; }
    /// <summary>Advertised features, indexed by driver-defined feature keys.</summary>
    IReadOnlyDictionary<string, InstrumentFeature> Features { get; }
    /// <summary>Request the driver-defined instrument reset; this is not a universal safe-state operation.</summary>
    /// <remarks>Reserve exclusive access before changing state. Transport and device errors propagate according to the implementation.</remarks>
    void Reset();
}

/// <summary>Optional escape hatch for non-standard SCPI. Implementations must validate support.</summary>
public interface IRawScpiInstrument
{
    /// <summary>Send a driver-supported raw SCPI command.</summary>
    /// <param name="command">Complete command in the instrument's supported dialect.</param>
    /// <remarks>Write completion alone does not confirm execution or settling. Ownership remains the caller's responsibility.</remarks>
    void WriteScpi(string command);
    /// <summary>Send a raw query and read its response under the driver's transport policy.</summary>
    /// <param name="command">Complete query supported by the instrument.</param>
    /// <returns>The unparsed response; interpretation belongs to the caller.</returns>
    string QueryScpi(string command);
}

/// <summary>Function generator channel exposes basic standardized waveform controls.</summary>
public interface IFunctionGeneratorChannel
{
    /// <summary>Driver-assigned channel number; consult the concrete instrument for valid values.</summary>
    int Number { get; }
    /// <summary>Configure a sine waveform; supported limits depend on the driver.</summary>
    /// <param name="frequencyHz">Frequency in hertz.</param>
    /// <param name="amplitudeVpp">Peak-to-peak amplitude in volts, not RMS.</param>
    /// <param name="offsetVolts">DC offset in volts.</param>
    void ConfigureSine(double frequencyHz, double amplitudeVpp, double offsetVolts = 0);
    /// <summary>Request output enable or disable for this channel.</summary>
    /// <param name="enabled">True to enable output; false to disable.</param>
    void SetEnabled(bool enabled);
}

/// <summary>Generic waveform generator. Advanced burst/sweep functions are optional extensions.</summary>
public interface IFunctionGenerator : IInstrument
{
    /// <summary>Channels exposed by this driver; channel limits are instrument-specific.</summary>
    IReadOnlyList<IFunctionGeneratorChannel> Channels { get; }
}

/// <summary>DMM common modes; advanced integration/aperture functions live in extensions.</summary>
public enum MeasurementFunction
{
    /// <summary>DC voltage in volts.</summary>
    DcVoltage,
    /// <summary>AC voltage in volts; consult the driver for RMS/peak measurement basis.</summary>
    AcVoltage,
    /// <summary>DC current in amperes.</summary>
    DcCurrent,
    /// <summary>AC current in amperes; consult the driver for measurement basis.</summary>
    AcCurrent,
    /// <summary>Resistance in ohms.</summary>
    Resistance,
    /// <summary>Four-wire resistance in ohms.</summary>
    FourWireResistance
}

/// <summary>Configure a supported DMM function and acquire scalar readings.</summary>
public interface IDigitalMultimeter : IInstrument
{
    /// <summary>Select a measurement function with optional range and resolution.</summary>
    /// <param name="function">Requested function; unsupported functions are driver-specific errors.</param>
    /// <param name="range">Range in the function's base unit, or null for driver-selected behavior.</param>
    /// <param name="resolution">Resolution in the function's base unit, or null for driver-selected behavior.</param>
    void Configure(MeasurementFunction function, double? range = null, double? resolution = null);
    /// <summary>Acquire a reading using the configured measurement function.</summary>
    /// <returns>A value in volts, amperes or ohms according to the selected function.</returns>
    double Measure();
}

/// <summary>Optional capability: measurement integration using NPLC.</summary>
public interface INplcIntegration
{
    /// <summary>Integration duration expressed as a number of power-line cycles; limits depend on the instrument.</summary>
    double IntegrationNplc { get; set; }
}

/// <summary>Optional capability: integration aperture in seconds.</summary>
public interface IApertureIntegration
{
    /// <summary>Integration aperture in seconds; limits depend on the instrument.</summary>
    double ApertureSeconds { get; set; }
}

/// <summary>Optional capability: instruments with waveform acquisition.</summary>
public interface IOscilloscope : IInstrument
{
    /// <summary>Acquisition channels exposed by the driver.</summary>
    IReadOnlyList<IOscilloscopeChannel> Channels { get; }
    /// <summary>Request one acquisition; the implementation defines trigger waiting and completion behavior.</summary>
    void SingleAcquisition();
}

/// <summary>Basic scope channel controls and decoded voltage samples.</summary>
public interface IOscilloscopeChannel
{
    /// <summary>Driver-assigned channel number; consult the concrete instrument for valid values.</summary>
    int Number { get; }
    /// <summary>Set the channel vertical scale within the instrument-supported range.</summary>
    /// <param name="voltsPerDivision">Vertical scale in volts per display division.</param>
    void SetVerticalScale(double voltsPerDivision);
    /// <summary>Read decoded voltage samples from the channel.</summary>
    /// <returns>Samples in volts in acquisition order; this legacy API does not carry timebase, trigger or calibration metadata.</returns>
    /// <remarks>Do not persist this array as a self-describing trace. Capture the acquisition metadata separately; the managed artifact service is still planned.</remarks>
    double[] ReadWaveformVolts();
}

/// <summary>A manufacturer-specific feature extension; never required by generic procedures.</summary>
/// <typeparam name="TCapabilities">Vendor capability contract exposed by the extension.</typeparam>
public interface IInstrumentExtension<out TCapabilities>
{
    /// <summary>The vendor-specific capability object; ownership remains defined by the concrete driver.</summary>
    TCapabilities VendorCapabilities { get; }
}

