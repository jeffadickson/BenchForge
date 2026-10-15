using BenchDouble.Host.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using Xunit;

namespace BenchDouble.Host.Tests;

public sealed class InstrumentHostDirectoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "benchdouble-routing-" + Guid.NewGuid());
    private readonly CaptureLogger _logger = new();
    public InstrumentHostDirectoryTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);
    private string Save(string json)
    {
        var path = Path.Combine(_folder, "instruments.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void MissingDefaultUsesLocalAndLogsBothDecisionAndRoute()
    {
        var directory = InstrumentHostDirectory.Load(_logger, baseDirectory: _folder);
        var route = directory.Resolve("DMM1");
        Assert.True(directory.UsesLocalDefaults);
        Assert.Equal(InstrumentHostDirectory.LocalEndpoint, route.Endpoint);
        Assert.Equal("DMM1", route.ResourceId);
        Assert.Contains(_logger.Messages, x => x.Contains("defaults to local"));
        Assert.Contains(_logger.Messages, x => x.Contains("Instrument route selected"));
    }

    [Fact]
    public void ExplicitMissingFileFailsWithoutFallback()
    {
        Assert.Throws<InvalidDataException>(() => InstrumentHostDirectory.Load(_logger,
            Path.Combine(_folder, "missing.json")));
        Assert.Contains(_logger.Messages, x => x.Contains("no local fallback"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":2,\"hosts\":{},\"instruments\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"hosts\":{},\"instruments\":{},\"typo\":1}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"hosts\":{},\"instruments\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"hosts\":{},\"instruments\":{\"meter\":{\"host\":\"missing\",\"resourceId\":\"DMM1\"}}}")]
    public void InvalidConfigurationsAreRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => InstrumentHostDirectory.Load(_logger, Save(json)));
        Assert.Contains(_logger.Messages, x => x.Contains("no local fallback"));
    }

    [Theory]
    [InlineData("http://lab.example/")]
    [InlineData("https://user:secret@lab.example/")]
    [InlineData("https://lab.example/?token=secret")]
    [InlineData("https://lab.example/path")]
    public void UnsafeEndpointsAreRejectedWithoutLoggingCredentials(string endpoint)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        { schemaVersion = 1, hosts = new { remote = endpoint }, instruments = new { } });
        Assert.Throws<InvalidDataException>(() => InstrumentHostDirectory.Load(_logger, Save(json)));
        Assert.DoesNotContain(_logger.Messages, x => x.Contains("secret"));
    }

    [Fact]
    public async Task ConfiguredClientRoutesCanonicalResourceWithoutMutatingHttpClient()
    {
        var path = Save("""
            {"schemaVersion":1,"hosts":{"remote":"https://lab.example:5279"},
             "instruments":{"voltmeter":{"host":"remote","resourceId":"DMM1"}}}
            """);
        var directory = InstrumentHostDirectory.Load(_logger, path);
        Assert.Throws<KeyNotFoundException>(() => directory.Resolve("unknown"));
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9999/") };
        var client = directory.CreateClient("voltmeter", http);
        var grant = await client.ReserveAsync();
        Assert.Equal("https://lab.example:5279/api/v1/reservations", handler.Address);
        Assert.Contains("DMM1", handler.Body);
        Assert.Equal("DMM1", grant.ResourceId);
        Assert.Equal("http://localhost:9999/", http.BaseAddress!.AbsoluteUri);
    }

    [Fact]
    public void TwoExperimentsAndManyObserversDoNotRequireAServerWideLease()
    {
        var registry = new HostResourceRegistry();
        var first = registry.Reserve("DMM1");
        var second = registry.Reserve("FGEN1");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(registry.Reserve("DMM1"));
        for (var listener = 0; listener < 5; listener++)
            Assert.Equal(2, registry.Monitor().Instruments.Count(x => x.IsLocked));
        Assert.True(registry.Release(first!.LeaseId));
        Assert.True(registry.Release(second!.LeaseId));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Address { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Address = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(
                "{\"resourceId\":\"DMM1\",\"leaseId\":\"11111111-1111-1111-1111-111111111111\"}") };
        }
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, error));
    }
}
