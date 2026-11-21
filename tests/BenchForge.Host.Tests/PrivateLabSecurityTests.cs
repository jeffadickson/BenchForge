using BenchForge.Host.Server.Security;
using BenchForge.Outcomes;
using Xunit;

namespace BenchForge.Host.Tests;

/// <summary>Regression tests for pragmatic private-lab security policy, RBAC, lease binding, and hash-chained audit log.</summary>
public sealed class PrivateLabSecurityTests
{
    /// <summary>Verify listener policy allows loopback, blocks unintended LAN exposure, and permits explicit isolated VLAN with auth.</summary>
    [Fact]
    public void ListenerPolicyFailsClosedForNonLoopbackWithoutExplicitPrivateLanOptIn()
    {
        var op = new LabPrincipal("op1", LabRole.Operator, LabPrincipal.HashToken("secret-op-token"), ["*"]);

        var loopbackOnly = new PrivateLabSecurityPolicy([]);
        Assert.True(loopbackOnly.ValidateListenerUri(new Uri("http://127.0.0.1:5278")).IsSuccess);
        Assert.Equal(BenchErrorKind.Forbidden, loopbackOnly.ValidateListenerUri(new Uri("http://10.42.0.5:5278")).Error.Kind);

        var lanNoPrincipals = new PrivateLabSecurityPolicy([], allowPrivateLanBinding: true);
        Assert.Equal(BenchErrorKind.Unauthorized, lanNoPrincipals.ValidateListenerUri(new Uri("https://10.42.0.5:5279")).Error.Kind);

        var lanTlsRequired = new PrivateLabSecurityPolicy([op], allowPrivateLanBinding: true, requireTlsOnLan: true);
        Assert.Equal(BenchErrorKind.Forbidden, lanTlsRequired.ValidateListenerUri(new Uri("http://10.42.0.5:5278")).Error.Kind);
        Assert.True(lanTlsRequired.ValidateListenerUri(new Uri("https://10.42.0.5:5279")).IsSuccess);

        var isolatedVlan = new PrivateLabSecurityPolicy([op], allowPrivateLanBinding: true, requireTlsOnLan: true, allowPlaintextOnIsolatedVlan: true);
        Assert.True(isolatedVlan.ValidateListenerUri(new Uri("http://10.42.0.5:5278")).IsSuccess);
    }

    /// <summary>Verify constant-time token auth, RBAC hierarchy, raw-SCPI lockdown, and cross-principal lease hijack rejection.</summary>
    [Fact]
    public void TokenAuthRbacRawScpiLockdownAndLeaseOwnerBindingEnforceLeastPrivilege()
    {
        var observer = new LabPrincipal("obs1", LabRole.Observer, LabPrincipal.HashToken("tok-obs"), ["*"]);
        var op = new LabPrincipal("op1", LabRole.Operator, LabPrincipal.HashToken("tok-op"), ["SMU1", "DMM1"]);
        var recovery = new LabPrincipal("rec1", LabRole.RecoveryAdmin, LabPrincipal.HashToken("tok-rec"), ["*"]);
        var policy = new PrivateLabSecurityPolicy([observer, op, recovery], allowPrivateLanBinding: true, allowRawScpiGlobal: false);

        Assert.Equal(BenchErrorKind.Unauthorized, policy.AuthenticateToken("wrong-token").Error.Kind);
        Assert.Equal("op1", policy.AuthenticateToken("tok-op").Value.PrincipalId);

        // Observer can observe, cannot reserve or reconcile.
        Assert.True(policy.Authorize(observer, LabHostAction.Observe, "SMU1").IsSuccess);
        Assert.Equal(BenchErrorKind.Forbidden, policy.Authorize(observer, LabHostAction.Reserve, "SMU1").Error.Kind);

        // Operator can actuate allowed SMU1 under own lease, denied on PSU2, denied raw SCPI, denied cross-principal lease hijack.
        Assert.True(policy.Authorize(op, LabHostAction.Actuate, "SMU1", leaseOwnerPrincipalId: "op1").IsSuccess);
        Assert.Equal(BenchErrorKind.Forbidden, policy.Authorize(op, LabHostAction.Actuate, "PSU2", leaseOwnerPrincipalId: "op1").Error.Kind);
        Assert.Equal("BF-SEC-RAW-SCPI-LOCKED", policy.Authorize(op, LabHostAction.RawScpi, "SMU1", leaseOwnerPrincipalId: "op1").Error.Code);
        Assert.Equal(BenchErrorKind.LeaseFenced, policy.Authorize(op, LabHostAction.Actuate, "SMU1", leaseOwnerPrincipalId: "other-op").Error.Kind);

        // Operator cannot reconcile; RecoveryAdmin can.
        Assert.Equal(BenchErrorKind.Forbidden, policy.Authorize(op, LabHostAction.Reconcile, "SMU1").Error.Kind);
        Assert.True(policy.Authorize(recovery, LabHostAction.Reconcile, "SMU1").IsSuccess);
    }

    /// <summary>Verify HashChainedAuditLog persists chained SHA-256 records and detects post-hoc file tampering.</summary>
    [Fact]
    public void HashChainedAuditLogVerifiesCleanChainAndDetectsTampering()
    {
        var dir = Path.Combine(Path.GetTempPath(), "benchforge-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "security-audit.jsonl");

        try
        {
            var log = new HashChainedAuditLog(path);
            var e1 = log.Append("op1", "Reserve", "SMU1", "Granted");
            var e2 = log.Append("op1", "OutputEnable", "SMU1", "Allowed");
            Assert.Equal(HashChainedAuditLog.GenesisHash, e1.PrevSha256);
            Assert.Equal(e1.EntrySha256, e2.PrevSha256);

            var clean = HashChainedAuditLog.VerifyChain(path);
            Assert.True(clean.IsSuccess);
            Assert.Equal(2, clean.Value.EntryCount);
            Assert.Equal(e2.EntrySha256, clean.Value.HeadSha256);

            // Tamper with entry 2 outcome text -> VerifyChain MUST fail closed.
            var text = File.ReadAllText(path).Replace("\"Allowed\"", "\"Denied\"", StringComparison.Ordinal);
            File.WriteAllText(path, text);

            var tampered = HashChainedAuditLog.VerifyChain(path);
            Assert.True(tampered.IsFailure);
            Assert.Equal("BF-AUDIT-TAMPERED", tampered.Error.Code);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
