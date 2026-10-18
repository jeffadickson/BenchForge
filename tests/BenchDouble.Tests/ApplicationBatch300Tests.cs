using BenchDouble.Generated;
using BenchDouble.Simulation;
using UnitsNet;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Application boundary regressions accumulated for the 100/150 checkpoints.</summary>
public sealed class ApplicationBatch300Tests
{
    private static MockScpiParameter Parameter(string name = "voltage", string? query = "V{channel}?", string? setter = "V{channel} {value}") =>
        new(name, "ElectricPotential", "Volt", query, setter, 1, 0, 10);
    private static DefinitionDrivenScpiMockSession Session() => new(2, [Parameter()]);
    private static AutoMockLoadRule Rule(int index) => new("rule" + index, 0, new(), AutoMockLoadActionKind.FixedCurrent, 1);
    private static ILoadBehavior Baseline() => new ResistiveLoadBehavior(ElectricResistance.FromOhms(100));
    private const string Definition = """
        {"schemaVersion":2,"identity":{"driverName":"BatchMeter"},"modes":[{"id":"voltage","selectionCommand":"CONF:VOLT"}],
         "capabilities":[{"name":"voltage","quantity":"ElectricPotential","unit":"Volt","access":"read","query":"MEAS:VOLT?","modes":["voltage"],"mockDefault":1}]}
        """;

    /// <summary>Null entries fail at the metadata boundary.</summary>
    [Fact] public void NullMetadataIsRejected() => Assert.Throws<ArgumentNullException>(() => new DefinitionDrivenScpiMockSession(1, [null!]));

    /// <summary>Over-limit enumeration stops before consuming unrelated later elements.</summary>
    [Fact] public void MetadataEnumerationIsBounded() => Assert.Throws<ArgumentException>(() =>
        new DefinitionDrivenScpiMockSession(1, Enumerable.Range(0, 514).Select(i => i == 513 ? throw new InvalidOperationException("Over-enumerated") : Parameter("p" + i))));

    /// <summary>Blank commands cannot create an unusable parameter.</summary>
    [Fact] public void BlankTemplatesAreRejected() => Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1, [Parameter(query: " ", setter: null)]));

    /// <summary>One query cannot silently select one of two different state variables.</summary>
    [Fact] public void DuplicateQueriesAreRejected() => Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1, [Parameter(setter: null), Parameter("other", setter: null)]));

    /// <summary>One setter cannot silently update only the first parameter.</summary>
    [Fact] public void DuplicateSettersAreRejected() => Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1, [Parameter(query: null), Parameter("other", query: null)]));

    /// <summary>Repeated numeric groups do not silently discard the first value.</summary>
    [Fact] public void RepeatedValuesAreRejected() => Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1, [Parameter(setter: "V {value} {value}")]));

    /// <summary>Repeated channel groups cannot route a command by its last occurrence.</summary>
    [Fact] public void RepeatedChannelsAreRejected() => Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1, [Parameter(query: "V{channel}:{channel}?")]));

    /// <summary>A trailing newline is not part of a legal definition template.</summary>
    [Fact] public void TemplateNewlineIsRejected() => Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1, [Parameter(query: "V?\n")]));

    /// <summary>Rejected query suffixes leave the accepted-command transcript unchanged.</summary>
    /// <returns>Completion after the command is rejected.</returns>
    [Fact] public async Task QueryNewlineIsRejected()
    {
        var session = Session();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await session.QueryAsync("V1?\n"));
        Assert.Empty(session.Transcript);
    }

    /// <summary>Rejected setter suffixes cannot mutate stored readings.</summary>
    /// <returns>Completion after state is checked.</returns>
    [Fact] public async Task SetterNewlinePreservesState()
    {
        var session = Session();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await session.WriteAsync("V1 9\n"));
        Assert.Equal("1", await session.QueryAsync("V1?"));
        Assert.Single(session.Transcript);
    }

    /// <summary>Oversized write and query requests fail without recording input.</summary>
    /// <returns>Completion after both entry points reject input.</returns>
    [Fact] public async Task OversizedCommandsAreRejected()
    {
        var session = Session(); var input = new string('V', 4097);
        await Assert.ThrowsAsync<ArgumentException>(async () => await session.QueryAsync(input));
        await Assert.ThrowsAsync<ArgumentException>(async () => await session.WriteAsync(input));
        Assert.Empty(session.Transcript);
    }

    /// <summary>Oversized templates fail before regex compilation.</summary>
    [Fact] public void OversizedTemplateIsRejected() => Assert.Throws<ArgumentException>(() => new DefinitionDrivenScpiMockSession(1, [Parameter(query: new string('V', 4097))]));

    /// <summary>An overflowing channel is a range error and does not affect channel one.</summary>
    /// <returns>Completion after rejection and valid state read.</returns>
    [Fact] public async Task OverflowingChannelPreservesState()
    {
        var session = Session();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.WriteAsync("V999999999999999999999 9"));
        Assert.Equal("1", await session.QueryAsync("V1?"));
    }

    /// <summary>Decorator construction enforces the same bounded rule contract as the builder.</summary>
    [Fact] public void DecoratorEnumerationIsBounded() => Assert.Throws<ArgumentException>(() => new RuleBasedLoadBehavior(Baseline(),
        Enumerable.Range(0, 258).Select(i => i == 257 ? throw new InvalidOperationException("Over-enumerated") : Rule(i))));

    /// <summary>Fingerprinting cannot enumerate an unbounded rule source.</summary>
    [Fact] public void FingerprintEnumerationIsBounded() => Assert.Throws<ArgumentException>(() => AutoMockRuleFiles.Sha256(
        Enumerable.Range(0, 258).Select(i => i == 257 ? throw new InvalidOperationException("Over-enumerated") : Rule(i))));

    /// <summary>An unconstrained condition does not accidentally accept a nonfinite voltage.</summary>
    [Fact] public void ConditionRejectsNonfiniteVoltage() => Assert.Throws<ArgumentOutOfRangeException>(() => new AutoMockLoadCondition().Matches(ElectricPotential.FromKilovolts(double.MaxValue), TimeSpan.Zero));

    /// <summary>Conditions reject times outside the run's nonnegative logical domain.</summary>
    [Fact] public void ConditionRejectsNegativeTime() => Assert.Throws<ArgumentOutOfRangeException>(() => new AutoMockLoadCondition().Matches(ElectricPotential.FromVolts(1), TimeSpan.FromTicks(-1)));

    /// <summary>A typed but nonfinite model cannot masquerade as a valid reading.</summary>
    /// <returns>Completion after model output rejection.</returns>
    [Fact] public async Task ModeAwareRejectsNonfiniteReading()
    {
        var instrument = new ModeAwareMockBuilder().Mode("voltage").Measurement("voltage", _ => ElectricPotential.FromKilovolts(double.MaxValue)).Build(new ManualSimulationClock());
        await instrument.ConfigureModeAsync("voltage");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await instrument.MeasureAsync<ElectricPotential>());
    }

    /// <summary>Cooperative callbacks cannot turn caller cancellation into success.</summary>
    /// <returns>Completion after cancellation is observed.</returns>
    [Fact] public async Task ModeAwareObservesCallbackCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var instrument = new ModeAwareMockBuilder().Mode("voltage").Measurement("voltage", _ => { cancellation.Cancel(); return ElectricPotential.FromVolts(1); }).Build(new ManualSimulationClock());
        await instrument.ConfigureModeAsync("voltage");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await instrument.MeasureAsync<ElectricPotential>(cancellation.Token));
    }

    /// <summary>Reinstalling the same delegate is not a simulated state change.</summary>
    /// <returns>Completion after sync and async model checks.</returns>
    [Fact] public async Task IdenticalModelsPreserveRevision()
    {
        var engine = new StatefulMockEngine(ImportedInstrumentDefinition.Parse(Definition));
        await engine.ConfigureModeAsync("voltage");
        Func<MockReadingContext,double> model = _ => 2;
        engine.SetBehavior("voltage", model); var revision = engine.Snapshot().Revision;
        engine.SetBehavior("voltage", model); Assert.Equal(revision, engine.Snapshot().Revision);
        Func<MockReadingContext,CancellationToken,ValueTask<double>> asyncModel = (_,_) => ValueTask.FromResult(3d);
        engine.SetAsyncBehavior("voltage", asyncModel); revision = engine.Snapshot().Revision;
        engine.SetAsyncBehavior("voltage", asyncModel); Assert.Equal(revision, engine.Snapshot().Revision);
        Assert.Equal(3, await engine.MeasureAsync("voltage"));
    }

    /// <summary>Concurrent writes preserve independent channels and accepted-command accounting.</summary>
    /// <returns>Completion after parallel mutations and reads.</returns>
    [Fact] public async Task ParallelScpiWritesRemainCoherent()
    {
        var session = Session();
        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(async () => await session.WriteAsync($"V{1 + i % 2} {2 + i % 2}"))));
        Assert.Equal("2", await session.QueryAsync("V1?")); Assert.Equal("3", await session.QueryAsync("V2?"));
        Assert.Equal(66, session.Transcript.Count);
    }
}
