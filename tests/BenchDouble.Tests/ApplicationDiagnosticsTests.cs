using BenchDouble.Observability;
using BenchDouble.Generated;
using BenchDouble.Simulation;
using Microsoft.Extensions.Logging;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Failure recovery, logging privacy and cancellation contracts for application simulations.</summary>
public sealed class ApplicationDiagnosticsTests
{
    private sealed class Clock(TimeSpan elapsed) : ISimulationClock
    {
        internal TimeSpan Now = elapsed;
        /// <inheritdoc/>
        public TimeSpan Elapsed => Now;
    }
    private sealed class Load(Func<ElectricCurrent> action) : ILoadBehavior
    {
        /// <inheritdoc/>
        public ElectricCurrent CurrentAt(ElectricPotential voltage, TimeSpan elapsed) => action();
    }
    private sealed class Session : IScpiSession
    {
        internal int Calls;
        internal Exception? Failure;
        internal string Response = "response-secret";
        /// <inheritdoc/>
        public void Write(string command) { ++Calls; if (Failure is not null) throw Failure; }
        /// <inheritdoc/>
        public string Query(string command) { ++Calls; if (Failure is not null) throw Failure; return Response; }
    }
    private sealed class Sink : ILogger<LoggingScpiSession>
    {
        internal bool Throw;
        internal readonly List<(EventId Id,string Message,Exception? Error)> Events = [];
        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <inheritdoc/>
        public bool IsEnabled(LogLevel level) => true;
        /// <inheritdoc/>
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState,Exception?,string> formatter)
        { if (Throw) throw new InvalidOperationException("sink"); Events.Add((id,formatter(state,error),error)); }
    }

    /// <summary>Canonical overflow is rejected without losing valid typed evaluation.</summary>
    [Fact] public void TypedEvaluationRecoversFromOverflow()
    {
        var fail = true;
        var measurements = new TypedMockMeasurementsBuilder().Register(_ => fail ? ElectricPotential.FromKilovolts(double.MaxValue) : ElectricPotential.FromVolts(2)).Build();
        Assert.Throws<InvalidOperationException>(() => measurements.Evaluate<ElectricPotential>(TimeSpan.Zero));
        fail = false; Assert.Equal(2, measurements.Evaluate<ElectricPotential>(TimeSpan.Zero).Volts);
    }

    /// <summary>A cancelled typed callback releases its transaction gate for later measurements.</summary>
    /// <returns>Completion after cancellation and recovery.</returns>
    [Fact] public async Task TypedMeterCancellationReleasesGate()
    {
        using var cancel = new CancellationTokenSource(); var shouldCancel = true;
        var meter = new TypedMockMeter(new TypedMockMeasurementsBuilder().Register(_ => { if (shouldCancel) cancel.Cancel(); return ElectricPotential.FromVolts(2); }).Build(), new ManualSimulationClock());
        await meter.ConfigureAsync<ElectricPotential>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await meter.MeasureAsync<ElectricPotential>(cancel.Token));
        shouldCancel = false; Assert.Equal(2, (await meter.MeasureAsync<ElectricPotential>()).Volts);
    }

    /// <summary>Missing run dependencies are rejected at construction.</summary>
    [Fact] public void TypedMeterRejectsMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new TypedMockMeter(null!, new ManualSimulationClock()));
        Assert.Throws<ArgumentNullException>(() => new TypedMockMeter(new TypedMockMeasurementsBuilder().Build(), null!));
    }

    /// <summary>A power supply cannot defer missing behavior or clock failures to a run.</summary>
    [Fact] public void SupplyRejectsMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new MockPowerSupply(null!, new ManualSimulationClock()));
        Assert.Throws<ArgumentNullException>(() => new MockPowerSupply(new Load(() => ElectricCurrent.FromAmperes(1)), null!));
    }

    /// <summary>Supply callbacks cannot publish success after cancellation and leave the gate reusable.</summary>
    /// <returns>Completion after a cancelled sample followed by a valid sample.</returns>
    [Fact] public async Task SupplyCancellationRecovers()
    {
        using var cancel = new CancellationTokenSource(); var shouldCancel = true;
        var supply = new MockPowerSupply(new Load(() => { if (shouldCancel) cancel.Cancel(); return ElectricCurrent.FromAmperes(1); }), new ManualSimulationClock());
        await supply.ConfigureAsync(ElectricPotential.FromVolts(2), ElectricCurrent.FromAmperes(3)); await supply.SetOutputAsync(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await supply.MeasureAsync(cancel.Token));
        shouldCancel = false; Assert.Equal(1, (await supply.MeasureAsync()).Current.Amperes);
    }

    /// <summary>Disabled outputs do not authorize invalid sample timestamps.</summary>
    /// <returns>Completion after rejecting a negative-clock sample.</returns>
    [Fact] public async Task SupplyRejectsNegativeClockWhenDisabled()
    {
        var calls = 0;
        var supply = new MockPowerSupply(new Load(() => { ++calls; return ElectricCurrent.FromAmperes(1); }), new Clock(TimeSpan.FromTicks(-1)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await supply.MeasureAsync()); Assert.Equal(0, calls);
    }

    /// <summary>Mode-aware instruments reject invalid clock values before invoking user behavior.</summary>
    /// <returns>Completion after rejecting the timestamp.</returns>
    [Fact] public async Task ModeAwareRejectsNegativeClockBeforeCallback()
    {
        var calls = 0;
        var meter = new ModeAwareMockBuilder().Mode("voltage").Measurement("voltage", _ => { ++calls; return ElectricPotential.FromVolts(1); }).Build(new Clock(TimeSpan.FromTicks(-1)));
        await meter.ConfigureModeAsync("voltage");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await meter.MeasureAsync<ElectricPotential>()); Assert.Equal(0, calls);
    }

    /// <summary>Invalid stateful timestamps cannot consume a fault before a valid measurement.</summary>
    /// <returns>Completion after clock repair and exactly-once fault delivery.</returns>
    [Fact] public async Task StatefulClockFailurePreservesQueuedFault()
    {
        const string definition = """
            {"schemaVersion":2,"identity":{"driverName":"Meter"},"modes":[{"id":"v","selectionCommand":"CONF:V"}],"capabilities":[{"name":"v","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"V?","modes":["v"],"mockDefault":1}]}
            """;
        var clock = new Clock(TimeSpan.FromTicks(-1)); var engine = new StatefulMockEngine(ImportedInstrumentDefinition.Parse(definition), clock);
        await engine.ConfigureModeAsync("v"); var fault = new IOException("one-shot"); engine.QueueFault("v", fault);
        Assert.Throws<InvalidOperationException>(() => engine.Snapshot());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.MeasureAsync("v"));
        clock.Now = TimeSpan.Zero;
        Assert.Same(fault, await Assert.ThrowsAsync<IOException>(async () => await engine.MeasureAsync("v")));
        Assert.Equal(1, await engine.MeasureAsync("v"));
    }

    /// <summary>Sink errors do not change successful return values or duplicate I/O.</summary>
    [Fact] public void SinkFailuresAreCountedWithoutChangingIo()
    {
        var inner = new Session(); var session = new LoggingScpiSession(inner, new Sink { Throw = true }, new("meter"));
        Assert.Equal("response-secret", session.Query("command-secret"));
        Assert.Equal(1, inner.Calls); Assert.Equal(2, session.LoggingFailureCount);
    }

    /// <summary>Sink errors preserve the exact original provider exception.</summary>
    [Fact] public void FailedIoPreservesExceptionAndCountsSinkFailures()
    {
        var original = new IOException("private-provider-message"); var inner = new Session { Failure = original };
        var session = new LoggingScpiSession(inner, new Sink { Throw = true }, new("meter"));
        Assert.Same(original, Assert.Throws<IOException>(() => session.Query("secret")));
        Assert.Equal(1, inner.Calls); Assert.Equal(2, session.LoggingFailureCount);
    }

    /// <summary>Both throwing and null-returning redactors fail closed and preserve I/O.</summary>
    /// <param name="throws">Whether the configured redactor throws instead of returning null.</param>
    [Theory] [InlineData(true)] [InlineData(false)]
    public void RedactorFailuresAreCounted(bool throws)
    {
        var sink = new Sink(); var inner = new Session();
        var session = new LoggingScpiSession(inner, sink, new("meter", true, true, _ => throws ? throw new IOException("redactor-secret") : null!));
        Assert.Equal(inner.Response, session.Query("command-secret"));
        Assert.Equal(2, session.RedactionFailureCount); Assert.Equal(0, session.LoggingFailureCount);
        Assert.All(sink.Events, item => { Assert.Contains("[redacted]", item.Message); Assert.DoesNotContain("secret", item.Message); Assert.Null(item.Error); });
    }

    /// <summary>Opted-in payload limits affect logs, preserving the original response and command.</summary>
    [Fact] public void PayloadTruncationPreservesIo()
    {
        var sink = new Sink(); var inner = new Session { Response = "abcdefgh" };
        var session = new LoggingScpiSession(inner, sink, new("meter", true, true, MaxPayloadCharacters: 3));
        Assert.Equal("abcdefgh", session.Query("123456"));
        Assert.Contains("123[truncated]", sink.Events[0].Message); Assert.Contains("abc[truncated]", sink.Events[1].Message); Assert.Equal(1, inner.Calls);
    }

    /// <summary>UTF-16 truncation never publishes one half of a surrogate pair.</summary>
    [Fact] public void TruncationPreservesUnicodeBoundaries()
    {
        var sink = new Sink(); var session = new LoggingScpiSession(new Session(), sink, new("meter", true, MaxPayloadCharacters: 2));
        session.Write("a\U0001F600b"); Assert.Contains("a[truncated]", sink.Events[0].Message); Assert.DoesNotContain('\uD83D', sink.Events[0].Message);
    }

    /// <summary>Control characters cannot be injected through diagnostic instrument names.</summary>
    /// <param name="name">Invalid display name.</param>
    [Theory] [InlineData("meter\n")] [InlineData("meter\r")] [InlineData("meter\0")] [InlineData("\t")]
    public void LoggingNamesRejectControls(string name) => Assert.Throws<ArgumentException>(() => new LoggingScpiSession(new Session(), new Sink(), new(name)));

    /// <summary>Logging payload and name bounds are explicit.</summary>
    [Fact] public void LoggingOptionBoundsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new LoggingScpiSession(new Session(), new Sink(), new(new string('x', 129))));
        foreach (var limit in new[] { 0, -1, 65537 }) Assert.Throws<ArgumentOutOfRangeException>(() => new LoggingScpiSession(new Session(), new Sink(), new("meter", MaxPayloadCharacters: limit)));
    }

    /// <summary>Stable event IDs distinguish start, success and failure without attaching provider exceptions.</summary>
    [Fact] public void ScpiEventsHaveStableIds()
    {
        var sink = new Sink(); var inner = new Session(); var session = new LoggingScpiSession(inner, sink, new("meter"));
        session.Query("secret"); inner.Failure = new IOException("provider-secret"); Assert.Throws<IOException>(() => session.Write("secret"));
        Assert.Equal(new[] { 7600,7601,7600,7602 }, sink.Events.Select(item => item.Id.Id));
        Assert.All(sink.Events, item => { Assert.DoesNotContain("secret", item.Message); Assert.Null(item.Error); });
    }
}
