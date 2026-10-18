using BenchDouble.Host.Server;
using BenchDouble.Host.Server.Setup;
using System.Text.Json;
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
    /// <summary>All supported boundaries and fractional policies survive strict import and persistence.</summary>
    /// <param name="limit">Accepted saved freshness policy.</param>
    [Theory] [InlineData(1.0)] [InlineData(1.25)] [InlineData(86400000.0)] [InlineData(null)] public void AcceptedPoliciesRoundTrip(double? limit)
    {
        using var f=new Fixture();var configuration=BenchSetupService.Parse(JsonSerializer.Serialize(Configuration(limit),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        f.Setup.Save(new(configuration,null,true));Assert.Equal(limit,Assert.Single(f.Setup.Read()!.Configuration.Roles).ReadingFreshnessMilliseconds);
    }
    /// <summary>Invalid policies cannot replace the existing revision or file bytes.</summary>
    /// <param name="limit">Rejected policy.</param>
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(0.99)] [InlineData(86400001)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void InvalidSavesPreservePriorEvidence(double limit)
    {
        using var f=new Fixture();var saved=f.Setup.Save(new(Configuration(1000),null,true));var before=File.ReadAllBytes(f.FilePath);
        Assert.ThrowsAny<ArgumentException>(()=>f.Setup.Save(new(Configuration(limit),saved.Revision,true)));Assert.Equal(before,File.ReadAllBytes(f.FilePath));Assert.Equal(saved.Revision,f.Setup.Read()!.Revision);
    }
    /// <summary>Invalid imported policy fields fail structural validation.</summary>
    /// <param name="limit">Rejected numeric policy.</param>
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(86400001)] public void InvalidImportsAreRejected(double limit)
    {
        var json=JsonSerializer.Serialize(Configuration(limit),new JsonSerializerOptions(JsonSerializerDefaults.Web));Assert.Throws<ArgumentException>(()=>BenchSetupService.Parse(json));
    }
    /// <summary>Saved-file reads reject corrupted policies instead of treating the setup as absent.</summary>
    [Fact] public void InvalidSavedPolicyIsRejectedOnRead()
    {
        using var f=new Fixture();f.Setup.Save(new(Configuration(1000),null,true));var original=File.ReadAllBytes(f.FilePath);
        File.WriteAllText(f.FilePath,JsonSerializer.Serialize(new{draft=true,configuration=Configuration(-1)},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Throws<ArgumentException>(()=>f.Setup.Read());File.WriteAllBytes(f.FilePath,original);Assert.Equal(1000,Assert.Single(f.Setup.Read()!.Configuration.Roles).ReadingFreshnessMilliseconds);
    }
    /// <summary>A policy update changes its revision and rejects a stale writer.</summary>
    [Fact] public void PolicyChangesParticipateInRevisionChecks()
    {
        using var f=new Fixture();var first=f.Setup.Save(new(Configuration(1000),null,true));var second=f.Setup.Save(new(Configuration(2000),first.Revision,true));Assert.NotEqual(first.Revision,second.Revision);
        Assert.Throws<InvalidOperationException>(()=>f.Setup.Save(new(Configuration(3000),first.Revision,true)));Assert.Equal(second.Revision,f.Setup.Read()!.Revision);
    }
    /// <summary>Policies can be cleared explicitly to restore unspecified freshness.</summary>
    [Fact] public void SavedPolicyCanBeCleared()
    {
        using var f=new Fixture();var first=f.Setup.Save(new(Configuration(1000),null,true));f.Setup.Save(new(Configuration(null),first.Revision,true));Assert.Null(Assert.Single(f.Setup.Read()!.Configuration.Roles).ReadingFreshnessMilliseconds);
    }
    /// <summary>Mutating caller role arrays cannot alter a committed policy.</summary>
    [Fact] public void SavedRolesAreDetached()
    {
        using var f=new Fixture();var configuration=Configuration(1000);f.Setup.Save(new(configuration,null,true));configuration.Roles[0]=new("probe","meter",9000);
        var read=f.Setup.Read()!;Assert.Equal(1000,read.Configuration.Roles[0].ReadingFreshnessMilliseconds);read.Configuration.Roles[0]=new("probe","meter",7000);Assert.Equal(1000,f.Setup.Read()!.Configuration.Roles[0].ReadingFreshnessMilliseconds);
    }
    /// <summary>Omitted fields in old role JSON preserve unspecified policy.</summary>
    [Fact] public void LegacyJsonDoesNotInventPolicy()
    {
        var role=JsonSerializer.Deserialize<SetupRole>("{\"role\":\"probe\",\"instrumentId\":\"meter\"}",new JsonSerializerOptions(JsonSerializerDefaults.Web));Assert.Null(role!.ReadingFreshnessMilliseconds);
    }
    /// <summary>Malformed duplicate and unsupported role policy keys do not bypass strict import.</summary>
    [Fact] public void DuplicatePolicyFieldsAreRejected()
    {
        var json=JsonSerializer.Serialize(Configuration(1000),new JsonSerializerOptions(JsonSerializerDefaults.Web));json=json.Replace("\"readingFreshnessMilliseconds\":1000","\"readingFreshnessMilliseconds\":1000,\"readingFreshnessMilliseconds\":2000");Assert.Throws<JsonException>(()=>BenchSetupService.Parse(json));
    }
}
