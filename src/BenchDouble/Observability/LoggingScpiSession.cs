using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BenchDouble.Observability;

/// <summary>
/// Controls what instrument data can be sent to diagnostic logging.
/// Commands and query responses are redacted by default; enabling payload
/// logging should be an explicit decision for a controlled test environment.
/// </summary>
public sealed record ScpiLoggingOptions(
    string InstrumentName,
    bool LogCommandPayload = false,
    bool LogResponsePayload = false,
    Func<string, string>? Redact = null);

/// <summary>
/// Decorates any SCPI session, including real VISA adapters, scripted mocks,
/// and stateful virtual instruments, without changing its behavior.
/// </summary>
public sealed class LoggingScpiSession : IScpiSession
{
    private readonly IScpiSession _inner;
    private readonly ILogger<LoggingScpiSession> _logger;
    private readonly ScpiLoggingOptions _options;

    public LoggingScpiSession(IScpiSession inner,
        ILogger<LoggingScpiSession> logger, ScpiLoggingOptions options)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.InstrumentName))
            throw new ArgumentException("Instrument name is required.", nameof(options));
    }

    public void Write(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Execute("Write", command, () => { _inner.Write(command); return true; });
    }

    public string Query(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Execute("Query", command, () => _inner.Query(command));
    }

    private T Execute<T>(string operation, string command, Func<T> action)
    {
        var started = Stopwatch.GetTimestamp();
        _logger.LogDebug("Instrument {Instrument} {Operation} started; Command={Command}",
            _options.InstrumentName, operation, Display(command, _options.LogCommandPayload));
        try
        {
            var result = action();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (operation == "Query" && result is string response)
                _logger.LogDebug("Instrument {Instrument} {Operation} completed; ElapsedMs={ElapsedMs}; Response={Response}",
                    _options.InstrumentName, operation, elapsed, Display(response, _options.LogResponsePayload));
            else
                _logger.LogDebug("Instrument {Instrument} {Operation} completed; ElapsedMs={ElapsedMs}",
                    _options.InstrumentName, operation, elapsed);
            return result;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Instrument {Instrument} {Operation} failed; ElapsedMs={ElapsedMs}; Command={Command}",
                _options.InstrumentName, operation,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Display(command, _options.LogCommandPayload));
            throw;
        }
    }

    private string Display(string content, bool permitted) =>
        permitted ? (_options.Redact?.Invoke(content) ?? content) : "[redacted]";
}
