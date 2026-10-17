using BenchDouble.Host.Server.Heartbeat;
using BenchDouble.Host.Server.Lifecycle;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BenchDouble.Host.Server;
using BenchDouble.Host.Server.Recovery;
using BenchDouble.Host.Server.Setup;
using Microsoft.Extensions.Logging.Abstractions;

// Process-crash probe only: no vendor driver, physical I/O or safe-state assertion.
if (args.Length != 2 || args[0] is not ("hold" or "verify" or "graceful" or "graceful-retained" or "heartbeat-expire"))
    throw new ArgumentException("Usage: hold|verify|graceful|graceful-retained|heartbeat-expire <temporary journal directory>");
using var journal = new FileHostOwnershipJournal(args[1]);
if (args[0] == "graceful-retained")
    journal.Add(new("PROBE", Guid.NewGuid(), OwnershipRecord.DigestRoutes(["TCPIP0::probe::inst0::INSTR"]), null, DateTimeOffset.UtcNow));
var registry = new HostResourceRegistry(journal: journal, heartbeatPolicy: args[0] == "heartbeat-expire"
    ? new HostHeartbeatPolicy(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(50)) : null);
var setup = new BenchSetupService(registry, new SimulatedSetupDiscoveryProvider(registry), Path.Combine(args[1], "unused-setup.json"));
var binding = new SetupDriverBinding(new string('a', 64), "probe", "session",
    new("probe", "IviNetSoftwareModule", "Probe.Driver, Probe"), "TCPIP0::probe::inst0::INSTR", new(), new());
var recoveries = 0;
var service = new HostedIviDmmService(registry, setup,
    [new("PROBE", [binding], _ => throw new InvalidOperationException("Probe must never open a driver"),
        _ => throw new InvalidOperationException("Probe has no live driver"), RecoverAfterRestart: () => recoveries++)],
    NullLogger<HostedIviDmmService>.Instance);
if (args[0] == "heartbeat-expire")
{
    var grant = registry.Reserve("PROBE") ?? throw new Exception("Reservation failed");
    var health = registry.InspectOwnerHeartbeat("PROBE", grant.LeaseId)!; var peer = Guid.NewGuid();
    registry.RenewOwnerHeartbeat("PROBE", grant.LeaseId, health.HostEpoch, health.OwnerGeneration, peer, 1);
    await Task.Delay(120);
    if (registry.ObserveOwnerHeartbeats() != 1 || registry.InspectOwnerHeartbeat("PROBE", grant.LeaseId)!.State != "Expired")
        throw new Exception("Real-time expiry failed");
    try { registry.RenewOwnerHeartbeat("PROBE", grant.LeaseId, health.HostEpoch, health.OwnerGeneration, peer, 2); throw new Exception("Expired renewal succeeded"); }
    catch (InvalidOperationException) { }
    if (registry.Reserve("PROBE") is not null || journal.Read().Length != 1) throw new Exception("Expired ownership was lost");
    Console.WriteLine("PASS: real-time heartbeat expiry fences renewal and preserves durable ownership without vendor I/O.");
}
else if (args[0].StartsWith("graceful", StringComparison.Ordinal))
{
    if (args[0] == "graceful" && registry.Reserve("PROBE") is null) throw new Exception("Reservation failed");
    var builder = Host.CreateApplicationBuilder(); builder.Logging.ClearProviders();
    builder.Services.AddSingleton(registry);
    builder.Services.AddSingleton(new HostShutdownPolicy(TimeSpan.FromMilliseconds(250)));
    builder.Services.AddHostedService<CanonicalShutdownService>();
    using var host = builder.Build();
    host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => { Console.WriteLine("READY"); Console.Out.Flush(); });
    await host.RunAsync();
    var report = registry.ShutdownReport ?? throw new Exception("No shutdown decision");
    var expected = args[0] == "graceful" ? "Closed" : "StartupQuarantine";
    if (report.Items.Single().Outcome != expected) throw new Exception("Unexpected shutdown decision");
}
else if (args[0] == "hold")
{
    if (registry.Reserve("PROBE") is null || journal.Read().Length != 1) throw new Exception("Ownership was not persisted");
    Console.WriteLine("READY");
    Console.Out.Flush();
    await Task.Delay(Timeout.InfiniteTimeSpan); // Harness forcibly kills this process; no Dispose runs.
}
else
{
    var record = registry.PendingRecovery().Single();
    if (record.ResourceId != "PROBE" || registry.Reserve("PROBE") is not null) throw new Exception("Restart quarantine missing");
    try { service.ReconcileRestart("PROBE", Guid.NewGuid(), true); throw new Exception("Stale review accepted"); }
    catch (InvalidOperationException) { }
    if (recoveries != 0 || journal.Read().Length != 1) throw new Exception("Stale review changed ownership");
    service.ReconcileRestart("PROBE", record.RecordId, true);
    if (recoveries != 1 || journal.Read().Length != 0 || registry.PendingRecovery().Length != 0) throw new Exception("Reconciliation incomplete");
    var grant = registry.Reserve("PROBE") ?? throw new Exception("Fresh reservation blocked");
    if (!registry.Release(grant.LeaseId) || journal.Read().Length != 0) throw new Exception("Cleanup incomplete");
    Console.WriteLine("PASS: killed owner stayed quarantined until explicit reconciliation; fresh ownership and cleanup succeeded.");
}
