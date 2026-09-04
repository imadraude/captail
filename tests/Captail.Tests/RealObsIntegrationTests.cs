using System.Diagnostics;
using System.Text.Json;
using Captail;
using Xunit;

namespace Captail.Tests;

public sealed class RealObsIntegrationTests
{


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
            string recordPath1 = "";
            string replayPath1 = "";
            string recordPath2 = "";
            string replayPath2 = "";

            try
            {
                try
                {
                    engine = await RunOnObs(() =>
                    {
                        var eng = new ObsReplayEngine(config);
                        eng.Start();
                        return eng;
                    });
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains(Localization.Text("L.Engine.NoEncoder")) ||
                    ex.Message.Contains("L.Engine.NoEncoder") ||
                    ex.Message.Contains("H.264") ||
                    ex.Message.Contains("encoder"))
                {
                    // Headless CI runners (e.g. GitHub Actions windows-2022) do not have a dedicated GPU encoder.
                    return;
                }

                Assert.NotNull(engine.Capabilities);
                Assert.True(engine.Capabilities.Supports("h264"), $"H.264 not supported. Adapter: {engine.Capabilities.AdapterName}");
                Assert.True(engine.IsActive, "Engine replay output should be active after start");

                // Wait for initial replay buffer frames
                await Task.Delay(1500);
                Assert.True(engine.EncodedFrameCount > 0, $"EncodedFrameCount should be > 0, was {engine.EncodedFrameCount}");

                // --- Phase 1: Replay suspended during manual recording ---

                // 1. Start manual recording while replay is active (should suspend replay)
                recordPath1 = await RunOnObs(() => engine.StartRecordingAsync()).Unwrap();
                Assert.True(engine.IsRecording, "Recording output should be active");
                Assert.True(engine.IsReplaySuspendedForManualRecording, "Replay should be marked suspended");
                Assert.Equal(0, engine.AvailableReplaySeconds);

                // Saving replay must throw when suspended
                Assert.Throws<InvalidOperationException>(() => engine.BeginSaveReplay());

                // 2. Record for 2 seconds
                await Task.Delay(2000);
                Assert.True(engine.RecordingOutputBytes > 0, "RecordingOutputBytes should be > 0");

                // 3. Stop recording (should resume replay)
                recordPath1 = await engine.StopRecordingAsync();
                Assert.False(engine.IsRecording, "Recording output should be stopped");
                Assert.False(engine.IsReplaySuspendedForManualRecording, "Suspended flag should be cleared");
                Assert.True(File.Exists(recordPath1), $"Recording file should exist at {recordPath1}");

                // 4. Wait for replay buffer to accumulate new window
                await Task.Delay(2000);
                Assert.True(engine.IsActive, "Replay output should be active after recording stopped");

                // 5. Save replay
                ReplaySaveOperation saveOp1 = await RunOnObs(() => engine.BeginSaveReplay());
                replayPath1 = await saveOp1.Completion;
                Assert.True(File.Exists(replayPath1), $"Replay file should exist at {replayPath1}");

                // --- Phase 2: Simultaneous replay and manual recording ---
                config.SuspendReplayDuringRecording = false;

                // Wait a moment for replay buffer continuity
                await Task.Delay(1500);
                Assert.True(engine.IsActive, "Replay output should be active before simultaneous recording");

                // Start manual recording while replay is active (both outputs active)
                recordPath2 = await RunOnObs(() => engine.StartRecordingAsync()).Unwrap();
                Assert.True(engine.IsRecording, "Recording output should be active");
                Assert.True(engine.IsActive, "Replay output should STILL be active simultaneously");
                Assert.False(engine.IsReplaySuspendedForManualRecording, "Replay should not be suspended in simultaneous mode");

                // Record for 2 seconds simultaneously
                await Task.Delay(2000);
                Assert.True(engine.RecordingOutputBytes > 0, "Simultaneous recording output bytes should be > 0");
                Assert.True(engine.IsActive, "Replay output should remain active while recording simultaneously");

                // Stop recording
                recordPath2 = await engine.StopRecordingAsync();
                Assert.False(engine.IsRecording, "Recording should be stopped");
                Assert.True(engine.IsActive, "Replay output should remain active after recording stops");
                Assert.True(File.Exists(recordPath2), $"Recording file should exist at {recordPath2}");

                // Save replay
                ReplaySaveOperation saveOp2 = await RunOnObs(() => engine.BeginSaveReplay());
                replayPath2 = await saveOp2.Completion;
                Assert.True(File.Exists(replayPath2), $"Replay file should exist at {replayPath2}");
            }
            finally
            {
                if (engine is not null)
                {
                    await RunOnObsAction(engine.Dispose);
                }
            }

            // Media Validation on all generated files
            ValidateMediaFile(recordPath1);
            ValidateMediaFile(replayPath1);
            ValidateMediaFile(recordPath2);
            ValidateMediaFile(replayPath2);
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
