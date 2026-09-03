using System.Diagnostics;
using System.Text.Json;
using Captail;
using Xunit;

namespace Captail.Tests;

public sealed class RealObsIntegrationTests
{
    [Fact]
    public void RealObs_ProbeCapabilities_SucceedsOnCurrentHardware()
    {
        var config = new Config();
        var capabilities = ObsReplayEngine.ProbeCapabilities(config);
        Assert.NotNull(capabilities);
        Assert.Null(capabilities.ProbeError);
        Assert.True(capabilities.Supports("h264"), $"H.264 not supported. Adapter: {capabilities.AdapterName}");
    }

    [Fact]
    public async Task RealObs_SharedEncoder_ReplayAndRecordingLifecycle_WithFfprobeValidation()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "Captail_Obs_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var config = new Config
            {
                CaptureSource = "desktop",
                OutputDirectory = testDir,
                BufferSeconds = 15,
                FrameRate = 60,
                BitrateMbps = 10,
                NvencMode = NvencModes.LowOverhead,
                Codec = "h264",
                ReplayEnabled = true,
                SuspendReplayDuringRecording = true,
            };

            using var scheduler = new SingleThreadTaskScheduler("ObsIntegrationTestThread");

            Task<T> RunOnObs<T>(Func<T> action) =>
                Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler);

            Task RunOnObsAction(Action action) =>
                Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler);

            ObsReplayEngine? engine = null;
            string recordPath = "";
            string replayPath = "";

            try
            {
                engine = await RunOnObs(() =>
                {
                    var eng = new ObsReplayEngine(config);
                    eng.Start();
                    return eng;
                });

                Assert.True(engine.IsActive, "Engine replay output should be active after start");

                // Wait for initial replay buffer frames
                await Task.Delay(1500);
                Assert.True(engine.EncodedFrameCount > 0, $"EncodedFrameCount should be > 0, was {engine.EncodedFrameCount}");

                // 1. Start manual recording while replay is active (should suspend replay)
                recordPath = await RunOnObs(() => engine.StartRecordingAsync()).Unwrap();
                Assert.True(engine.IsRecording, "Recording output should be active");
                Assert.True(engine.IsReplaySuspendedForManualRecording, "Replay should be marked suspended");
                Assert.Equal(0, engine.AvailableReplaySeconds);

                // Saving replay must throw when suspended
                Assert.Throws<InvalidOperationException>(() => engine.BeginSaveReplay());

                // 2. Record for 2 seconds
                await Task.Delay(2000);
                Assert.True(engine.RecordingOutputBytes > 0, "RecordingOutputBytes should be > 0");

                // 3. Stop recording (should resume replay)
                recordPath = await engine.StopRecordingAsync();
                Assert.False(engine.IsRecording, "Recording output should be stopped");
                Assert.False(engine.IsReplaySuspendedForManualRecording, "Suspended flag should be cleared");
                Assert.True(File.Exists(recordPath), $"Recording file should exist at {recordPath}");

                // 4. Wait for replay buffer to accumulate new window
                await Task.Delay(2000);
                Assert.True(engine.IsActive, "Replay output should be active after recording stopped");

                // 5. Save replay
                ReplaySaveOperation saveOp = await RunOnObs(() => engine.BeginSaveReplay());
                replayPath = await saveOp.Completion;
                Assert.True(File.Exists(replayPath), $"Replay file should exist at {replayPath}");
            }
            finally
            {
                if (engine is not null)
                {
                    await RunOnObsAction(engine.Dispose);
                }
            }

            // 6. ffprobe Media Validation on both files
            ValidateMediaFile(recordPath);
            ValidateMediaFile(replayPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(testDir))
                    Directory.Delete(testDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public async Task RealObs_SharedEncoder_SimultaneousReplayAndRecording_BothOutputsActive_WithFfprobeValidation()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "Captail_Obs_Simul_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var config = new Config
            {
                CaptureSource = "desktop",
                OutputDirectory = testDir,
                BufferSeconds = 15,
                FrameRate = 60,
                BitrateMbps = 10,
                NvencMode = NvencModes.LowOverhead,
                Codec = "h264",
                ReplayEnabled = true,
                SuspendReplayDuringRecording = false, // Simultaneous mode
            };

            using var scheduler = new SingleThreadTaskScheduler("ObsSimulTestThread");

            Task<T> RunOnObs<T>(Func<T> action) =>
                Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler);

            Task RunOnObsAction(Action action) =>
                Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler);

            ObsReplayEngine? engine = null;
            string recordPath = "";
            string replayPath = "";

            try
            {
                engine = await RunOnObs(() =>
                {
                    var eng = new ObsReplayEngine(config);
                    eng.Start();
                    return eng;
                });

                Assert.True(engine.IsActive, "Engine replay output should be active after start");

                // Wait for initial replay buffer frames
                await Task.Delay(1500);
                Assert.True(engine.EncodedFrameCount > 0);

                // Start manual recording while replay is active (both outputs active)
                recordPath = await RunOnObs(() => engine.StartRecordingAsync()).Unwrap();
                Assert.True(engine.IsRecording, "Recording output should be active");
                Assert.True(engine.IsActive, "Replay output should STILL be active simultaneously");
                Assert.False(engine.IsReplaySuspendedForManualRecording);

                // Record for 2 seconds simultaneously
                await Task.Delay(2000);
                Assert.True(engine.RecordingOutputBytes > 0);
                Assert.True(engine.IsActive);

                // Stop recording
                recordPath = await engine.StopRecordingAsync();
                Assert.False(engine.IsRecording);
                Assert.True(engine.IsActive, "Replay output should remain active after recording stops");

                // Save replay
                ReplaySaveOperation saveOp = await RunOnObs(() => engine.BeginSaveReplay());
                replayPath = await saveOp.Completion;
                Assert.True(File.Exists(replayPath));
            }
            finally
            {
                if (engine is not null)
                {
                    await RunOnObsAction(engine.Dispose);
                }
            }

            // ffprobe Media Validation on both files
            ValidateMediaFile(recordPath);
            ValidateMediaFile(replayPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(testDir))
                    Directory.Delete(testDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private static void ValidateMediaFile(string filePath)
    {
        Assert.True(File.Exists(filePath), $"File {filePath} does not exist.");
        var fileInfo = new FileInfo(filePath);
        Assert.True(fileInfo.Length > 10_000, $"File size too small ({fileInfo.Length} bytes): {filePath}");

        // Run ffprobe to check format and streams
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -show_entries format=format_name,duration:stream=codec_type,codec_name -of json \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);

        Assert.Equal(0, process.ExitCode);
        Assert.Empty(stderr);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        // Check format
        Assert.True(root.TryGetProperty("format", out var formatElement));
        string formatName = formatElement.GetProperty("format_name").GetString()!;
        Assert.Contains("mp4", formatName, StringComparison.OrdinalIgnoreCase);

        double duration = double.Parse(formatElement.GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(duration > 0.5, $"Duration should be > 0.5s, was {duration}");

        // Check streams
        Assert.True(root.TryGetProperty("streams", out var streamsElement));
        bool hasVideo = false;
        foreach (var stream in streamsElement.EnumerateArray())
        {
            if (stream.GetProperty("codec_type").GetString() == "video")
            {
                hasVideo = true;
                string codecName = stream.GetProperty("codec_name").GetString()!;
                Assert.Equal("h264", codecName);
            }
        }
        Assert.True(hasVideo, "File must contain a video stream");

        // Check first packet is a keyframe
        var keyframePsi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -select_streams v:0 -show_entries packet=flags -read_intervals \"%+#1\" -of json \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var kfProc = Process.Start(keyframePsi)!;
        string kfStdout = kfProc.StandardOutput.ReadToEnd();
        kfProc.WaitForExit(5000);

        using var kfDoc = JsonDocument.Parse(kfStdout);
        var packets = kfDoc.RootElement.GetProperty("packets");
        Assert.True(packets.GetArrayLength() > 0, "No video packets found");
        string flags = packets[0].GetProperty("flags").GetString()!;
        Assert.StartsWith("K", flags); // Keyframe flag
    }
}
