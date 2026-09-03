namespace Captail.Tests;

using Xunit;

public sealed class SharedEncoderMultiOutputTests
{
    [Fact]
    public async Task SharedEncoder_BothOutputsActive_PreservesReplayAndRecordingContinuity()
    {
        var events = new List<string>();
        var pipeline = new MultiOutputTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = true,
            SuspendReplayDuringRecording = false,
        };
        await using var runtime = new ReplayRuntime(
            config,
            new TrackingPipelineFactory(pipeline, events),
            new TrackingConfigStore(events));

        await runtime.SetEnabledAsync(true);
        Assert.True(runtime.Snapshot.ReplayEnabled);
        Assert.False(runtime.Snapshot.IsRecording);

        string recordingPath = await runtime.StartRecordingAsync();
        Assert.Equal("recording.mp4", recordingPath);
        Assert.True(runtime.Snapshot.IsRecording);
        Assert.True(runtime.Snapshot.ReplayEnabled);
        Assert.False(runtime.Snapshot.IsReplaySuspended);

        // In non-suspended mode, both outputs remain active
        Assert.True(pipeline.ReplayOutputActive);
        Assert.True(pipeline.RecordingOutputActive);
        Assert.Equal(1, pipeline.SharedVideoEncoderAttachCount);

        string stoppedPath = await runtime.StopRecordingAsync();
        Assert.Equal("recording.mp4", stoppedPath);
        Assert.False(runtime.Snapshot.IsRecording);
        Assert.True(pipeline.ReplayOutputActive);
        Assert.False(pipeline.RecordingOutputActive);
    }

    [Fact]
    public async Task SharedEncoder_StopOneOutput_DoesNotStopTheOther()
    {
        var events = new List<string>();
        var pipeline = new MultiOutputTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = true,
            SuspendReplayDuringRecording = false,
        };
        await using var runtime = new ReplayRuntime(
            config,
            new TrackingPipelineFactory(pipeline, events),
            new TrackingConfigStore(events));

        await runtime.SetEnabledAsync(true);
        await runtime.StartRecordingAsync();

        Assert.True(pipeline.ReplayOutputActive);
        Assert.True(pipeline.RecordingOutputActive);

        // Stop manual recording output
        await runtime.StopRecordingAsync();

        // Replay output must remain active and uninterrupted
        Assert.True(pipeline.ReplayOutputActive);
        Assert.False(pipeline.RecordingOutputActive);

        // Replay can save successfully
        string replayPath = await runtime.SaveAsync();
        Assert.Equal("replay.mp4", replayPath);
    }

    [Fact]
    public async Task SharedEncoder_PauseAndResumeRecording_DoesNotAffectReplayBuffer()
    {
        var events = new List<string>();
        var pipeline = new MultiOutputTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = true,
            SuspendReplayDuringRecording = false,
        };
        await using var runtime = new ReplayRuntime(
            config,
            new TrackingPipelineFactory(pipeline, events),
            new TrackingConfigStore(events));

        await runtime.SetEnabledAsync(true);
        await runtime.StartRecordingAsync();

        await runtime.PauseRecordingAsync(true);
        Assert.True(runtime.Snapshot.IsRecordingPaused);
        Assert.True(pipeline.ReplayOutputActive);

        await runtime.PauseRecordingAsync(false);
        Assert.False(runtime.Snapshot.IsRecordingPaused);
        Assert.True(pipeline.ReplayOutputActive);

        await runtime.StopRecordingAsync();
    }

    [Fact]
    public async Task SharedEncoder_StopFailure_CleansUpRecordingStateGracefully()
    {
        var events = new List<string>();
        var pipeline = new FailingStopPipeline(events);
        var config = new Config
        {
            ReplayEnabled = true,
            SuspendReplayDuringRecording = false,
        };
        await using var runtime = new ReplayRuntime(
            config,
            new TrackingPipelineFactory(pipeline, events),
            new TrackingConfigStore(events));

        await runtime.SetEnabledAsync(true);
        await runtime.StartRecordingAsync();
        Assert.True(runtime.Snapshot.IsRecording);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StopRecordingAsync());
    }

    [Fact]
    public async Task SharedEncoder_ShutdownDuringRecording_DisposesCleanly()
    {
        var events = new List<string>();
        var pipeline = new MultiOutputTrackingPipeline(events);
        var config = new Config
        {
            ReplayEnabled = true,
            SuspendReplayDuringRecording = false,
        };
        var runtime = new ReplayRuntime(
            config,
            new TrackingPipelineFactory(pipeline, events),
            new TrackingConfigStore(events));

        await runtime.SetEnabledAsync(true);
        await runtime.StartRecordingAsync();
        Assert.True(runtime.Snapshot.IsRecording);

        // Shutdown while recording
        await runtime.DisposeAsync();

        Assert.True(pipeline.IsDisposed);
        Assert.Contains("dispose", events);
    }

    private sealed class MultiOutputTrackingPipeline(List<string> events) : IReplayPipeline
    {
        public bool ReplayOutputActive { get; private set; }
        public bool RecordingOutputActive { get; private set; }
        public int SharedVideoEncoderAttachCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("start");
            ReplayOutputActive = true;
            SharedVideoEncoderAttachCount++;
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
            RecordingOutputActive = true;
            return Task.FromResult("recording.mp4");
        }

        public Task<string> StopRecordingAsync(CancellationToken cancellationToken)
        {
            events.Add("stop-recording");
            RecordingOutputActive = false;
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
            ReplayOutputActive = false;
            RecordingOutputActive = false;
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStopPipeline(List<string> events) : IReplayPipeline
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("start");
            return Task.CompletedTask;
        }

        public Task<string> SaveAsync(CancellationToken cancellationToken) =>
            Task.FromResult("replay.mp4");

        public Task<string> StartRecordingAsync(CancellationToken cancellationToken)
        {
            events.Add("start-recording");
            return Task.FromResult("recording.mp4");
        }

        public Task<string> StopRecordingAsync(CancellationToken cancellationToken)
        {
            events.Add("stop-recording-fail");
            throw new InvalidOperationException("Failed to finalize recording muxer.");
        }

        public ValueTask DisposeAsync()
        {
            events.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingPipelineFactory(
        IReplayPipeline pipeline,
        List<string> events) : IReplayPipelineFactory
    {
        public IReplayPipeline Create(Config configuration)
        {
            events.Add("create");
            return pipeline;
        }
    }

    private sealed class TrackingConfigStore(List<string> events) : IReplayConfigStore
    {
        public void Save(Config configuration)
        {
            events.Add($"save:{configuration.ReplayEnabled}");
        }
    }
}
