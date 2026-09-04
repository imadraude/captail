namespace Captail.Tests;

using System.Security.Cryptography;
using System.Text;
using Xunit;

public sealed class ReplayLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Captail.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SelectPageReturnsFirstPage()
    {
        Directory.CreateDirectory(_directory);
        FileInfo[] files = Enumerable.Range(0, 150)
            .Select(CreateFile)
            .ToArray();

        List<FileInfo> page = ReplayLibrary.SelectPage(files, 0, 64);

        Assert.Equal(64, page.Count);
        Assert.Equal("149.mp4", page[0].Name);
        Assert.Equal("086.mp4", page[^1].Name);
    }

    [Fact]
    public void SelectPageReturnsRequestedNewestWindow()
    {
        Directory.CreateDirectory(_directory);
        FileInfo[] files = Enumerable.Range(0, 150)
            .Select(CreateFile)
            .ToArray();

        List<FileInfo> page = ReplayLibrary.SelectPage(files, 64, 64);

        Assert.Equal(64, page.Count);
        Assert.Equal("085.mp4", page[0].Name);
        Assert.Equal("022.mp4", page[^1].Name);
    }

    [Fact]
    public void SelectPageReturnsRemainingFilesWhenPageExceedsCount()
    {
        Directory.CreateDirectory(_directory);
        FileInfo[] files = Enumerable.Range(0, 150)
            .Select(CreateFile)
            .ToArray();

        List<FileInfo> page = ReplayLibrary.SelectPage(files, 140, 64);

        Assert.Equal(10, page.Count);
        Assert.Equal("009.mp4", page[0].Name);
        Assert.Equal("000.mp4", page[^1].Name);
    }

    [Fact]
    public void SelectPageReturnsEmptyWhenSkipExceedsCount()
    {
        Directory.CreateDirectory(_directory);
        FileInfo[] files = Enumerable.Range(0, 10)
            .Select(CreateFile)
            .ToArray();

        List<FileInfo> page = ReplayLibrary.SelectPage(files, 50, 64);

        Assert.Empty(page);
    }

    [Fact]
    public void SelectPageThrowsOnNegativeSkip()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReplayLibrary.SelectPage([], -1, 10));
    }

    [Fact]
    public void SelectPageReturnsEmptyOnZeroOrNegativeLimit()
    {
        Assert.Empty(ReplayLibrary.SelectPage([], 0, 0));
        Assert.Empty(ReplayLibrary.SelectPage([], 0, -10));
    }

    [Fact]
    public void SelectPageHonorsCancellation()
    {
        Directory.CreateDirectory(_directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ReplayLibrary.SelectPage([CreateFile(0)], 0, 1, cancellation.Token));
    }

    [Fact]
    public async Task GetPageAsyncReturnsRequestedPages()
    {
        Directory.CreateDirectory(_directory);
        for (int i = 0; i < 5; i++)
            CreateFile(i);

        var library = new ReplayLibrary(
            new FfmpegAdapter(_directory),
            Path.Combine(_directory, "thumb_cache"));

        IReadOnlyList<ReplayClip> page1 = await library.GetPageAsync(_directory, 0, 2);
        IReadOnlyList<ReplayClip> page2 = await library.GetPageAsync(_directory, 2, 2);

        Assert.Equal(2, page1.Count);
        Assert.Equal("004.mp4", page1[0].Name);
        Assert.Equal("003.mp4", page1[1].Name);

        Assert.Equal(2, page2.Count);
        Assert.Equal("002.mp4", page2[0].Name);
        Assert.Equal("001.mp4", page2[1].Name);
    }

    [Fact]
    public async Task GetRecentAsyncDelegatesToFirstPage()
    {
        Directory.CreateDirectory(_directory);
        for (int i = 0; i < 5; i++)
            CreateFile(i);

        var library = new ReplayLibrary(
            new FfmpegAdapter(_directory),
            Path.Combine(_directory, "thumb_cache"));

        IReadOnlyList<ReplayClip> recent = await library.GetRecentAsync(_directory, 3);

        Assert.Equal(3, recent.Count);
        Assert.Equal("004.mp4", recent[0].Name);
        Assert.Equal("003.mp4", recent[1].Name);
        Assert.Equal("002.mp4", recent[2].Name);
    }

    [Fact]
    public async Task GetPageAsyncReturnsEmptyForMissingDirectory()
    {
        var library = new ReplayLibrary(
            new FfmpegAdapter(_directory),
            Path.Combine(_directory, "thumb_cache"));

        IReadOnlyList<ReplayClip> result = await library.GetPageAsync(
            Path.Combine(_directory, "nonexistent"),
            0,
            10);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPageAsyncThrowsOnNegativeSkip()
    {
        var library = new ReplayLibrary(
            new FfmpegAdapter(_directory),
            Path.Combine(_directory, "thumb_cache"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            library.GetPageAsync(_directory, -1, 10));
    }

    [Fact]
    public async Task GetPageAsyncUsesCachedDurationFromMetaFile()
    {
        Directory.CreateDirectory(_directory);
        string thumbCache = Path.Combine(_directory, "thumb_cache");
        Directory.CreateDirectory(thumbCache);

        string ffmpegExe = Path.Combine(_directory, "ffmpeg.exe");
        string ffprobeExe = Path.Combine(_directory, "ffprobe.exe");
        File.WriteAllText(ffmpegExe, "dummy");
        File.WriteAllText(ffprobeExe, "dummy");

        FileInfo file = CreateFile(1);
        string key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        TimeSpan expectedDuration = TimeSpan.FromSeconds(42.5);
        string metaPath = Path.Combine(thumbCache, $"{hash}.meta");
        await File.WriteAllTextAsync(metaPath, expectedDuration.Ticks.ToString());

        var library = new ReplayLibrary(
            new FfmpegAdapter(_directory),
            thumbCache);

        IReadOnlyList<ReplayClip> clips = await library.GetPageAsync(_directory, 0, 10);

        Assert.Single(clips);
        Assert.Equal(expectedDuration, clips[0].Duration);
    }

    [Fact]
    public void DeleteToRecycleBinDeletesThumbnailAndMetaFiles()
    {
        Directory.CreateDirectory(_directory);
        string thumbCache = Path.Combine(_directory, "thumb_cache");
        Directory.CreateDirectory(thumbCache);

        FileInfo file = CreateFile(1);
        string key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        string thumbPath = Path.Combine(thumbCache, $"{hash}.jpg");
        string metaPath = Path.Combine(thumbCache, $"{hash}.meta");
        File.WriteAllText(thumbPath, "dummy-thumb");
        File.WriteAllText(metaPath, "dummy-meta");

        var library = new ReplayLibrary(
            new FfmpegAdapter(_directory),
            thumbCache);

        var clip = new ReplayClip(
            file.FullName,
            file.Name,
            null,
            file.LastWriteTime,
            file.Length,
            TimeSpan.FromSeconds(10),
            thumbPath);

        library.DeleteToRecycleBin(_directory, clip);

        Assert.False(File.Exists(thumbPath));
        Assert.False(File.Exists(metaPath));
    }

    [Fact]
    public void DeleteToRecycleBinDeletesMetaFileEvenWhenThumbnailPathIsNull()
    {
        Directory.CreateDirectory(_directory);
        string thumbCache = Path.Combine(_directory, "thumb_cache");
        Directory.CreateDirectory(thumbCache);

        FileInfo file = CreateFile(2);
        string key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        string metaPath = Path.Combine(thumbCache, $"{hash}.meta");
        File.WriteAllText(metaPath, "dummy-meta");

        var library = new ReplayLibrary(
            new FfmpegAdapter(_directory),
            thumbCache);

        var clip = new ReplayClip(
            file.FullName,
            file.Name,
            null,
            file.LastWriteTime,
            file.Length,
            TimeSpan.FromSeconds(10),
            null);

        library.DeleteToRecycleBin(_directory, clip);

        Assert.False(File.Exists(metaPath));
    }

    [Fact]
    public void Rename_ValidNewName_RenamesFileAndMigratesThumbnails()
    {
        Directory.CreateDirectory(_directory);
        string thumbCache = Path.Combine(_directory, "thumb_cache");
        Directory.CreateDirectory(thumbCache);

        FileInfo file = CreateFile(10);
        string oldKey = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        string oldHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldKey)));

        string oldThumbPath = Path.Combine(thumbCache, $"{oldHash}.jpg");
        string oldMetaPath = Path.Combine(thumbCache, $"{oldHash}.meta");
        File.WriteAllText(oldThumbPath, "dummy-thumb");
        File.WriteAllText(oldMetaPath, "dummy-meta");

        var library = new ReplayLibrary(
            new FfmpegAdapter(_directory),
            thumbCache);

        var clip = new ReplayClip(
            file.FullName,
            file.Name,
            null,
            file.LastWriteTime,
            file.Length,
            TimeSpan.FromSeconds(15),
            oldThumbPath);

        ReplayClip renamed = library.Rename(_directory, clip, "great_highlight");

        string expectedNewPath = Path.Combine(_directory, "great_highlight.mp4");
        Assert.False(File.Exists(file.FullName));
        Assert.True(File.Exists(expectedNewPath));
        Assert.Equal(expectedNewPath, renamed.Path);
        Assert.Equal("great_highlight.mp4", renamed.Name);
        Assert.Equal(clip.Duration, renamed.Duration);

        string newKey = $"{expectedNewPath}|{renamed.SizeBytes}|{renamed.SavedAt.ToUniversalTime().Ticks}";
        string newHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newKey)));
        string expectedThumbPath = Path.Combine(thumbCache, $"{newHash}.jpg");
        string expectedMetaPath = Path.Combine(thumbCache, $"{newHash}.meta");

        Assert.True(File.Exists(expectedThumbPath));
        Assert.True(File.Exists(expectedMetaPath));
        Assert.False(File.Exists(oldThumbPath));
        Assert.False(File.Exists(oldMetaPath));
        Assert.Equal(expectedThumbPath, renamed.ThumbnailPath);
    }

    [Fact]
    public void Rename_SameName_ReturnsOriginalClip()
    {
        Directory.CreateDirectory(_directory);
        FileInfo file = CreateFile(11);
        var library = new ReplayLibrary(new FfmpegAdapter(_directory), _directory);
        var clip = new ReplayClip(
            file.FullName,
            file.Name,
            null,
            file.LastWriteTime,
            file.Length,
            TimeSpan.FromSeconds(5),
            null);

        ReplayClip result = library.Rename(_directory, clip, Path.GetFileNameWithoutExtension(file.Name));
        Assert.Equal(clip.Path, result.Path);
        Assert.True(File.Exists(file.FullName));
    }

    [Fact]
    public void Rename_TargetAlreadyExists_ThrowsIOException()
    {
        Directory.CreateDirectory(_directory);
        FileInfo fileA = CreateFile(12);
        FileInfo fileB = CreateFile(13);

        var library = new ReplayLibrary(new FfmpegAdapter(_directory), _directory);
        var clipA = new ReplayClip(
            fileA.FullName,
            fileA.Name,
            null,
            fileA.LastWriteTime,
            fileA.Length,
            TimeSpan.FromSeconds(5),
            null);

        Assert.Throws<IOException>(() =>
            library.Rename(_directory, clipA, Path.GetFileNameWithoutExtension(fileB.Name)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("clip/name")]
    [InlineData("clip\\name")]
    [InlineData("..")]
    [InlineData("clip:name")]
    [InlineData("clip*name")]
    public void Rename_InvalidName_ThrowsArgumentException(string invalidName)
    {
        Directory.CreateDirectory(_directory);
        FileInfo file = CreateFile(14);
        var library = new ReplayLibrary(new FfmpegAdapter(_directory), _directory);
        var clip = new ReplayClip(
            file.FullName,
            file.Name,
            null,
            file.LastWriteTime,
            file.Length,
            TimeSpan.FromSeconds(5),
            null);

        Assert.Throws<ArgumentException>(() =>
            library.Rename(_directory, clip, invalidName));
    }

    [Fact]
    public void Rename_InternalWorkingFileName_ThrowsArgumentException()
    {
        Directory.CreateDirectory(_directory);
        FileInfo file = CreateFile(15);
        var library = new ReplayLibrary(new FfmpegAdapter(_directory), _directory);
        var clip = new ReplayClip(
            file.FullName,
            file.Name,
            null,
            file.LastWriteTime,
            file.Length,
            TimeSpan.FromSeconds(5),
            null);

        Assert.Throws<ArgumentException>(() =>
            library.Rename(_directory, clip, "recording.tmp"));
    }

    private FileInfo CreateFile(int index)
    {
        string path = Path.Combine(_directory, $"{index:000}.mp4");
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddMinutes(index));
        return new FileInfo(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
