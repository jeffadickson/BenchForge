using System.Net;
using BenchForge.Host.Client;
using BenchForge.Host.Contracts;
using BenchForge.Host.Server.Security;
using BenchForge.Outcomes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BenchForge.Host.Tests;

/// <summary>
/// End-to-end integration tests for the railroad <c>/api/v2/*</c> host API and <see cref="RailroadHostClient"/>:
/// authentication, RBAC, raw-SCPI lockdown, principal-bound leases, durable command receipts, and hash-chained audit verification.
/// </summary>
public sealed class HostV2SecurityAndRailroadTests
{
    /// <summary>Verify live HTTP /api/v2/* endpoints enforce RBAC, lease binding, raw-SCPI lockdown, and verifiable audit chains.</summary>
    /// <returns>Completion of the test assertion.</returns>
    [Fact]
    public async Task V2RailroadApiEnforcesPrivateLabSecurityFencedCommandsAndAuditChainOverHttp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "benchforge-v2-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        const string observerToken = "obs-token-flight-01";
        const string operatorToken = "op-token-flight-02";
        const string otherOpToken = "op-token-flight-03";
        const string recoveryToken = "rec-token-flight-04";

        var policy = new PrivateLabSecurityPolicy(
            [
                new LabPrincipal("observer-1", LabRole.Observer, LabPrincipal.HashToken(observerToken), ["*"]),
                new LabPrincipal("operator-1", LabRole.Operator, LabPrincipal.HashToken(operatorToken), ["DMM1"]),
                new LabPrincipal("operator-2", LabRole.Operator, LabPrincipal.HashToken(otherOpToken), ["DMM1"]),
                new LabPrincipal("recovery-1", LabRole.RecoveryAdmin, LabPrincipal.HashToken(recoveryToken), ["*"])
            ],
            allowPrivateLanBinding: true,
            allowPlaintextOnIsolatedVlan: true,
            allowRawScpiGlobal: false);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(policy);
        builder.Services.AddSingleton<HostResourceRegistry>(_ => new HostResourceRegistry());
        builder.Services.AddSingleton(sp => new RailroadHostService(
            sp.GetRequiredService<HostResourceRegistry>(),
            sp.GetRequiredService<PrivateLabSecurityPolicy>(),
            Path.Combine(dir, "audit.jsonl"),
            Path.Combine(dir, "fencing.json"),
            Path.Combine(dir, "commands.json")));

        var app = builder.Build();
        app.MapRailroadV2Api();
        await app.StartAsync();

        try
        {
            var baseUri = new Uri(app.Urls.First());
            using var http = new HttpClient();

            var anonClient = new RailroadHostClient(http, baseUri);
            var obsClient = new RailroadHostClient(http, baseUri, observerToken);
            var op1Client = new RailroadHostClient(http, baseUri, operatorToken);
            var op2Client = new RailroadHostClient(http, baseUri, otherOpToken);
            var recClient = new RailroadHostClient(http, baseUri, recoveryToken);

            // 1. Unauthenticated caller rejected with Unauthorized.
            var unauth = await anonClient.DescribeAsync();
            Assert.True(unauth.IsFailure);
            Assert.Equal(BenchErrorKind.Unauthorized, unauth.Error.Kind);

            // 2. Observer can describe bench but cannot reserve.
            var manifest = await obsClient.DescribeAsync();
            Assert.True(manifest.IsSuccess);
            Assert.Equal("2.0", manifest.Value.ProtocolVersion);
            Assert.False(manifest.Value.AllowRawScpi);
            Assert.Contains(manifest.Value.Instruments, i => i.ResourceId == "DMM1");

            var obsReserve = await obsClient.ReserveAsync("DMM1");
            Assert.True(obsReserve.IsFailure);
            Assert.Equal(BenchErrorKind.Forbidden, obsReserve.Error.Kind);

            // 3. Operator-1 reserves DMM1 and dispatches a canonical fenced query command.
            var grantRes = await op1Client.ReserveAsync("DMM1");
            Assert.True(grantRes.IsSuccess);
            var grant = grantRes.Value;
            Assert.Equal("operator-1", grant.PrincipalId);

            var queryCmd = new RemoteCommandRequest(
                Guid.NewGuid(),
                "corr-v2-001",
                "DMM1",
                grant.LeaseId,
                grant.HostEpoch,
                grant.Generation,
                "Query",
                "ReadOnlyIdempotent",
                "MEAS:VOLT:DC?",
                ExpectsResponse: true,
                MaxSafeRetries: 1,
                IsRawScpi: false);

            var cmdRes = await op1Client.SubmitCommandAsync(queryCmd);
            Assert.True(cmdRes.IsSuccess);
            Assert.Equal("5", cmdRes.Value.Response?.Trim());

            // 4. Raw SCPI is locked down even for owning operator.
            var rawRes = await op1Client.SubmitCommandAsync(queryCmd with { CommandId = Guid.NewGuid(), IsRawScpi = true });
            Assert.True(rawRes.IsFailure);
            Assert.Equal("BF-SEC-RAW-SCPI-LOCKED", rawRes.Error.Code);

            // 5. Operator-2 cannot hijack Operator-1's lease GUID.
            var hijackRes = await op2Client.SubmitCommandAsync(queryCmd with { CommandId = Guid.NewGuid() });
            Assert.True(hijackRes.IsFailure);
            Assert.Equal(BenchErrorKind.LeaseFenced, hijackRes.Error.Kind);

            // 6. Operator-1 cannot reconcile; RecoveryAdmin can; Release succeeds; Audit chain verifies cleanly.
            var opReconcile = await op1Client.ReconcileAsync("DMM1", "Attempted without privilege");
            Assert.Equal(BenchErrorKind.Forbidden, opReconcile.Error.Kind);

            var recReconcile = await recClient.ReconcileAsync("DMM1", "Verified DMM1 safe.");
            Assert.True(recReconcile.IsSuccess);

            var released = await op1Client.ReleaseAsync(grant.LeaseId);
            Assert.True(released.IsSuccess);
            Assert.True(released.Value);

            var audit = await obsClient.VerifyAuditAsync();
            Assert.True(audit.IsSuccess);
            Assert.True(audit.Value.EntryCount >= 6);
            Assert.Equal(64, audit.Value.HeadSha256.Length);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
