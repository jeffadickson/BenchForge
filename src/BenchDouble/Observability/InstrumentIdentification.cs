using BenchDouble.Contracts;
using Microsoft.Extensions.Logging;

namespace BenchDouble.Observability;

/// <summary>Observed identity for one opened connection; never inferred from its address.</summary>
public sealed record InstrumentIdentification(string Status, string? Response,
    InstrumentIdentity? Identity, DateTimeOffset ObservedUtc);

/// <summary>
/// Captures and logs identity before a newly opened session is published to callers.
/// The driver supplies a supported query; null means identification is unsupported.
/// Transport timeout configuration remains the session owner's responsibility.
/// </summary>
public static class InstrumentIdentificationReader
{
    public static InstrumentIdentification Capture(string resourceId, string backend,
        Func<string>? identify, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (identify is null)
            return Record(resourceId, backend, "Unsupported", null, logger);
        try
        {
            var response = identify();
            if (string.IsNullOrWhiteSpace(response))
            {
                Record(resourceId, backend, "EmptyResponse", null, logger);
                throw new ScpiProtocolException("Instrument identification returned an empty response.");
            }
            return Record(resourceId, backend, "Identified", response, logger);
        }
        catch (Exception exception)
        {
            // Exception messages can contain transport payloads. Log the type only.
            logger.LogWarning("Instrument {ResourceId} identification failed; Backend={Backend}; ErrorType={ErrorType}",
                resourceId, backend, exception.GetType().Name);
            throw; // Failed I/O may leave an unread response; do not publish this session.
        }
    }

    private static InstrumentIdentification Record(string resourceId, string backend,
        string status, string? response, ILogger logger)
    {
        // Bound and flatten device-controlled text before logging/displaying it.
        var safe = response is null ? null : new string(response.Take(1024)
            .Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        var fields = safe?.Split(',', 4, StringSplitOptions.TrimEntries);
        var identity = fields is { Length: 4 }
            ? new InstrumentIdentity(fields[0], fields[1], fields[2], fields[3]) : null;
        var observed = new InstrumentIdentification(status, safe, identity, DateTimeOffset.UtcNow);
        logger.LogInformation("Instrument {ResourceId} opened; Backend={Backend}; IdentificationStatus={IdentificationStatus}; Identification={Identification}; ObservedUtc={ObservedUtc}",
            resourceId, backend, status, safe, observed.ObservedUtc);
        return observed;
    }
}
