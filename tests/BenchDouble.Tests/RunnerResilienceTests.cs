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
        {
            /// <inheritdoc/>
            public void Dispose(){if(owner.ThrowDispose)throw new IOException("dispose");}
        }
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

    /// <summary>Procedures receive the isolated logger, including enabled checks.</summary>
    /// <returns>Completion after diagnostic failure counting.</returns>
    [Fact] public async Task ContextEnabledFailureIsIsolated()
    {
        var runner=Runner(new Logger{ThrowEnabled=true});var p=new Procedure{Execute=(c,_)=>{Assert.False(c.Logger.IsEnabled(LogLevel.Debug));return Task.CompletedTask;}};
        Assert.Equal(ExperimentStatus.Completed,(await runner.RunAsync(p)).Status);Assert.Equal(1,runner.LoggingFailureCount);
    }
    /// <summary>Original execution errors survive broken diagnostics and mandatory cleanup.</summary>
    /// <returns>Completion after the failed run.</returns>
    [Fact] public async Task OriginalExecutionErrorSurvivesLogging()
    {
        var error=new IOException("private-provider-payload");var p=new Procedure{Execute=(_,_)=>throw error};var runner=Runner(new Logger{ThrowWrites=true,ThrowDispose=true});
        var run=await runner.RunAsync(p);Assert.Same(error,run.Error);Assert.Equal(ExperimentStatus.Failed,run.Status);Assert.Equal(1,p.Cleanups);Assert.False(runner.RequiresRecovery);
    }
    /// <summary>Shutdown failure keeps recovery fencing even with broken diagnostics.</summary>
    /// <returns>Completion after recovery-required classification.</returns>
    [Fact] public async Task CleanupFailureSurvivesLogging()
    {
        var error=new IOException("cleanup");var p=new Procedure{Stop=(_,_)=>throw error};var runner=Runner(new Logger{ThrowWrites=true});var run=await runner.RunAsync(p);
        Assert.Same(error,run.Error);Assert.Equal(ExperimentStatus.RecoveryRequired,run.Status);await Assert.ThrowsAsync<InvalidOperationException>(()=>runner.RunAsync(new Procedure()));
    }
    /// <summary>Execution and cleanup failures retain both exact exception instances.</summary>
    /// <returns>Completion after aggregate error inspection.</returns>
    [Fact] public async Task OriginalErrorsAreAggregated()
    {
        var first=new IOException("first");var second=new InvalidOperationException("second");var run=await Runner().RunAsync(new Procedure{Execute=(_,_)=>throw first,Stop=(_,_)=>throw second});
        var error=Assert.IsType<AggregateException>(run.Error);Assert.Same(first,error.InnerExceptions[0]);Assert.Same(second,error.InnerExceptions[1]);
    }
    /// <summary>Throwing token callbacks cannot escape timeout fencing or hide cleanup uncertainty.</summary>
    /// <returns>Completion after the bounded decision and pending task release.</returns>
    [Fact] public async Task TimeoutWithThrowingCancellationCallbackStaysFenced()
    {
        var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var runner=Runner(timeout:TimeSpan.FromMilliseconds(20));
        try
        {
            var run=await runner.RunAsync(new Procedure{Stop=(_,ct)=>{ct.Register(()=>throw new IOException("cancel callback"));return pending.Task;}}).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ExperimentStatus.RecoveryRequired,run.Status);Assert.True(runner.RequiresRecovery);var errors=Assert.IsType<AggregateException>(run.Error).Flatten().InnerExceptions;
            Assert.Contains(errors,e=>e is TimeoutException);Assert.Contains(errors,e=>e is IOException);await Assert.ThrowsAsync<InvalidOperationException>(()=>runner.RunAsync(new Procedure()));
        }finally{pending.TrySetResult();}
    }
    /// <summary>Ignored cancellation at a phase boundary still prevents successful completion.</summary>
    /// <param name="startup">True cancels during startup, false during execution.</param>
    /// <returns>Completion after independent cleanup.</returns>
    [Theory] [InlineData(true)] [InlineData(false)] public async Task IgnoredPhaseCancellationIsObserved(bool startup)
    {
        using var cancel=new CancellationTokenSource();var p=new Procedure();if(startup)p.Start=(_,_)=>{cancel.Cancel();return Task.CompletedTask;};else p.Execute=(_,_)=>{cancel.Cancel();return Task.CompletedTask;};
        var run=await Runner().RunAsync(p,cancel.Token);Assert.Equal(ExperimentStatus.Cancelled,run.Status);Assert.Equal(startup?0:1,p.Executions);Assert.Equal(1,p.Cleanups);
    }
    /// <summary>Cleanup receives an independent token after caller cancellation.</summary>
    /// <returns>Completion after cancellation and cleanup observation.</returns>
    [Fact] public async Task CleanupTokenIsIndependent()
    {
        using var cancel=new CancellationTokenSource();var p=new Procedure{Execute=(_,_)=>{cancel.Cancel();return Task.CompletedTask;},Stop=(_,ct)=>{Assert.False(ct.IsCancellationRequested);return Task.CompletedTask;}};
        Assert.Equal(ExperimentStatus.Cancelled,(await Runner().RunAsync(p,cancel.Token)).Status);
    }
    /// <summary>An unrelated cancellation exception is a failure rather than caller cancellation.</summary>
    /// <returns>Completion after classification.</returns>
    [Fact] public async Task ForeignCancellationIsFailure()
    {
        var error=new OperationCanceledException();var run=await Runner().RunAsync(new Procedure{Execute=(_,_)=>throw error});Assert.Equal(ExperimentStatus.Failed,run.Status);Assert.Same(error,run.Error);
    }
    /// <summary>Null lifecycle tasks report explicit failures with cleanup semantics preserved.</summary>
    /// <param name="phase">Phase returning a null task.</param>
    /// <returns>Completion after lifecycle classification.</returns>
    [Theory] [InlineData("startup")] [InlineData("execute")] [InlineData("shutdown")] public async Task NullTasksAreExplicit(string phase)
    {
        var p=new Procedure();if(phase=="startup")p.Start=(_,_)=>null!;else if(phase=="execute")p.Execute=(_,_)=>null!;else p.Stop=(_,_)=>null!;
        var run=await Runner().RunAsync(p);Assert.IsType<InvalidOperationException>(run.Error);Assert.Equal(phase=="shutdown"?ExperimentStatus.RecoveryRequired:ExperimentStatus.Failed,run.Status);Assert.Equal(1,p.Cleanups);
    }
    /// <summary>Procedure names are read once and cannot change the final run identity.</summary>
    /// <returns>Completion after name mutation during execution.</returns>
    [Fact] public async Task ProcedureNameIsPinned()
    {
        var p=new Procedure();p.Execute=(_,_)=>{p.Label="changed";return Task.CompletedTask;};var result=await Runner().RunAsync(p);Assert.Equal("run",result.Name);Assert.Equal(1,p.Names);
    }
    /// <summary>Invalid display names fail before any lifecycle phase executes.</summary>
    /// <param name="name">Invalid display name.</param>
    /// <returns>Completion after rejected admission.</returns>
    [Theory] [InlineData("")] [InlineData(" ")] [InlineData("run\n")] [InlineData("run\0")] public async Task InvalidNamesFailBeforeStartup(string name)
    {
        var p=new Procedure{Label=name};await Assert.ThrowsAsync<ArgumentException>(()=>Runner().RunAsync(p));Assert.Equal(0,p.Starts);Assert.Equal(0,p.Cleanups);
    }
    /// <summary>Name length limits are inclusive and rejected attempts leave admission usable.</summary>
    /// <returns>Completion after rejected and accepted names.</returns>
    [Fact] public async Task NameBoundsLeaveGateUsable()
    {
        var runner=Runner();await Assert.ThrowsAsync<ArgumentException>(()=>runner.RunAsync(new Procedure{Label=new string('x',129)}));Assert.Equal(ExperimentStatus.Completed,(await runner.RunAsync(new Procedure{Label=new string('x',128)})).Status);
    }
    /// <summary>Unsupported timer intervals fail at construction, without running a procedure.</summary>
    [Fact] public void TimerPolicyBoundsAreValidated()
    {
        Runner(timeout:TimeSpan.FromMilliseconds(uint.MaxValue-1));Assert.Throws<ArgumentOutOfRangeException>(()=>Runner(timeout:TimeSpan.MaxValue));Assert.Throws<ArgumentOutOfRangeException>(()=>Runner(timeout:TimeSpan.Zero));
    }
    /// <summary>Runner events expose stable IDs without forwarding provider exception payloads.</summary>
    /// <returns>Completion after diagnostic inspection.</returns>
    [Fact] public async Task LifecycleEventsArePayloadFree()
    {
        var logger=new Logger();var error=new IOException("private-provider-payload");var run=await Runner(logger).RunAsync(new Procedure{Execute=(_,_)=>throw error});
        Assert.Same(error,run.Error);Assert.Contains(logger.Events,e=>e.Id.Id==7700);Assert.Contains(logger.Events,e=>e.Id.Id==7702);Assert.Contains(logger.Events,e=>e.Id.Id==7705);
        Assert.All(logger.Events,e=>{Assert.Null(e.Error);Assert.DoesNotContain("private-provider-payload",e.Text);});
    }
    /// <summary>Concurrent callers serialize complete lifecycle transactions on a shared runner.</summary>
    /// <returns>Completion after all callers finish.</returns>
    [Fact] public async Task ConcurrentRunsAreSerialized()
    {
        var active=0;var runner=Runner();await Task.WhenAll(Enumerable.Range(0,16).Select(_=>Task.Run(async()=>await runner.RunAsync(new Procedure{Start=(_,_)=>{Assert.Equal(1,Interlocked.Increment(ref active));return Task.CompletedTask;},Execute=async(_,_)=>await Task.Yield(),Stop=(_,_)=>{Assert.Equal(0,Interlocked.Decrement(ref active));return Task.CompletedTask;}}))));Assert.Equal(0,active);
    }
}
