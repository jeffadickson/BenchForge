using BenchDouble.Observability;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BenchDouble.Tests;

public sealed class InstrumentIdentificationTests
{
    [Fact]
    public void CapturesIdentityAndLogsWithoutEnablingCommandPayloadLogging()
    {
        var log = new CaptureLogger();
        var calls = 0;
        var result = InstrumentIdentificationReader.Capture("meter", "Mock", () =>
        { calls++; return "Vendor,Model,Serial,Firmware\r\n"; }, log);
        Assert.Equal(1, calls);
        Assert.Equal("Serial", result.Identity!.SerialNumber);
        Assert.Contains(log.Messages, x => x.Contains("Vendor,Model,Serial,Firmware"));
        Assert.DoesNotContain('\n', result.Response!);
    }

    [Fact]
    public void UnsupportedIdentificationIsExplicitAndLogged()
    {
        var log = new CaptureLogger();
        var result = InstrumentIdentificationReader.Capture("legacy", "Physical", null, log);
        Assert.Equal("Unsupported", result.Status);
        Assert.Null(result.Identity);
        Assert.Contains(log.Messages, x => x.Contains("Unsupported"));
    }

    [Fact]
    public void FailedIdentificationIsLoggedAndDoesNotPublishASuccessfulOpen()
    {
        var log = new CaptureLogger();
        Assert.Throws<TimeoutException>(() => InstrumentIdentificationReader.Capture(
            "meter", "Mock", () => throw new TimeoutException("private payload"), log));
        Assert.Contains(log.Messages, x => x.Contains("TimeoutException"));
        Assert.DoesNotContain(log.Messages, x => x.Contains("private payload"));
    }

    [Fact]
    public void LegacyResponseIsRetainedWithoutInventingParsedFields()
    {
        var result = InstrumentIdentificationReader.Capture("legacy", "Mock", () => "MODEL 236",
            new CaptureLogger());
        Assert.Equal("MODEL 236", result.Response);
        Assert.Null(result.Identity);
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
