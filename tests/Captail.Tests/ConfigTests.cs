namespace Captail.Tests;

using Xunit;

public sealed class ConfigTests
{
    [Fact]
    public void ClonedConfigHasEqualValues()
    {
        var config = new Config
        {
            ReplayEnabled = true,
            BufferSeconds = 120,
            BitrateMbps = 45,
            Codec = "av1",
            FrameRate = 120,
            CaptureSource = "game",
            Hotkey = "Ctrl+Shift+F10",
            ToggleReplayHotkey = "Ctrl+Shift+F9",
            CaptureSystemAudio = true,
            CaptureMicrophone = true,
            MicrophoneVolume = 80,
            MicrophoneBoostDb = 10,
        };

        Config clone = config.Clone();

        Assert.True(config.ValuesEqual(clone));
        Assert.True(clone.ValuesEqual(config));
        Assert.True(config.PipelineEquals(clone));
    }

    [Fact]
    public void ChangingHotkeyOrReplayEnabledChangesValuesEqualButNotPipelineEquals()
    {
        var original = new Config
        {
            ReplayEnabled = true,
            Hotkey = "Ctrl+Shift+F10",
            BitrateMbps = 30,
        };
        Config modified = original.Clone();
        modified.Hotkey = "Ctrl+Shift+F11";

        Assert.False(original.ValuesEqual(modified));
        Assert.True(original.PipelineEquals(modified));

        modified = original.Clone();
        modified.ReplayEnabled = false;

        Assert.False(original.ValuesEqual(modified));
        Assert.True(original.PipelineEquals(modified));

        modified = original.Clone();
        modified.AutoUpdate = false;

        Assert.False(original.ValuesEqual(modified));
        Assert.True(original.PipelineEquals(modified));
    }

    [Fact]
    public void ChangingPipelinePropertyChangesBothValuesEqualAndPipelineEquals()
    {
        var original = new Config
        {
            BitrateMbps = 30,
            FrameRate = 60,
        };
        Config modified = original.Clone();
        modified.BitrateMbps = 50;

        Assert.False(original.ValuesEqual(modified));
        Assert.False(original.PipelineEquals(modified));
    }

    [Fact]
    public void ProcessAudioRoutesOrderDoesNotAffectEquality()
    {
        var config1 = new Config
        {
            AudioRoutingMode = "advanced",
            ProcessAudioRoutes =
            [
                new ProcessAudioRoute { Executable = "game.exe", Track = 1, Enabled = true },
                new ProcessAudioRoute { Executable = "discord.exe", Track = 2, Enabled = true }
            ]
        };

        var config2 = new Config
        {
            AudioRoutingMode = "advanced",
            ProcessAudioRoutes =
            [
                new ProcessAudioRoute { Executable = "discord.exe", Track = 2, Enabled = true },
                new ProcessAudioRoute { Executable = "game.exe", Track = 1, Enabled = true }
            ]
        };

        Assert.True(config1.ValuesEqual(config2));
    }

    [Fact]
    public void ChangingRecordHotkeyChangesValuesEqualButNotPipelineEquals()
    {
        var original = new Config
        {
            RecordHotkey = "Ctrl+Shift+F11",
        };
        Config modified = original.Clone();
        modified.RecordHotkey = "Ctrl+Shift+F8";

        Assert.False(original.ValuesEqual(modified));
        Assert.True(original.PipelineEquals(modified));
    }

    [Fact]
    public void NormalizeResolvesRecordHotkeyCollision()
    {
        var config = new Config
        {
            Hotkey = "Ctrl+Shift+F10",
            ToggleReplayHotkey = "Ctrl+Shift+F9",
            RecordHotkey = "Ctrl+Shift+F10", // Collision with Hotkey
        };

        config.Normalize();

        Assert.True(HotkeyManager.AreDistinct(config.Hotkey, config.ToggleReplayHotkey, config.RecordHotkey));
        Assert.Equal("Ctrl+Shift+F11", config.RecordHotkey);
    }

    [Fact]
    public void ChangingOpenAppHotkeyChangesValuesEqualButNotPipelineEquals()
    {
        var original = new Config
        {
            OpenAppHotkey = "Ctrl+Shift+F8",
        };
        Config modified = original.Clone();
        modified.OpenAppHotkey = "Ctrl+Shift+F7";

        Assert.False(original.ValuesEqual(modified));
        Assert.True(original.PipelineEquals(modified));
    }

    [Fact]
    public void NormalizeResolvesOpenAppHotkeyCollision()
    {
        var config = new Config
        {
            Hotkey = "Ctrl+Shift+F10",
            ToggleReplayHotkey = "Ctrl+Shift+F9",
            RecordHotkey = "Ctrl+Shift+F11",
            OpenAppHotkey = "Ctrl+Shift+F10", // Collision with Hotkey
        };

        config.Normalize();

        Assert.True(HotkeyManager.AreDistinct(
            config.Hotkey,
            config.ToggleReplayHotkey,
            config.RecordHotkey,
            config.OpenAppHotkey));
        Assert.Equal("Ctrl+Shift+F8", config.OpenAppHotkey);
    }

    [Fact]
    public void NewConfig_UsesLowOverheadByDefault()
    {
        var config = new Config();
        Assert.Equal(NvencModes.LowOverhead, config.NvencMode);
    }

    [Fact]
    public void Deserialize_MissingNvencMode_MigratesToLowOverhead()
    {
        string json = """
        {
            "BufferSeconds": 120,
            "FrameRate": 60
        }
        """;

        bool success = Config.TryDeserialize(json, out Config? config);

        Assert.True(success);
        Assert.NotNull(config);
        Assert.Equal(NvencModes.LowOverhead, config.NvencMode);
    }

    [Fact]
    public void Deserialize_ExplicitBalanced_PreservesBalanced()
    {
        string json = """
        {
            "NvencMode": "balanced",
            "FrameRate": 60
        }
        """;

        bool success = Config.TryDeserialize(json, out Config? config);

        Assert.True(success);
        Assert.NotNull(config);
        Assert.Equal(NvencModes.Balanced, config.NvencMode);
    }

    [Fact]
    public void Normalize_InvalidNvencMode_NormalizesToLowOverhead()
    {
        var config = new Config
        {
            NvencMode = "invalid_ultra_fast",
        };

        config.Normalize();

        Assert.Equal(NvencModes.LowOverhead, config.NvencMode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecommendedNvencSettings_LowOverhead_AppliesExpectedParameters(bool adaptiveQuantization)
    {
        ObsReplayEngine.NvencSettings settings = ObsReplayEngine.RecommendedNvencSettings(
            codec: "h264",
            mode: NvencModes.LowOverhead,
            lowOverheadAdaptiveQuantization: adaptiveQuantization,
            loadProfile: ObsReplayEngine.EncoderLoadProfile.Standard);

        Assert.Equal("p2", settings.Preset);
        Assert.Equal("ll", settings.Tune);
        Assert.Equal("disabled", settings.Multipass);
        Assert.False(settings.Lookahead);
        Assert.Equal(0, settings.BFrames);
        Assert.Equal(adaptiveQuantization, settings.AdaptiveQuantization);
    }
}
