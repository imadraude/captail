namespace Captail.Tests;

using Xunit;

public sealed class ReplayRuntimeTests
{
    [Fact]
    public async Task EnablingStartsPipelineBeforePersistingRunningState()
    {
        var events = new List<string>();
        var pipeline = new RecordingPipeline(events);
        var store = new RecordingConfigStore(events);
        var config = new Config { ReplayEnabled = false };
        await using var runtime = new ReplayRuntime(
            config,
            new RecordingPipelineFactory(pipeline, events),
            store);

        ReplayCommandResult result = await runtime.SetEnabledAsync(true);

        Assert.True(result.Succeeded);
        Assert.Equal(ReplayRuntimeState.Running, runtime.Snapshot.State);
        Assert.True(runtime.Snapshot.ReplayEnabled);
        Assert.Equal(["create", "start", "save:true"], events);
    }

    [Fact]
    public async Task ApplyingPipelineChangeRestartsBeforePersistingConfiguration()
    {
        var events = new List<string>();
        var store = new RecordingConfigStore(
            events,
            configuration => configuration.BitrateMbps.ToString());
        var factory = new SequencedPipelineFactory(events);
        var config = new Config { ReplayEnabled = false, BitrateMbps = 20 };
        await using var runtime = new ReplayRuntime(config, factory, store);
        await runtime.SetEnabledAsync(true);
        events.Clear();
        Config candidate = config.Clone();
        candidate.ReplayEnabled = true;
        candidate.BitrateMbps = 50;

        ReplayCommandResult result = await runtime.ApplyConfigurationAsync(candidate);

        Assert.True(result.Succeeded);
        Assert.Equal(ReplayRuntimeState.Running, runtime.Snapshot.State);
        Assert.Equal(["dispose:1", "create:50", "start:2", "save:50"], events);
    }

    [Fact]
    public async Task FailedPipelineChangeRestoresPreviousRunningConfiguration()
    {
        var events = new List<string>();
        var store = new RecordingConfigStore(
            events,
            configuration => configuration.BitrateMbps.ToString());
        var factory = new FailingSecondPipelineFactory(events);
        var config = new Config { ReplayEnabled = false, BitrateMbps = 20 };
        await using var runtime = new ReplayRuntime(config, factory, store);
        await runtime.SetEnabledAsync(true);
        events.Clear();
        Config candidate = config.Clone();
        candidate.ReplayEnabled = true;
        candidate.BitrateMbps = 50;

        ReplayCommandResult result = await runtime.ApplyConfigurationAsync(candidate);

        Assert.False(result.Succeeded);
        Assert.Equal(ReplayRuntimeState.Running, runtime.Snapshot.State);
        Assert.Equal(
            [
                "dispose:1",
                "create:50",
                "start:2:fail",
                "dispose:2",
                "save:20",
                "create:20",
                "start:3",
            ],
            events);
    }

    [Fact]
    public async Task EnablingWaitsForPipelineStartBeforePersistingConfiguration()
    {
        var events = new List<string>();
        var pipeline = new BlockingPipeline(events);
        var config = new Config { ReplayEnabled = false };
        await using var runtime = new ReplayRuntime(
            config,
            new RecordingPipelineFactory(pipeline, events),
            new RecordingConfigStore(events));

        Task<ReplayCommandResult> enabling = runtime.SetEnabledAsync(true);
        await pipeline.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["create", "start:pending"], events);
        pipeline.AllowStart.SetResult();
        ReplayCommandResult result = await enabling;

        Assert.True(result.Succeeded);
        Assert.Equal(["create", "start:pending", "start:done", "save:true"], events);
    }

    [Fact]
    public async Task ConfigurationWaitsForActiveReplaySave()
    {
        var events = new List<string>();
        var pipeline = new BlockingSavePipeline(events);
        var config = new Config { ReplayEnabled = false, BitrateMbps = 20 };
        await using var runtime = new ReplayRuntime(
            config,
            new RecordingPipelineFactory(pipeline, events),
            new RecordingConfigStore(events));
        await runtime.SetEnabledAsync(true);
        events.Clear();

        Task<string> saving = runtime.SaveAsync();
        await pipeline.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Config candidate = config.Clone();
        candidate.ReplayEnabled = true;
        candidate.BitrateMbps = 50;
        Task<ReplayCommandResult> applying = runtime.ApplyConfigurationAsync(candidate);

        Assert.Equal(["save:pending"], events);
        pipeline.AllowSave.SetResult();
        Assert.Equal("replay.mp4", await saving);
        await applying;
        Assert.Equal(
            ["save:pending", "save:done", "dispose", "create", "start", "save:true"],
            events);
    }

    [Fact]
    public async Task ShutdownRunsAfterActiveSaveAndRejectsQueuedConfiguration()
    {
        var events = new List<string>();
        var pipeline = new BlockingSavePipeline(events);
        var config = new Config { ReplayEnabled = false, BitrateMbps = 20 };
        await using var runtime = new ReplayRuntime(
            config,
            new RecordingPipelineFactory(pipeline, events),
            new RecordingConfigStore(events));
        await runtime.SetEnabledAsync(true);
        events.Clear();

        Task<string> saving = runtime.SaveAsync();
        await pipeline.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Config candidate = config.Clone();
        candidate.ReplayEnabled = true;
        candidate.BitrateMbps = 50;
        Task<ReplayCommandResult> applying = runtime.ApplyConfigurationAsync(candidate);
        Task shutdown = runtime.ShutdownAsync();

        pipeline.AllowSave.SetResult();
        await saving;
        await shutdown;

        await Assert.ThrowsAsync<ObjectDisposedException>(() => applying);
        Assert.Equal(["save:pending", "save:done", "dispose"], events);
        Assert.Equal(ReplayRuntimeState.Disabled, runtime.Snapshot.State);
    }

    [Fact]
    public async Task DuplicateRecoveryRequestsShareOnePipelineRestart()
    {
        var events = new List<string>();
        var factory = new BlockingRecoveryPipelineFactory(events);
        var config = new Config { ReplayEnabled = false };
        await using var runtime = new ReplayRuntime(
            config,
            factory,
            new RecordingConfigStore(events));
        await runtime.SetEnabledAsync(true);
        events.Clear();

        Task<ReplayCommandResult> first = runtime.RecoverAsync("pipeline fault");
        await factory.RecoveryStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<ReplayCommandResult> duplicate = runtime.RecoverAsync("second signal");

        Assert.Same(first, duplicate);
        factory.AllowRecovery.SetResult();
        Assert.True((await first).Succeeded);
        Assert.Equal(["dispose:1", "create:2", "start:2"], events);
        Assert.Equal(ReplayRuntimeState.Running, runtime.Snapshot.State);
    }

    [Fact]
    public async Task PersistenceFailureDisposesCandidateBeforeRollback()
    {
        var events = new List<string>();
        var factory = new SequencedPipelineFactory(events);
        var config = new Config { ReplayEnabled = false, BitrateMbps = 20 };
        await using var runtime = new ReplayRuntime(
            config,
            factory,
            new RejectingConfigStore(events, rejectedBitrate: 50));
        await runtime.SetEnabledAsync(true);
        events.Clear();
        Config candidate = config.Clone();
        candidate.ReplayEnabled = true;
        candidate.BitrateMbps = 50;

        ReplayCommandResult result = await runtime.ApplyConfigurationAsync(candidate);

        Assert.False(result.Succeeded);
        Assert.Equal(ReplayRuntimeState.Running, result.Snapshot.State);
        Assert.Equal(
            [
                "dispose:1",
                "create:50",
                "start:2",
                "save:50:fail",
                "dispose:2",
                "save:20",
                "create:20",
                "start:3",
            ],
            events);
    }

    private sealed class RecordingPipelineFactory(
        IReplayPipeline pipeline,
        List<string> events) : IReplayPipelineFactory
    {
        public IReplayPipeline Create(Config configuration)
        {
            events.Add("create");
            return pipeline;
        }
    }

    private sealed class BlockingPipeline(List<string> events) : IReplayPipeline
    {
        internal TaskCompletionSource StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource AllowStart { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("start:pending");
            StartEntered.SetResult();
            await AllowStart.Task.WaitAsync(cancellationToken);
            events.Add("start:done");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<string> SaveAsync(CancellationToken cancellationToken) =>
            Task.FromResult("replay.mp4");
    }

    private sealed class BlockingSavePipeline(List<string> events) : IReplayPipeline
    {
        internal TaskCompletionSource SaveEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource AllowSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("start");
            return Task.CompletedTask;
        }

        public async Task<string> SaveAsync(CancellationToken cancellationToken)
        {
            events.Add("save:pending");
            SaveEntered.SetResult();
            await AllowSave.Task.WaitAsync(cancellationToken);
            events.Add("save:done");
            return "replay.mp4";
        }

        public ValueTask DisposeAsync()
        {
            events.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPipeline(List<string> events) : IReplayPipeline
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("start");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            events.Add("dispose");
            return ValueTask.CompletedTask;
        }

        public Task<string> SaveAsync(CancellationToken cancellationToken) =>
            Task.FromResult("replay.mp4");
    }

    private sealed class RecordingConfigStore(
        List<string> events,
        Func<Config, string>? describe = null) : IReplayConfigStore
    {
        public void Save(Config configuration) =>
            events.Add(
                $"save:{describe?.Invoke(configuration) ?? configuration.ReplayEnabled.ToString().ToLowerInvariant()}");
    }

    private sealed class RejectingConfigStore(
        List<string> events,
        int rejectedBitrate) : IReplayConfigStore
    {
        public void Save(Config configuration)
        {
            if (configuration.BitrateMbps == rejectedBitrate)
            {
                events.Add($"save:{configuration.BitrateMbps}:fail");
                throw new InvalidOperationException("configuration store unavailable");
            }
            events.Add($"save:{configuration.BitrateMbps}");
        }
    }

    private sealed class SequencedPipelineFactory(List<string> events)
        : IReplayPipelineFactory
    {
        private int _nextId;

        public IReplayPipeline Create(Config configuration)
        {
            int id = ++_nextId;
            events.Add($"create:{configuration.BitrateMbps}");
            return new SequencedPipeline(id, events);
        }
    }

    private sealed class SequencedPipeline(int id, List<string> events)
        : IReplayPipeline
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add($"start:{id}");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            events.Add($"dispose:{id}");
            return ValueTask.CompletedTask;
        }

        public Task<string> SaveAsync(CancellationToken cancellationToken) =>
            Task.FromResult("replay.mp4");
    }

    private sealed class FailingSecondPipelineFactory(List<string> events)
        : IReplayPipelineFactory
    {
        private int _nextId;

        public IReplayPipeline Create(Config configuration)
        {
            int id = ++_nextId;
            events.Add($"create:{configuration.BitrateMbps}");
            return new FailingPipeline(id, events, failOnStart: id == 2);
        }
    }

    private sealed class BlockingRecoveryPipelineFactory(List<string> events)
        : IReplayPipelineFactory
    {
        private int _nextId;

        internal TaskCompletionSource RecoveryStartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource AllowRecovery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReplayPipeline Create(Config configuration)
        {
            int id = ++_nextId;
            events.Add($"create:{id}");
            return new RecoveryPipeline(
                id,
                events,
                RecoveryStartEntered,
                AllowRecovery);
        }
    }

    private sealed class RecoveryPipeline(
        int id,
        List<string> events,
        TaskCompletionSource recoveryStartEntered,
        TaskCompletionSource allowRecovery) : IReplayPipeline
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add($"start:{id}");
            if (id == 2)
            {
                recoveryStartEntered.SetResult();
                await allowRecovery.Task.WaitAsync(cancellationToken);
            }
        }

        public Task<string> SaveAsync(CancellationToken cancellationToken) =>
            Task.FromResult("replay.mp4");

        public ValueTask DisposeAsync()
        {
            events.Add($"dispose:{id}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingPipeline(
        int id,
        List<string> events,
        bool failOnStart) : IReplayPipeline
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add($"start:{id}{(failOnStart ? ":fail" : "")}");
            if (failOnStart)
                throw new InvalidOperationException("pipeline rejected configuration");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            events.Add($"dispose:{id}");
            return ValueTask.CompletedTask;
        }

        public Task<string> SaveAsync(CancellationToken cancellationToken) =>
            Task.FromResult("replay.mp4");
    }
}
