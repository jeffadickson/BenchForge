using System.Text.Json;
using System.Text;
using BenchDouble.Ivi.Official;

namespace BenchDouble.Host.Server.Setup;

/// <summary>Loopback-only setup endpoints; host-owned files and existing leases remain authoritative.</summary>
public static class SetupEndpoints
{
    /// <summary>Map discovery, import/validation, atomic save and role reservation endpoints.</summary>
    /// <param name="app">Existing local demonstration host.</param>
    public static void MapBenchSetup(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/setup");
        group.AddEndpointFilter(async (context, next) =>
        {
            var request = context.HttpContext.Request;
            if (request.Host.Host is not ("127.0.0.1" or "localhost" or "::1")) return Results.StatusCode(403);
            var origin = request.Headers.Origin.ToString();
            if (origin.Length > 0 && origin != $"{request.Scheme}://{request.Host}") return Results.StatusCode(403);
            try { return await next(context); }
            catch (PlatformNotSupportedException ex) { return Results.Json(new { error = ex.Message }, statusCode: 503); }
            catch (JsonException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        });
        group.MapPost("/discover", async (BenchSetupService setup, CancellationToken ct) =>
            Results.Ok(await setup.DiscoverAsync(ct)));
        group.MapPost("/discover-visa", async (string scope, string? host, BenchSetupService setup, CancellationToken ct) =>
            Results.Ok(await setup.DiscoverAsync(ct, new NativeVisaDiscoveryProvider(scope, host))));
        group.MapPost("/ivi-catalog", async (HttpRequest request, CancellationToken ct) =>
        {
            // Bounded upload only: the browser cannot request arbitrary host file paths.
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await request.Body.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + count > IviConfigurationCatalogReader.MaximumBytes)
                    return Results.BadRequest(new { error = "IVI configuration exceeds the 2 MiB limit." });
                buffer.Write(chunk, 0, count);
            }
            try
            {
                var xml = new UTF8Encoding(false, true).GetString(buffer.ToArray()).TrimStart('\uFEFF');
                return Results.Ok(IviConfigurationCatalogReader.ReadXml(xml, ct));
            }
            catch (DecoderFallbackException) { return Results.BadRequest(new { error = "Upload IVI XML as UTF-8 text." }); }
        });
        group.MapGet("/configuration", (BenchSetupService setup) => setup.Read() is { } saved
            ? Results.Ok(saved) : Results.Content("null", "application/json"));
        group.MapPost("/validate", (JsonElement document, BenchSetupService setup) =>
            Results.Ok(setup.Validate(BenchSetupService.Parse(document.GetRawText()))));
        group.MapPost("/configuration", (JsonElement document, BenchSetupService setup) =>
            Results.Ok(setup.Save(BenchSetupService.ParseSaveRequest(document.GetRawText()))));
        group.MapPost("/roles/{role}/reserve", (string role, BenchSetupService setup) =>
            setup.ReserveRole(role) is { } lease ? Results.Ok(lease) : Results.Conflict(new { error = "Instrument is busy." }));
    }
}
