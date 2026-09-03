namespace Captail.Tests;

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
