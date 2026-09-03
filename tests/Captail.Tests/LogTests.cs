namespace Captail.Tests;

using System.IO;
using Xunit;

public sealed class LogTests : IDisposable
{
    private readonly string _testDir = Path.Combine(
        Path.GetTempPath(),
        "Captail.Tests.Log",
        Guid.NewGuid().ToString("N"));

    public LogTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void RotateIfNeededMovesFileWhenExceedingMaxSize()
    {
        string logFile = Path.Combine(_testDir, "log.txt");
        string backupFile = Path.Combine(_testDir, "log.old.txt");

        File.WriteAllText(logFile, new string('A', 1000));
        Assert.True(File.Exists(logFile));
        Assert.False(File.Exists(backupFile));

        Log.RotateIfNeeded(logFile, maxSizeBytes: 500);

        Assert.False(File.Exists(logFile));
        Assert.True(File.Exists(backupFile));
        Assert.Equal(1000, new FileInfo(backupFile).Length);
    }

    [Fact]
    public void RotateIfNeededDoesNotRotateWhenBelowMaxSize()
    {
        string logFile = Path.Combine(_testDir, "log.txt");
        string backupFile = Path.Combine(_testDir, "log.old.txt");

        File.WriteAllText(logFile, new string('A', 100));

        Log.RotateIfNeeded(logFile, maxSizeBytes: 500);

        Assert.True(File.Exists(logFile));
        Assert.False(File.Exists(backupFile));
    }

    [Fact]
    public void RotateIfNeededOverwritesExistingBackup()
    {
        string logFile = Path.Combine(_testDir, "log.txt");
        string backupFile = Path.Combine(_testDir, "log.old.txt");

        File.WriteAllText(backupFile, "old backup content");
        File.WriteAllText(logFile, new string('B', 1000));

        Log.RotateIfNeeded(logFile, maxSizeBytes: 500);

        Assert.False(File.Exists(logFile));
        Assert.True(File.Exists(backupFile));
        Assert.Equal(1000, new FileInfo(backupFile).Length);
    }

    [Fact]
    public void RotateIfNeededDerivesBackupNameFromCustomFilename()
    {
        string logFile = Path.Combine(_testDir, "app_debug.log");
        string backupFile = Path.Combine(_testDir, "app_debug.old.log");

        File.WriteAllText(logFile, new string('C', 1000));
        bool rotated = Log.RotateIfNeeded(logFile, maxSizeBytes: 500);

        Assert.True(rotated);
        Assert.False(File.Exists(logFile));
        Assert.True(File.Exists(backupFile));
    }

    [Fact]
    public void RotateIfNeededReturnsFalseForNonExistentFile()
    {
        string logFile = Path.Combine(_testDir, "non_existent.log");
        bool rotated = Log.RotateIfNeeded(logFile, maxSizeBytes: 500);

        Assert.False(rotated);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }
}
