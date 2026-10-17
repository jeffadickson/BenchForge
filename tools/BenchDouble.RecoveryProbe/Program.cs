using BenchDouble.Host.Server;
using BenchDouble.Host.Server.Recovery;
using BenchDouble.Host.Server.Setup;
using Microsoft.Extensions.Logging.Abstractions;

// Process-crash probe only: no vendor driver, physical I/O or safe-state assertion.
if (args.Length != 2 || args[0] is not ("hold" or "verify"))
    throw new ArgumentException("Usage: hold|verify <temporary journal directory>");
using var journal = new FileHostOwnershipJournal(args[1]);
var registry = new HostResourceRegistry(journal: journal);
var setup = new BenchSetupService(registry, new SimulatedSetupDiscoveryProvider(registry), Path.Combine(args[1], "unused-setup.json"));
var binding = new SetupDriverBinding(new string('a', 64), "probe", "session",
    new("probe", "IviNetSoftwareModule", "Probe.Driver, Probe"), "TCPIP0::probe::inst0::INSTR", new(), new());
var recoveries = 0;
var service = new HostedIviDmmService(registry, setup,
    [new("PROBE", [binding], _ => throw new InvalidOperationException("Probe must never open a driver"),
        _ => throw new InvalidOperationException("Probe has no live driver"), RecoverAfterRestart: () => recoveries++)],
    NullLogger<HostedIviDmmService>.Instance);
if (args[0] == "hold")
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
