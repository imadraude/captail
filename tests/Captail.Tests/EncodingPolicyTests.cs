namespace Captail.Tests;

using Xunit;

public sealed class EncodingPolicyTests
{
    [Fact]
    public void AutomaticBitrate_1080p60_H264_ReturnsReference25Mbps()
    {
        int bitrate = ObsReplayEngine.AutomaticBitrateMbps(1920, 1080, 60, "h264");
        Assert.Equal(25, bitrate);
    }

    [Fact]
    public void AutomaticBitrate_AppliesCodecEfficiencyFactors()
    {
        int h264 = ObsReplayEngine.AutomaticBitrateMbps(1920, 1080, 60, "h264");
        int hevc = ObsReplayEngine.AutomaticBitrateMbps(1920, 1080, 60, "hevc");
        int av1 = ObsReplayEngine.AutomaticBitrateMbps(1920, 1080, 60, "av1");

        Assert.Equal(25, h264);
        Assert.Equal(19, hevc); // 25 * 0.75 = 18.75 -> 19
        Assert.Equal(15, av1);  // 25 * 0.60 = 15
        Assert.True(av1 < hevc);
        Assert.True(hevc < h264);
    }

    [Fact]
    public void EffectiveBitrate_ClampsQsvTo65Mbps()
    {
        int normal = ObsReplayEngine.EffectiveBitrateMbps(80, 3840, 2160, 60, "h264", "nvenc");
        int qsv = ObsReplayEngine.EffectiveBitrateMbps(80, 3840, 2160, 60, "h264", "qsv");

        Assert.Equal(80, normal);
        Assert.Equal(65, qsv);
    }

    [Fact]
    public void EstimateReplayBytes_CalculatesExpectedByteSize()
    {
        // 8 Mbps video + 192 Kbps audio (1 track) for 300 seconds
        // total bits/sec = 8,000,000 + 192,000 = 8,192,000 bits/sec
        // bytes = 8,192,000 * 300 / 8 = 307,200,000 bytes
        long bytes = Config.EstimateReplayBytes(8, 300, 192, 1);
        Assert.Equal(307_200_000L, bytes);
    }
}
