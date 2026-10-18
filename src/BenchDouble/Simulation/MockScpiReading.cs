namespace BenchDouble.Simulation;
/// <summary>One immutable simulated scalar captured without physical I/O.</summary>
/// <param name="Parameter">Definition parameter identity.</param>
/// <param name="Channel">One-based instrument channel.</param>
/// <param name="Quantity">Declared physical quantity identity.</param>
/// <param name="Unit">Declared canonical unit identity.</param>
/// <param name="Value">Finite value in the declared canonical unit.</param>
public sealed record MockScpiReading(string Parameter, int Channel, string Quantity, string Unit, double Value);
