using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

public sealed class VirtualBenchGraphTests
{
    [Fact]
    public void TypedConnectionsProduceCoherentTemperatureAndCurrent()
    {
        var builder = new VirtualBenchGraphBuilder();
        var chamber = builder.Port<Temperature>("chamber.setpoint");
        var sensor = builder.Port<Temperature>("dut.sensor");
        var supply = builder.Port<ElectricPotential>("supply.voltage");
        var current = builder.Port<ElectricCurrent>("dut.current");

        builder.Source(chamber, t => Temperature.FromDegreesCelsius(30 + t.TotalMinutes));
        builder.Connect(chamber, sensor);
        builder.Source(supply, _ => ElectricPotential.FromVolts(12));
        builder.Transform(sensor, supply, current, (temp, voltage, _) =>
            ElectricCurrent.FromAmperes(voltage.Volts / 24 *
                (1 + 0.10 * Math.Clamp((temp.DegreesCelsius - 30) / 40, 0, 1))));

        var graph = builder.Build();
        var atZero = graph.SampleAt(TimeSpan.Zero);
        Assert.Equal(30, atZero.Read(sensor).DegreesCelsius);
        Assert.Equal(0.5, atZero.Read(current).Amperes, 10);

        var at40 = graph.SampleAt(TimeSpan.FromMinutes(40));
        Assert.Equal(70, at40.Read(chamber).DegreesCelsius);
        Assert.Equal(0.55, at40.Read(current).Amperes, 10);
    }

    [Fact]
    public void OneSnapshotEvaluatesSharedSourceOnce()
    {
        var builder = new VirtualBenchGraphBuilder();
        var source = builder.Port<Temperature>("chamber");
        var sensorA = builder.Port<Temperature>("sensor.a");
        var sensorB = builder.Port<Temperature>("sensor.b");
        int evaluations = 0;
        builder.Source(source, _ =>
        {
            evaluations++;
            return Temperature.FromDegreesCelsius(70);
        });
        builder.Connect(source, sensorA);
        builder.Connect(source, sensorB);

        var snapshot = builder.Build().SampleAt(TimeSpan.FromHours(1));
        Assert.Equal(70, snapshot.Read(sensorA).DegreesCelsius);
        Assert.Equal(70, snapshot.Read(sensorB).DegreesCelsius);
        Assert.Equal(70, snapshot.Read(sensorA).DegreesCelsius);
        Assert.Equal(1, evaluations);
    }

    [Fact]
    public void CyclicConnectionsFailBeforeRun()
    {
        var builder = new VirtualBenchGraphBuilder();
        var a = builder.Port<Temperature>("a");
        var b = builder.Port<Temperature>("b");
        builder.Connect(a, b);
        builder.Connect(b, a);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void MissingSourceAndDuplicateDriverFail()
    {
        var builder = new VirtualBenchGraphBuilder();
        var input = builder.Port<Temperature>("input");
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        builder.Source(input, _ => Temperature.FromDegreesCelsius(20));
        Assert.Throws<InvalidOperationException>(() =>
            builder.Source(input, _ => Temperature.FromDegreesCelsius(30)));
        Assert.Throws<ArgumentException>(() => builder.Port<Temperature>("input"));
    }

    [Fact]
    public void CrossBenchPortCannotBeWiredOrRead()
    {
        var first = new VirtualBenchGraphBuilder();
        var second = new VirtualBenchGraphBuilder();
        var foreign = first.Port<Temperature>("foreign");
        var local = second.Port<Temperature>("local");
        first.Source(foreign, _ => Temperature.FromDegreesCelsius(20));
        second.Source(local, _ => Temperature.FromDegreesCelsius(30));
        Assert.Throws<InvalidOperationException>(() => second.Connect(foreign, local));
        var snapshot = second.Build().SampleAt(TimeSpan.Zero);
        Assert.Throws<InvalidOperationException>(() => snapshot.Read(foreign));
    }

    [Fact]
    public void StatefulThermalSensorCanBeWiredAsExplicitTransform()
    {
        var chamber = new ThermalChamberModel(ThermalCycleSettings.Example());
        var thermalSensor = new FirstOrderThermalSensor(
            chamber, TimeSpan.FromMinutes(20), Temperature.FromDegreesCelsius(30));
        var builder = new VirtualBenchGraphBuilder();
        var setpoint = builder.Port<Temperature>("chamber.setpoint");
        var sensed = builder.Port<Temperature>("dut.temperature");
        builder.Source(setpoint, t => chamber.ReadAt(t));
        builder.Transform(setpoint, sensed, (_, t) => thermalSensor.ReadAt(t));
        var graph = builder.Build();

        var atOneHour = graph.SampleAt(TimeSpan.FromHours(1));
        Assert.Equal(70, atOneHour.Read(setpoint).DegreesCelsius);
        Assert.InRange(atOneHour.Read(sensed).DegreesCelsius, 57.32, 57.34);
        Assert.Equal(atOneHour.Read(sensed), atOneHour.Read(sensed));
    }
}
