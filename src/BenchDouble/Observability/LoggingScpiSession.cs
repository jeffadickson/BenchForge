using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BenchDouble.Observability;

/// <summary>
/// Controls what instrument data can be sent to diagnostic logging.
/// Commands and query responses are redacted by default; enabling payload
/// logging should be an explicit decision for a controlled test environment.
/// </summary>
/// <param name="InstrumentName">Nonblank display name, at most 128 characters without controls.</param>
/// <param name="LogCommandPayload">Whether command text may be logged after optional redaction.</param>
/// <param name="LogResponsePayload">Whether response text may be logged after optional redaction.</param>
/// <param name="Redact">Optional payload transform; failures substitute a redaction marker.</param>
/// <param name="MaxPayloadCharacters">Payload prefix limit in UTF-16 characters, from one to 65536; truncated text receives a marker.</param>
public sealed record ScpiLoggingOptions(
    string InstrumentName,
    bool LogCommandPayload = false,
    bool LogResponsePayload = false,
    Func<string, string>? Redact = null,
    int MaxPayloadCharacters = 4096);

/// <summary>
/// Decorates any SCPI session, including real VISA adapters, scripted mocks,
/// and stateful virtual instruments, without changing its behavior.
/// </summary>
public sealed class LoggingScpiSession : IScpiSession
{
    private readonly IScpiSession _inner;
    private readonly ILogger<LoggingScpiSession> _logger;
    private readonly ScpiLoggingOptions _options;
    private long _loggingFailures;
    private long _redactionFailures;
    /// <summary>Monotonic sink failure count; instrument I/O outcomes are preserved.</summary>
    public long LoggingFailureCount => Interlocked.Read(ref _loggingFailures);
    /// <summary>Monotonic redactor failure count; failed redactions never publish their payload.</summary>
    public long RedactionFailureCount => Interlocked.Read(ref _redactionFailures);

    public LoggingScpiSession(IScpiSession inner,
        ILogger<LoggingScpiSession> logger, ScpiLoggingOptions options)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.InstrumentName))
            throw new ArgumentException("Instrument name is required.", nameof(options));
        if (options.InstrumentName.Length > 128 || options.InstrumentName.Any(char.IsControl))
            throw new ArgumentException("Instrument name is too long or contains controls.", nameof(options));
        if (options.MaxPayloadCharacters is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(options));
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
        TryLog(() => _logger.LogDebug(new EventId(7600, "ScpiStarted"), "Instrument {Instrument} {Operation} started; Command={Command}",
            _options.InstrumentName, operation, Display(command, _options.LogCommandPayload)));
        try
        {
            var result = action();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (operation == "Query" && result is string response)
                TryLog(() => _logger.LogDebug(new EventId(7601, "ScpiCompleted"), "Instrument {Instrument} {Operation} completed; ElapsedMs={ElapsedMs}; Response={Response}",
                    _options.InstrumentName, operation, elapsed, Display(response, _options.LogResponsePayload)));
            else
                TryLog(() => _logger.LogDebug(new EventId(7601, "ScpiCompleted"), "Instrument {Instrument} {Operation} completed; ElapsedMs={ElapsedMs}",
                    _options.InstrumentName, operation, elapsed));
            return result;
        }
        catch (Exception exception)
        {
            TryLog(() => _logger.LogError(
                new EventId(7602, "ScpiFailed"),
                "Instrument {Instrument} {Operation} failed; ElapsedMs={ElapsedMs}; Command={Command}; ErrorType={ErrorType}",
                _options.InstrumentName, operation,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Display(command, _options.LogCommandPayload), exception.GetType().Name));
            throw;
        }
    }

    private void TryLog(Action emit)
    {
        try { emit(); } catch { Interlocked.Increment(ref _loggingFailures); }
    }

    private string Display(string content, bool permitted)
    {
        if (!permitted) return "[redacted]";
        try
        {
            var text = _options.Redact is null ? content : _options.Redact(content);
            if (text is null) { Interlocked.Increment(ref _redactionFailures); return "[redacted]"; }
            var limit = _options.MaxPayloadCharacters;
            if (text.Length <= limit) return text;
            if (char.IsHighSurrogate(text[limit - 1]) && char.IsLowSurrogate(text[limit])) --limit;
            return text[..limit] + "[truncated]";
        }
        catch { Interlocked.Increment(ref _redactionFailures); return "[redacted]"; }
    }
}
