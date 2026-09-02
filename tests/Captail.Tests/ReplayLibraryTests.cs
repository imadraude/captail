namespace Captail.Tests;

using Xunit;

public sealed class ReplayLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Captail.Tests",
        Guid.NewGuid().ToString("N"));

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
    public void SelectPageHonorsCancellation()
    {
        Directory.CreateDirectory(_directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ReplayLibrary.SelectPage([CreateFile(0)], 0, 1, cancellation.Token));
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
