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

// CASES
}
