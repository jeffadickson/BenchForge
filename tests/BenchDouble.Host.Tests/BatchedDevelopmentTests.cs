using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using BenchDouble.Simulation;
using BenchDouble.Generated;
using UnitsNet;
using BenchDouble.Host.Client;
using BenchDouble.Host.Contracts;
using BenchDouble.Host.Server.Setup;

namespace BenchDouble.Host.Tests;

/// <summary>Regression cases accumulated between ten-iteration test checkpoints.</summary>
public sealed class BatchedDevelopmentTests
{
    private const string Definition = """
    {"schemaVersion":2,"identity":{"driverName":"BatchMeter"},
     "modes":[{"id":"voltage","selectionCommand":"CONF:VOLT"}],
     "capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"MEAS:VOLT?","modes":["voltage"],"mockDefault":0.25}]}
    """;
    private static StatefulMockEngine Engine() => new(ImportedInstrumentDefinition.Parse(Definition));

    private sealed class ReplyHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Reply=(_,_)=>throw new InvalidOperationException();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Reply(request,ct);
    }
    private static OwnerHeartbeatSnapshot Snapshot()=>new(Guid.NewGuid(),1,Guid.Empty,0,"Healthy",0,10,30000,"FenceAndRetain");
    /// <summary>A short host renewal interval does not force a ten-millisecond HTTP budget.</summary>
    /// <returns>Completion after a delayed healthy acknowledgment.</returns>
    [Fact] public async Task OwnerRenewalHasAnIndependentTransportBudget()
    {
        var initial=Snapshot();var handler=new ReplyHandler();
        handler.Reply=async(request,ct)=>
        {
            var body=await request.Content!.ReadFromJsonAsync<OwnerHeartbeatRequest>(ct);
            await Task.Delay(30,ct);
            return new(HttpStatusCode.OK){Content=JsonContent.Create(initial with{PeerEpoch=body!.PeerEpoch,Sequence=body.Sequence})};
        };
        using var client=new HttpClient(handler){BaseAddress=new Uri("http://localhost/")};
        await using var heartbeat=new OwnerLeaseHeartbeat(client,"meter",Guid.NewGuid(),initial);
        Assert.Equal("Healthy",await heartbeat.RenewAsync());
    }

// CASES
}
