using BenchDouble.Experiments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BenchDouble.Tests;

/// <summary>Runner diagnostics, cancellation and cleanup regression checks.</summary>
public sealed class RunnerResilienceTests
{
    private sealed class Procedure : ExperimentProcedure
    {
        internal int Starts,Executions,Cleanups,Names;
        internal string Label="run";
        internal Func<ExperimentContext,CancellationToken,Task> Start=(_,_)=>Task.CompletedTask,Execute=(_,_)=>Task.CompletedTask,Stop=(_,_)=>Task.CompletedTask;
        /// <inheritdoc/>
        public override string Name { get { ++Names;return Label; } }
        /// <inheritdoc/>
        public override Task StartupAsync(ExperimentContext context,CancellationToken token){++Starts;return Start(context,token);}
        /// <inheritdoc/>
        public override Task ExecuteAsync(ExperimentContext context,CancellationToken token){++Executions;return Execute(context,token);}
        /// <inheritdoc/>
        public override Task ShutdownAsync(ExperimentContext context,CancellationToken token){++Cleanups;return Stop(context,token);}
    }
    private sealed class Logger : ILogger<ExperimentRunner>
    {
        internal bool ThrowWrites,ThrowScope,ThrowDispose,ThrowEnabled;
        internal readonly List<(EventId Id,string Text,Exception? Error)> Events=[];
        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull => ThrowScope?throw new IOException("scope"):new Scope(this);
        /// <inheritdoc/>
        public bool IsEnabled(LogLevel level)=>ThrowEnabled?throw new IOException("enabled"):true;
        /// <inheritdoc/>
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter)
        {if(ThrowWrites)throw new IOException("write");Events.Add((id,formatter(state,error),error));}
        private sealed class Scope(Logger owner):IDisposable
        { public void Dispose(){if(owner.ThrowDispose)throw new IOException("dispose");} }
    }
    private static ExperimentRunner Runner(ILogger<ExperimentRunner>? logger=null,TimeSpan? timeout=null)=>new(new InMemoryExperimentResultSink(),logger??NullLogger<ExperimentRunner>.Instance,new(){ShutdownTimeout=timeout??TimeSpan.FromSeconds(1)});

    /// <summary>Broken diagnostic writes cannot suppress procedure execution or cleanup.</summary>
    /// <returns>Completion after the successful run.</returns>
    [Fact] public async Task ThrowingWritesPreserveCleanup()
    {
        var logger=new Logger{ThrowWrites=true};var runner=Runner(logger);var procedure=new Procedure();var result=await runner.RunAsync(procedure);
        Assert.Equal(ExperimentStatus.Completed,result.Status);Assert.Equal(1,procedure.Cleanups);Assert.True(runner.LoggingFailureCount>=2);
    }
    /// <summary>Diagnostic scope creation and disposal failures do not change the outcome.</summary>
    /// <param name="creation">True tests scope creation, false scope disposal.</param>
    /// <returns>Completion after scope failure isolation.</returns>
    [Theory] [InlineData(true)] [InlineData(false)] public async Task ScopeFailuresAreIsolated(bool creation)
    {
        var logger=new Logger{ThrowScope=creation,ThrowDispose=!creation};var runner=Runner(logger);var procedure=new Procedure();
        Assert.Equal(ExperimentStatus.Completed,(await runner.RunAsync(procedure)).Status);Assert.Equal(1,procedure.Cleanups);Assert.Equal(1,runner.LoggingFailureCount);
    }
}
