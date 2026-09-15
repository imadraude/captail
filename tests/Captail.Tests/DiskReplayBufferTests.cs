namespace Captail.Tests;

using System.IO;
using Xunit;

public sealed class DiskReplayBufferTests : IDisposable
{
    private readonly string _testDir = Path.Combine(
        Path.GetTempPath(),
        "Captail.Tests.DiskBuffer",
        Guid.NewGuid().ToString("N"));

    public DiskReplayBufferTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void PrepareBufferDirectory_CreatesDirectoryWhenMissing()
    {
        string subDir = Path.Combine(_testDir, "sub_buffer");
        Assert.False(Directory.Exists(subDir));

        DiskReplayBufferManager.PrepareBufferDirectory(subDir);

        Assert.True(Directory.Exists(subDir));
    }

    [Fact]
    public void GetReplaySegments_ReturnsExpectedSegmentsInChronologicalOrder()
    {
        DateTime baseTime = DateTime.UtcNow.AddMinutes(-10);

        string seg1 = Path.Combine(_testDir, "buf_2026-09-15_10-00-00.mp4");
        string seg2 = Path.Combine(_testDir, "buf_2026-09-15_10-00-30.mp4");
        string seg3 = Path.Combine(_testDir, "buf_2026-09-15_10-01-00.mp4");
        string seg4 = Path.Combine(_testDir, "buf_2026-09-15_10-01-30.mp4");

        File.WriteAllText(seg1, "1");
        File.SetCreationTimeUtc(seg1, baseTime);

        File.WriteAllText(seg2, "2");
        File.SetCreationTimeUtc(seg2, baseTime.AddSeconds(30));

        File.WriteAllText(seg3, "3");
        File.SetCreationTimeUtc(seg3, baseTime.AddSeconds(60));

        File.WriteAllText(seg4, "4");
        File.SetCreationTimeUtc(seg4, baseTime.AddSeconds(90));

        // Request 60 seconds of replay (2 segments of 30 seconds)
        IReadOnlyList<string> segments = DiskReplayBufferManager.GetReplaySegments(
            _testDir,
            requestedSeconds: 60,
            segmentDuration: TimeSpan.FromSeconds(30));

        Assert.Equal(2, segments.Count);
        Assert.Equal(seg3, segments[0]);
        Assert.Equal(seg4, segments[1]);
    }

    [Fact]
    public void PruneOldSegments_DeletesOnlyExcessOldSegments()
    {
        DateTime baseTime = DateTime.UtcNow.AddMinutes(-10);

        var filePaths = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            string path = Path.Combine(_testDir, $"buf_segment_{i}.mp4");
            File.WriteAllText(path, $"segment {i}");
            File.SetCreationTimeUtc(path, baseTime.AddSeconds(i * 30));
            filePaths.Add(path);
        }

        // Buffer: 60s -> keep 60/30 + 1 = 3 segments. 5 - 3 = 2 oldest segments pruned.
        IReadOnlyList<string> deleted = DiskReplayBufferManager.PruneOldSegments(
            _testDir,
            bufferSeconds: 60,
            segmentDuration: TimeSpan.FromSeconds(30));

        Assert.Equal(2, deleted.Count);
        Assert.Contains(filePaths[0], deleted);
        Assert.Contains(filePaths[1], deleted);

        Assert.False(File.Exists(filePaths[0]));
        Assert.False(File.Exists(filePaths[1]));
        Assert.True(File.Exists(filePaths[2]));
        Assert.True(File.Exists(filePaths[3]));
        Assert.True(File.Exists(filePaths[4]));
    }

    [Fact]
    public void CleanTemporaryBuffer_RemovesBufferAndConcatFilesOnly()
    {
        string bufMp4 = Path.Combine(_testDir, "buf_2026-09-15_10-00-00.mp4");
        string bufMkv = Path.Combine(_testDir, "buf_2026-09-15_10-00-30.mkv");
        string concatTxt = Path.Combine(_testDir, "concat_123.txt");
        string keepVideo = Path.Combine(_testDir, "replay_saved.mp4");

        File.WriteAllText(bufMp4, "mp4");
        File.WriteAllText(bufMkv, "mkv");
        File.WriteAllText(concatTxt, "concat");
        File.WriteAllText(keepVideo, "keep this video");

        DiskReplayBufferManager.CleanTemporaryBuffer(_testDir);

        Assert.False(File.Exists(bufMp4));
        Assert.False(File.Exists(bufMkv));
        Assert.False(File.Exists(concatTxt));
        Assert.True(File.Exists(keepVideo));
    }

    [Fact]
    public void GetReplaySegments_ReturnsEmptyWhenDirectoryIsEmpty()
    {
        IReadOnlyList<string> segments = DiskReplayBufferManager.GetReplaySegments(
            _testDir,
            requestedSeconds: 120,
            segmentDuration: TimeSpan.FromSeconds(30));

        Assert.Empty(segments);
    }
}
