using BenchDouble;
using BenchDouble.Observability;
using Microsoft.Extensions.Logging;
using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Local behavior and composition regressions.</summary>
public sealed class LocalHardeningTests
{
    private sealed class Sink : ILogger<LoggingScpiSession>
    {
        public readonly List<string> Messages = [];
        public readonly List<Exception?> Exceptions = [];
        public bool Throw;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (Throw) throw new InvalidOperationException("sink");
            Messages.Add(formatter(state, exception)); Exceptions.Add(exception);
        }
    }
    private sealed class Session : IScpiSession
    {
        public int Calls;
        public Exception? Failure;
        public void Write(string command) { Calls++; if (Failure is not null) throw Failure; }
        public string Query(string command) { Write(command); return "secret"; }
    }

    /// <summary>Diagnostics never suppress, replay or replace an instrument operation.</summary>
    [Fact]
    public void LoggingSinkCannotAlterIo()
    {
        var inner = new Session();
        var session = new LoggingScpiSession(inner, new Sink { Throw = true }, new("meter"));
        Assert.Equal("secret", session.Query("READ?"));
        var failure = new TimeoutException("device"); inner.Failure = failure;
        Assert.Same(failure, Assert.Throws<TimeoutException>(() => session.Write("OUTP ON")));
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Broken or null-returning redactors cannot expose payloads or change I/O.</summary>
    [Fact]
    public void RedactorFailureIsClosed()
    {
        foreach (Func<string, string> redact in new Func<string, string>[] { _ => throw new Exception("secret"), _ => null! })
        {
            var sink = new Sink(); var inner = new Session();
            var session = new LoggingScpiSession(inner, sink, new("meter", true, true, redact));
            Assert.Equal("secret", session.Query("secret"));
            Assert.Equal(1, inner.Calls);
            Assert.All(sink.Messages, message => Assert.DoesNotContain("secret", message));
            Assert.Contains(sink.Messages, message => message.Contains("[redacted]"));
        }
    }

    /// <summary>Provider exception messages remain with the caller and are excluded from diagnostic sinks.</summary>
    [Fact]
    public void InstrumentErrorsDoNotLeakPayloads()
    {
        var inner = new Session { Failure = new InvalidOperationException("secret") }; var sink = new Sink();
        var session = new LoggingScpiSession(inner, sink, new("meter"));
        Assert.Same(inner.Failure, Assert.Throws<InvalidOperationException>(() => session.Query("secret")));
        Assert.All(sink.Messages, message => Assert.DoesNotContain("secret", message));
        Assert.All(sink.Exceptions, Assert.Null);
        Assert.Contains(sink.Messages, message => message.Contains("InvalidOperationException"));
    }

    private sealed class InvalidLoad(double amps) : ILoadBehavior
    {
        public ElectricCurrent CurrentAt(ElectricPotential voltage, TimeSpan elapsed) => ElectricCurrent.FromAmperes(amps);
    }

    /// <summary>Unmatched baselines obey the same finite nonnegative current contract as rules.</summary>
    [Fact]
    public void AutoMockRejectsInvalidBaselines()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoMockLoadCondition(MaximumVoltage: ElectricPotential.FromVolts(-1)).Validate());
        var negative = new RuleBasedLoadBehavior(new InvalidLoad(-1), []);
        Assert.Throws<InvalidOperationException>(() => negative.EvaluateAt(ElectricPotential.FromVolts(1), TimeSpan.Zero));
        // UnitsNet rejects nonfinite values while the baseline constructs its
        // quantity; the decorator must preserve that original validation error.
        foreach (var amps in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var behavior = new RuleBasedLoadBehavior(new InvalidLoad(amps), []);
            Assert.Throws<ArgumentException>(() => behavior.EvaluateAt(ElectricPotential.FromVolts(1), TimeSpan.Zero));
        }
    }

// CASES
}
