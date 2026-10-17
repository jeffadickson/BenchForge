using Xunit;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using BenchDouble.Host.Client;
using BenchDouble.Host.Server.Setup;

namespace BenchDouble.Host.Tests;

/// <summary>Local lifecycle, routing and discovery regression checks.</summary>
public sealed class LocalHardeningTests
{
    /// <summary>Invalid route components and timer budgets fail before starting a loop.</summary>
    [Fact]
    public void HeartbeatRejectsInvalidConfiguration()
    {
        using var client = new HttpClient();
        foreach (var id in new[] { "meter/1", "meter\\1", "meter\n", "meter?token=x", new string('x', 81) })
            Assert.Throws<ArgumentException>(() => new ScopeLeaseHeartbeat(client, id, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScopeLeaseHeartbeat(client, "meter", Guid.NewGuid(), TimeSpan.MaxValue));
    }

// CASES
}
