using System.Net.Http.Json;
using BenchDouble.Host.Contracts;

namespace BenchDouble.Host.Client;

/// <summary>HTTP development client for a loopback-only simulated instrument host.</summary>
public sealed class InstrumentHostClient(HttpClient http, Uri? endpoint = null)
{
    // Absolute addresses allow several configured hosts to share a caller-owned
    // HttpClient without mutating its BaseAddress during concurrent operations.
    private string Address(string path) => endpoint is null ? path : new Uri(endpoint, path).AbsoluteUri;
    public async Task<IReadOnlyList<InstrumentDescriptor>> ListAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<InstrumentDescriptor[]>(Address("/api/v1/instruments"), ct) ?? [];

    public async Task<ReservationGrant> ReserveAsync(string resource, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(Address("/api/v1/reservations"), new ReservationRequest(resource), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReservationGrant>(cancellationToken: ct))!;
    }

    public async Task<string> QueryAsync(string resource, Guid lease, string command, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            Address($"/api/v1/instruments/{Uri.EscapeDataString(resource)}/query"), new ScpiRequest(lease, command), ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ScpiResponse>(cancellationToken: ct);
        return result?.Response ?? throw new InvalidOperationException("Missing SCPI response.");
    }

    public async Task ReleaseAsync(Guid lease, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync(Address($"/api/v1/reservations/{lease}"), ct);
        response.EnsureSuccessStatusCode();
    }
}

