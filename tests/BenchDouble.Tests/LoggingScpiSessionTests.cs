using BenchDouble;
using BenchDouble.Observability;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace BenchDouble.Tests;

public sealed class LoggingScpiSessionTests
{
    [Fact]
    public void DecoratorPreservesCommandAndQueryBehavior()
    {
        var underlying = Substitute.For<IScpiSession>();
        underlying.Query("*IDN?").Returns("MODEL");
        var logger = new CollectingLogger();
        var session = new LoggingScpiSession(underlying, logger,
            new ScpiLoggingOptions("DMM1"));
        session.Write("CONF:VOLT:DC");
        Assert.Equal("MODEL", session.Query("*IDN?"));
        underlying.Received(1).Write("CONF:VOLT:DC");
        Assert.DoesNotContain(logger.Messages, m => m.Contains("MODEL", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("CONF:VOLT:DC", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("[redacted]", StringComparison.Ordinal));
    }

    [Fact]
    public void OptInResponseCaptureAndRedactorAreApplied()
    {
        var underlying = Substitute.For<IScpiSession>();
        underlying.Query("READ?").Returns("SECRET=123");
        var logger = new CollectingLogger();
        var session = new LoggingScpiSession(underlying, logger,
            new ScpiLoggingOptions("DMM1", LogResponsePayload: true,
                Redact: text => text.Replace("123", "***", StringComparison.Ordinal)));
        session.Query("READ?");
        Assert.DoesNotContain(logger.Messages, m => m.Contains("123", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("SECRET=***", StringComparison.Ordinal));
    }

    [Fact]
    public void ExceptionsArePropagatedWithoutRetry()
    {
        var underlying = Substitute.For<IScpiSession>();
        underlying.When(x => x.Write("OUTP ON")).Do(_ => throw new TimeoutException("Timeout"));
        var logger = new CollectingLogger();
        var session = new LoggingScpiSession(underlying, logger,
            new ScpiLoggingOptions("FGEN1"));
        Assert.Throws<TimeoutException>(() => session.Write("OUTP ON"));
        underlying.Received(1).Write("OUTP ON");
        Assert.Contains(logger.Messages, m => m.Contains("failed", StringComparison.Ordinal));
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();
        public void Dispose() { }
    }

    private sealed class CollectingLogger : ILogger<LoggingScpiSession>
    {
        public List<string> Messages { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NoopScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
