namespace Captail.Tests;

using Xunit;

public sealed class ReplaySuspensionTests
{
    [Fact]
    public async Task StartRecording_WhenSuspendEnabled_MarksReplaySuspendedAndBlocksSave()
    {
        var events = new List<string>();
        var pipeline = new SuspendingTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = true,
            SuspendReplayDuringRecording = true,
        };
        await using var runtime = new ReplayRuntime(
            config,
            new SuspendingPipelineFactory(pipeline, events),
            new SuspendingConfigStore(events));

        await runtime.SetEnabledAsync(true);
        Assert.True(runtime.Snapshot.ReplayEnabled);
        Assert.False(runtime.Snapshot.IsRecording);
        Assert.False(runtime.Snapshot.IsReplaySuspended);

        await runtime.StartRecordingAsync();
        Assert.True(runtime.Snapshot.IsRecording);
        Assert.True(runtime.Snapshot.IsReplaySuspended);

        // Attempting to save replay during suspension must throw
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SaveAsync());
        Assert.Contains("suspended", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Stopping recording must clear suspension
        await runtime.StopRecordingAsync();
        Assert.False(runtime.Snapshot.IsRecording);
        Assert.False(runtime.Snapshot.IsReplaySuspended);

        // Now save works
        string replay = await runtime.SaveAsync();
        Assert.Equal("replay.mp4", replay);
    }

    [Fact]
    public async Task StartRecording_WhenSuspendDisabled_ReplayIsNotSuspended()
    {
        var events = new List<string>();
        var pipeline = new SuspendingTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = true,
            SuspendReplayDuringRecording = false,
        };
        await using var runtime = new ReplayRuntime(
            config,
            new SuspendingPipelineFactory(pipeline, events),
            new SuspendingConfigStore(events));

        await runtime.SetEnabledAsync(true);
        await runtime.StartRecordingAsync();

        Assert.True(runtime.Snapshot.IsRecording);
        Assert.False(runtime.Snapshot.IsReplaySuspended);

        // Save succeeds during recording
        string replay = await runtime.SaveAsync();
        Assert.Equal("replay.mp4", replay);

        await runtime.StopRecordingAsync();
    }

    [Fact]
    public async Task StartRecording_WhenReplayDisabled_IsNotMarkedSuspended()
    {
        var events = new List<string>();
        var pipeline = new SuspendingTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = false,
            SuspendReplayDuringRecording = true,
        };
        await using var runtime = new ReplayRuntime(
            config,
            new SuspendingPipelineFactory(pipeline, events),
            new SuspendingConfigStore(events));

        await runtime.StartRecordingAsync();

        Assert.True(runtime.Snapshot.IsRecording);
        Assert.False(runtime.Snapshot.IsReplaySuspended);

        await runtime.StopRecordingAsync();
    }

    [Fact]
    public void IndicatorState_SuspendedValueExistsAndConfigDefaultsToTrue()
    {
        var config = new Config();
        Assert.True(config.SuspendReplayDuringRecording);
        Assert.False(config.KeepRecordingPipelineWarm);

        Assert.True(Enum.IsDefined(typeof(ReplayIndicatorState), ReplayIndicatorState.Suspended));
    }

    private sealed class SuspendingTrackingPipeline(List<string> events) : IReplayPipeline
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("start");
            return Task.CompletedTask;
        }

        public Task<string> SaveAsync(CancellationToken cancellationToken)
        {
            events.Add("save");
            return Task.FromResult("replay.mp4");
        }

        public Task<string> StartRecordingAsync(CancellationToken cancellationToken)
        {
            events.Add("start-recording");
            return Task.FromResult("recording.mp4");
        }

        public Task<string> StopRecordingAsync(CancellationToken cancellationToken)
        {
            events.Add("stop-recording");
            return Task.FromResult("recording.mp4");
        }

        public Task<bool> PauseRecordingAsync(bool pause, CancellationToken cancellationToken)
        {
            events.Add($"pause-recording:{pause}");
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            events.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SuspendingPipelineFactory(
        IReplayPipeline pipeline,
        List<string> events) : IReplayPipelineFactory
    {
        public IReplayPipeline Create(Config configuration)
        {
            events.Add("create");
            return pipeline;
        }
    }

    private sealed class SuspendingConfigStore(List<string> events) : IReplayConfigStore
    {
        public void Save(Config configuration)
        {
            events.Add($"save:{configuration.ReplayEnabled}");
        }
    }
}
