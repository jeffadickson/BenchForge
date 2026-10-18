using BenchDouble.Host.Server;
using BenchDouble.Host.Server.Setup;
using Xunit;

namespace BenchDouble.Host.Tests;

/// <summary>Saved experiment-role freshness validation, persistence and compatibility regressions.</summary>
public sealed class SavedFreshnessPolicyTests
{
    private sealed class Fixture : IDisposable
    {
        internal readonly string DirectoryPath=Path.Combine(Path.GetTempPath(),"saved-freshness-"+Guid.NewGuid());
        internal readonly string FilePath;
        internal readonly BenchSetupService Setup;
        internal Fixture(){FilePath=Path.Combine(DirectoryPath,"setup.json");var registry=new HostResourceRegistry();Setup=new(registry,new SimulatedSetupDiscoveryProvider(registry),FilePath);}
        /// <inheritdoc/>
        public void Dispose(){if(Directory.Exists(DirectoryPath))Directory.Delete(DirectoryPath,true);}
    }
    private static BenchSetup Configuration(double? limit)=>new(1,[],[new("meter","Meter","primary",[new("primary","simulated","USB","1")])],[new("probe","meter",limit)]);
    /// <summary>Legacy two-field construction and deconstruction retain omitted freshness semantics.</summary>
    [Fact] public void LegacyRoleContractRemainsAvailable()
    {
        var role=new SetupRole("probe","meter");var (name,instrument)=role;Assert.Equal("probe",name);Assert.Equal("meter",instrument);Assert.Null(role.ReadingFreshnessMilliseconds);
    }
    /// <summary>Saved role policies round-trip independently of device discovery and activation.</summary>
    [Fact] public void RolePolicyPersistsInOfflineDraft()
    {
        using var f=new Fixture();var saved=f.Setup.Save(new(Configuration(2500),null,true));var read=f.Setup.Read()!;Assert.Equal(saved.Revision,read.Revision);Assert.Equal(2500,Assert.Single(read.Configuration.Roles).ReadingFreshnessMilliseconds);
    }
}
