namespace Captail.Tests;

using Xunit;

public sealed class WarmPipelineTests
{
    [Fact]
    public async Task WarmPipeline_ReplayDisabled_KeepsPipelineWarmAcrossRecordings()
    {
        var events = new List<string>();
        var pipeline = new WarmTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = false,
            KeepRecordingPipelineWarm = true,
        };
        await using var runtime = new ReplayRuntime(
            config,
            new WarmPipelineFactory(pipeline, events),
            new WarmConfigStore(events));

        // Initial apply with KeepRecordingPipelineWarm=true warms the pipeline
        ReplayCommandResult initResult = await runtime.ApplyConfigurationAsync(config);
        Assert.True(initResult.Succeeded);
        Assert.Equal(ReplayRuntimeState.Disabled, runtime.Snapshot.State);
        Assert.False(runtime.Snapshot.ReplayEnabled);
        Assert.Contains("create", events);
        Assert.Contains("start", events);

        events.Clear();

        // Start recording uses existing warm pipeline without creating a new one
        string recordPath = await runtime.StartRecordingAsync();
        Assert.Equal("recording.mp4", recordPath);
        Assert.True(runtime.Snapshot.IsRecording);
        Assert.Contains("start-recording", events);
        Assert.DoesNotContain("create", events); // Proves pipeline was warm and reused!

        events.Clear();

        // Stop recording keeps pipeline warm rather than disposing it
        string stopPath = await runtime.StopRecordingAsync();
        Assert.Equal("recording.mp4", stopPath);
        Assert.False(runtime.Snapshot.IsRecording);
        Assert.Equal(ReplayRuntimeState.Disabled, runtime.Snapshot.State);
        Assert.Contains("stop-recording", events);
        Assert.DoesNotContain("dispose", events); // Proves pipeline is NOT disposed!

        events.Clear();

        // Second recording also starts immediately on the same warm pipeline
        await runtime.StartRecordingAsync();
        Assert.True(runtime.Snapshot.IsRecording);
        Assert.Contains("start-recording", events);
        Assert.DoesNotContain("create", events);

        await runtime.StopRecordingAsync();
    }

    [Fact]
    public async Task ColdPipeline_ReplayDisabled_CreatesAndDisposesPipelinePerRecording()
    {
        var events = new List<string>();
        var pipeline = new WarmTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = false,
            KeepRecordingPipelineWarm = false, // Cold mode
        };
        await using var runtime = new ReplayRuntime(
            config,
            new WarmPipelineFactory(pipeline, events),
            new WarmConfigStore(events));

        // In cold mode, pipeline is created on recording start
        await runtime.StartRecordingAsync();
        Assert.Contains("create", events);
        Assert.Contains("start", events);
        Assert.Contains("start-recording", events);

        // In cold mode, pipeline is disposed when recording stops
        await runtime.StopRecordingAsync();
        Assert.Contains("stop-recording", events);
        Assert.Contains("dispose", events);
    }

    [Fact]
    public async Task EnablingReplay_FromWarmState_TransitionsSmoothly()
    {
        var events = new List<string>();
        var pipeline = new WarmTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = false,
            KeepRecordingPipelineWarm = true,
        };
        await using var runtime = new ReplayRuntime(
            config,
            new WarmPipelineFactory(pipeline, events),
            new WarmConfigStore(events));

        await runtime.ApplyConfigurationAsync(config);
        events.Clear();

        ReplayCommandResult result = await runtime.SetEnabledAsync(true);
        Assert.True(result.Succeeded);
        Assert.Equal(ReplayRuntimeState.Running, runtime.Snapshot.State);
        Assert.True(runtime.Snapshot.ReplayEnabled);
        Assert.Contains("dispose", events); // Disposed warm inactive pipeline
        Assert.Contains("create", events);  // Created active replay pipeline
    }

    private sealed class WarmTrackingPipeline(List<string> events) : IReplayPipeline
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

    private sealed class WarmPipelineFactory(
        IReplayPipeline pipeline,
        List<string> events) : IReplayPipelineFactory
    {
        public IReplayPipeline Create(Config configuration)
        {
            events.Add("create");
            return pipeline;
        }
    }

    private sealed class WarmConfigStore(List<string> events) : IReplayConfigStore
    {
        public void Save(Config configuration)
        {
            events.Add($"save:{configuration.ReplayEnabled}");
        }
    }
}
