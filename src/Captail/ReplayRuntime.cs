namespace Captail;

internal enum ReplayRuntimeState
{
    Disabled,
    Starting,
    Running,
    Recovering,
    Stopping,
    Faulted,
}

internal sealed record ReplayRuntimeSnapshot(
    ReplayRuntimeState State,
    bool ReplayEnabled,
    bool IsRecording = false,
    bool IsRecordingPaused = false,
    DateTime? RecordingStartedUtc = null,
    string? Error = null,
    bool IsReplaySuspended = false);

internal sealed record ReplayCommandResult(
    bool Succeeded,
    ReplayRuntimeSnapshot Snapshot)
{
    internal static ReplayCommandResult Success(ReplayRuntimeSnapshot snapshot) =>
        new(true, snapshot);

    internal static ReplayCommandResult Failure(ReplayRuntimeSnapshot snapshot) =>
        new(false, snapshot);
}

internal interface IReplayPipeline : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    Task<string> SaveAsync(CancellationToken cancellationToken);

    Task<string> StartRecordingAsync(CancellationToken cancellationToken) =>
        Task.FromResult("recording.mp4");

    Task<string> StopRecordingAsync(CancellationToken cancellationToken) =>
        Task.FromResult("recording.mp4");

    Task<bool> PauseRecordingAsync(bool pause, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

internal interface IReplayPipelineFactory
{
    IReplayPipeline Create(Config configuration);
}

internal interface IReplayConfigStore
{
    void Save(Config configuration);
}

internal sealed class ReplayRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly IReplayPipelineFactory _pipelineFactory;
    private readonly IReplayConfigStore _configStore;
    private readonly Config _configuration;
    private readonly object _recoverySync = new();
    private readonly TaskCompletionSource _shutdownCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReplayPipeline? _pipeline;
    private bool _disposed;
    private int _shutdownRequested;
    private Task<ReplayCommandResult>? _activeRecovery;

    internal ReplayRuntimeSnapshot Snapshot { get; private set; }

    internal event EventHandler<ReplayRuntimeSnapshot>? SnapshotChanged;

    internal ReplayRuntime(
        Config configuration,
        IReplayPipelineFactory pipelineFactory,
        IReplayConfigStore configStore)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(pipelineFactory);
        ArgumentNullException.ThrowIfNull(configStore);

        _configuration = configuration.Clone();
        _pipelineFactory = pipelineFactory;
        _configStore = configStore;
        Snapshot = new ReplayRuntimeSnapshot(
            ReplayRuntimeState.Disabled,
            _configuration.ReplayEnabled);
    }

    internal async Task<ReplayCommandResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            return enabled
                ? await EnableCoreAsync(cancellationToken)
                : await DisableCoreAsync();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    internal async Task<ReplayCommandResult> ApplyConfigurationAsync(
        Config configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfUnavailable();
        await _commandGate.WaitAsync(cancellationToken);
        Config previousConfiguration = _configuration.Clone();
        bool wasRunning = _pipeline is not null;
        IReplayPipeline? candidatePipeline = null;
        bool commandStarted = false;
        bool pipelineReplaced = false;
        try
        {
            ThrowIfUnavailable();
            commandStarted = true;
            Config candidateConfiguration = configuration.Clone();
            if (_pipeline is null || !_configuration.PipelineEquals(candidateConfiguration))
            {
                if (_pipeline is not null)
                {
                    Publish(ReplayRuntimeState.Stopping, replayEnabled: true);
                    IReplayPipeline previousPipeline = _pipeline;
                    _pipeline = null;
                    await previousPipeline.DisposeAsync();
                }

                if (candidateConfiguration.ReplayEnabled)
                {
                    Publish(ReplayRuntimeState.Starting, replayEnabled: true);
                    candidatePipeline = _pipelineFactory.Create(candidateConfiguration);
                    await candidatePipeline.StartAsync(cancellationToken);
                    _pipeline = candidatePipeline;
                    candidatePipeline = null;
                    pipelineReplaced = true;
                }
            }

            _configuration.CopyFrom(candidateConfiguration);
            _configStore.Save(_configuration.Clone());
            Publish(
                _pipeline is null
                    ? ReplayRuntimeState.Disabled
                    : ReplayRuntimeState.Running,
                _configuration.ReplayEnabled);
            return ReplayCommandResult.Success(Snapshot);
        }
        catch (Exception exception) when (commandStarted)
        {
            if (candidatePipeline is not null)
                await candidatePipeline.DisposeAsync();

            if (pipelineReplaced && _pipeline is not null)
            {
                IReplayPipeline rejectedPipeline = _pipeline;
                _pipeline = null;
                await rejectedPipeline.DisposeAsync();
            }

            _configuration.CopyFrom(previousConfiguration);
            _configStore.Save(_configuration.Clone());
            if (wasRunning)
            {
                try
                {
                    IReplayPipeline rollbackPipeline =
                        _pipelineFactory.Create(previousConfiguration.Clone());
                    await rollbackPipeline.StartAsync(CancellationToken.None);
                    _pipeline = rollbackPipeline;
                    Publish(
                        ReplayRuntimeState.Running,
                        replayEnabled: true,
                        exception.Message);
                    return ReplayCommandResult.Failure(Snapshot);
                }
                catch (Exception rollbackException)
                {
                    Publish(
                        ReplayRuntimeState.Faulted,
                        replayEnabled: true,
                        $"{exception.Message} Rollback failed: {rollbackException.Message}");
                    return ReplayCommandResult.Failure(Snapshot);
                }
            }

            Publish(ReplayRuntimeState.Faulted, replayEnabled: false, exception.Message);
            return ReplayCommandResult.Failure(Snapshot);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    internal async Task<string> SaveAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            IReplayPipeline pipeline = _pipeline ??
                throw new InvalidOperationException(
                    "Instant Replay must be running before a replay can be saved.");
            return await pipeline.SaveAsync(cancellationToken);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    internal async Task<string> StartRecordingAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            if (Snapshot.IsRecording)
                throw new InvalidOperationException("Recording is already in progress.");

            IReplayPipeline? pipeline = _pipeline;
            bool pipelineCreatedForRecording = false;
            if (pipeline is null)
            {
                Publish(ReplayRuntimeState.Starting, _configuration.ReplayEnabled);
                Config recordingConfig = _configuration.Clone();
                pipeline = _pipelineFactory.Create(recordingConfig);
                await pipeline.StartAsync(cancellationToken);
                _pipeline = pipeline;
                pipelineCreatedForRecording = true;
            }

            try
            {
                string path = await pipeline.StartRecordingAsync(cancellationToken);
                Publish(
                    ReplayRuntimeState.Running,
                    _configuration.ReplayEnabled,
                    isRecording: true,
                    isRecordingPaused: false,
                    recordingStartedUtc: DateTime.UtcNow);
                return path;
            }
            catch
            {
                if (pipelineCreatedForRecording && _pipeline is not null)
                {
                    IReplayPipeline failed = _pipeline;
                    _pipeline = null;
                    await failed.DisposeAsync();
                    Publish(ReplayRuntimeState.Disabled, _configuration.ReplayEnabled);
                }
                throw;
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    internal async Task<string> StopRecordingAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            if (!Snapshot.IsRecording || _pipeline is null)
                throw new InvalidOperationException("Recording is not running.");

            string savedPath = await _pipeline.StopRecordingAsync(cancellationToken);

            if (!_configuration.ReplayEnabled)
            {
                Publish(
                    ReplayRuntimeState.Stopping,
                    _configuration.ReplayEnabled,
                    isRecording: false,
                    isRecordingPaused: false,
                    recordingStartedUtc: null);
                IReplayPipeline pipelineToDispose = _pipeline;
                _pipeline = null;
                await pipelineToDispose.DisposeAsync();
                Publish(
                    ReplayRuntimeState.Disabled,
                    _configuration.ReplayEnabled,
                    isRecording: false,
                    isRecordingPaused: false,
                    recordingStartedUtc: null);
            }
            else
            {
                Publish(
                    ReplayRuntimeState.Running,
                    _configuration.ReplayEnabled,
                    isRecording: false,
                    isRecordingPaused: false,
                    recordingStartedUtc: null);
            }

            return savedPath;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    internal async Task<bool> PauseRecordingAsync(
        bool pause,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            if (!Snapshot.IsRecording || _pipeline is null)
                return false;

            bool paused = await _pipeline.PauseRecordingAsync(pause, cancellationToken);
            Publish(
                Snapshot.State,
                Snapshot.ReplayEnabled,
                isRecording: Snapshot.IsRecording,
                isRecordingPaused: pause && paused,
                recordingStartedUtc: Snapshot.RecordingStartedUtc);
            return paused;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    internal Task<ReplayCommandResult> RecoverAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ThrowIfUnavailable();
        lock (_recoverySync)
        {
            if (_activeRecovery is { IsCompleted: false })
                return _activeRecovery;

            var completion = new TaskCompletionSource<ReplayCommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRecovery = completion.Task;
            _ = RecoverAndCompleteAsync(completion, reason, cancellationToken);
            return _activeRecovery;
        }
    }

    private async Task RecoverAndCompleteAsync(
        TaskCompletionSource<ReplayCommandResult> completion,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            completion.SetResult(await RecoverCoreAsync(reason, cancellationToken));
        }
        catch (OperationCanceledException exception)
        {
            completion.SetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
    }

    private async Task<ReplayCommandResult> RecoverCoreAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        await _commandGate.WaitAsync(cancellationToken);
        IReplayPipeline? candidate = null;
        try
        {
            ThrowIfUnavailable();
            if (_pipeline is null)
                return ReplayCommandResult.Failure(Snapshot);

            Publish(ReplayRuntimeState.Recovering, replayEnabled: true, reason);
            IReplayPipeline failedPipeline = _pipeline;
            _pipeline = null;
            await failedPipeline.DisposeAsync();

            candidate = _pipelineFactory.Create(_configuration.Clone());
            await candidate.StartAsync(cancellationToken);
            _pipeline = candidate;
            candidate = null;
            Publish(ReplayRuntimeState.Running, replayEnabled: true);
            return ReplayCommandResult.Success(Snapshot);
        }
        catch (Exception exception)
        {
            if (candidate is not null)
                await candidate.DisposeAsync();
            Publish(ReplayRuntimeState.Faulted, replayEnabled: true, exception.Message);
            return ReplayCommandResult.Failure(Snapshot);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    internal async Task ShutdownAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
        {
            await _shutdownCompleted.Task;
            return;
        }

        try
        {
            await _commandGate.WaitAsync();
            try
            {
                if (_pipeline is not null)
                {
                    Publish(ReplayRuntimeState.Stopping, replayEnabled: true);
                    IReplayPipeline pipeline = _pipeline;
                    _pipeline = null;
                    await pipeline.DisposeAsync();
                }
                Publish(ReplayRuntimeState.Disabled, replayEnabled: false);
            }
            finally
            {
                _commandGate.Release();
            }
            _shutdownCompleted.SetResult();
        }
        catch (Exception exception)
        {
            _shutdownCompleted.SetException(exception);
            throw;
        }
    }

    private async Task<ReplayCommandResult> EnableCoreAsync(
        CancellationToken cancellationToken)
    {
        if (_pipeline is not null)
            return ReplayCommandResult.Success(Snapshot);

        Publish(ReplayRuntimeState.Starting, replayEnabled: false);
        IReplayPipeline? candidate = null;
        try
        {
            Config candidateConfiguration = _configuration.Clone();
            candidateConfiguration.ReplayEnabled = true;
            candidate = _pipelineFactory.Create(candidateConfiguration);
            await candidate.StartAsync(cancellationToken);
            _pipeline = candidate;
            _configuration.CopyFrom(candidateConfiguration);
            _configStore.Save(_configuration.Clone());
            Publish(ReplayRuntimeState.Running, replayEnabled: true);
            return ReplayCommandResult.Success(Snapshot);
        }
        catch (Exception exception)
        {
            if (candidate is not null)
                await candidate.DisposeAsync();
            Publish(
                ReplayRuntimeState.Faulted,
                replayEnabled: false,
                exception.Message);
            return ReplayCommandResult.Failure(Snapshot);
        }
    }

    private async Task<ReplayCommandResult> DisableCoreAsync()
    {
        if (_pipeline is null)
        {
            _configuration.ReplayEnabled = false;
            Publish(ReplayRuntimeState.Disabled, replayEnabled: false);
            return ReplayCommandResult.Success(Snapshot);
        }

        if (Snapshot.IsRecording)
        {
            _configuration.ReplayEnabled = false;
            _configStore.Save(_configuration.Clone());
            Publish(
                ReplayRuntimeState.Running,
                replayEnabled: false,
                isRecording: Snapshot.IsRecording,
                isRecordingPaused: Snapshot.IsRecordingPaused,
                recordingStartedUtc: Snapshot.RecordingStartedUtc);
            return ReplayCommandResult.Success(Snapshot);
        }

        Publish(ReplayRuntimeState.Stopping, replayEnabled: true);
        IReplayPipeline pipeline = _pipeline;
        _pipeline = null;
        await pipeline.DisposeAsync();
        _configuration.ReplayEnabled = false;
        _configStore.Save(_configuration.Clone());
        Publish(ReplayRuntimeState.Disabled, replayEnabled: false);
        return ReplayCommandResult.Success(Snapshot);
    }

    private void Publish(
        ReplayRuntimeState state,
        bool replayEnabled,
        string? error = null)
    {
        bool isRecording = state != ReplayRuntimeState.Disabled && (Snapshot?.IsRecording ?? false);
        bool isPaused = state != ReplayRuntimeState.Disabled && (Snapshot?.IsRecordingPaused ?? false);
        DateTime? startedUtc = state != ReplayRuntimeState.Disabled ? Snapshot?.RecordingStartedUtc : null;
        Publish(
            state,
            replayEnabled,
            isRecording,
            isPaused,
            startedUtc,
            error);
    }

    private void Publish(
        ReplayRuntimeState state,
        bool replayEnabled,
        bool isRecording,
        bool isRecordingPaused,
        DateTime? recordingStartedUtc,
        string? error = null)
    {
        Snapshot = new ReplayRuntimeSnapshot(
            state,
            replayEnabled,
            isRecording,
            isRecordingPaused,
            recordingStartedUtc,
            error);
        SnapshotChanged?.Invoke(this, Snapshot);
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(
            _disposed || Volatile.Read(ref _shutdownRequested) != 0,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await ShutdownAsync();
        _disposed = true;
        _commandGate.Dispose();
    }
}
