namespace BenchDouble.Ivi;

/// <summary>
/// A portable BenchDouble contract inspired by IVI instrument classes.
/// This is NOT the IVI Foundation's binary or source-level interface.
/// Official IVI interface adapters belong in optional integration packages.
/// </summary>
public interface IIviInstrument : IDisposable
{
    string Identity { get; }
    bool SimulationEnabled { get; }
    void Reset();
}

public enum IviWaveform { Sine, Square, Triangle, Ramp, Noise, Dc }
public enum IviMeasurementFunction { DcVoltage, AcVoltage, DcCurrent, AcCurrent, Resistance }

public interface IIviFunctionGenerator : IIviInstrument
{
    void ConfigureWaveform(int channel, IviWaveform shape, double frequencyHz, double amplitudeVpp);
    void SetOutputEnabled(int channel, bool enabled);
}

public interface IIviDigitalMultimeter : IIviInstrument
{
    void ConfigureMeasurement(IviMeasurementFunction function, double? range = null, double? resolution = null);
    double Measure();
}

public sealed record InstrumentCapabilities(
    string InstrumentClass,
    int Channels,
    double? MaximumFrequencyHz = null,
    double? MaximumAmplitudeVpp = null,
    bool SupportsSimulation = true);

public interface IInstrumentCapabilities
{
    InstrumentCapabilities Capabilities { get; }
}
