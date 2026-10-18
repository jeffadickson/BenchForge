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

    /// <summary>Malformed, expired or unsupported initial health snapshots cannot start renewal.</summary>
    [Fact] public void InitialOwnerBudgetsAreValidated()
    {
        using var client=new HttpClient();var initial=Snapshot();
        foreach(var invalid in new[]{initial with{AgeMilliseconds=-1},initial with{AgeMilliseconds=double.NaN},initial with{ExpireMilliseconds=double.PositiveInfinity},initial with{AgeMilliseconds=30000},initial with{ExpireMilliseconds=20},initial with{LossPolicy="Release"}})
            Assert.Throws<ArgumentException>(()=>new OwnerLeaseHeartbeat(client,"meter",Guid.NewGuid(),invalid));
    }

    /// <summary>Valid sequence identity cannot hide malformed ages or changed server timing budgets.</summary>
    /// <returns>Completion after each invalid acknowledgment becomes terminal Lost.</returns>
    [Fact] public async Task AcknowledgmentsCannotChangeOwnerBudgets()
    {
        foreach(var change in new Func<OwnerHeartbeatSnapshot,OwnerHeartbeatSnapshot>[] {s=>s with{AgeMilliseconds=-1},s=>s with{IntervalMilliseconds=20},s=>s with{ExpireMilliseconds=40000},s=>s with{LossPolicy="Release"}})
        {
            var initial=Snapshot();var handler=new ReplyHandler();
            handler.Reply=async(request,ct)=>
            {
                var body=await request.Content!.ReadFromJsonAsync<OwnerHeartbeatRequest>(ct);
                return new(HttpStatusCode.OK){Content=JsonContent.Create(change(initial with{PeerEpoch=body!.PeerEpoch,Sequence=body.Sequence}))};
            };
            using var client=new HttpClient(handler){BaseAddress=new Uri("http://localhost/")};
            await using var heartbeat=new OwnerLeaseHeartbeat(client,"meter",Guid.NewGuid(),initial);
            Assert.Equal("Lost",await heartbeat.RenewAsync());Assert.Null(heartbeat.LastAcknowledgment);
        }
    }

// CASES
}
