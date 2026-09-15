using System.IO;

namespace Captail;

internal sealed class DiskReplayBufferManager
{
    internal const int DefaultSegmentSeconds = 30;
    internal const long MinimumFreeDiskSpaceBytes = 1024L * 1024 * 1024 * 2; // 2 GB

    public static void PrepareBufferDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
    }

    public static void CleanTemporaryBuffer(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        try
        {
            var files = Directory.GetFiles(directory, "buf_*.mp4")
                .Concat(Directory.GetFiles(directory, "buf_*.mkv"))
                .Concat(Directory.GetFiles(directory, "concat_*.txt"));

            foreach (string file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception exception)
                {
                    // In-use or transiently locked files are ignored with diagnostic record.
                    Log.Write($"Temporary buffer file delete skipped for '{file}': {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            Log.Write($"Temporary buffer directory clean failed for '{directory}': {exception.Message}");
        }
    }

    public static IReadOnlyList<string> PruneOldSegments(
        string directory,
        int bufferSeconds,
        TimeSpan segmentDuration)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return [];

        List<FileInfo> segments;
        try
        {
            segments = Directory.EnumerateFiles(directory, "buf_*.*")
                .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.CreationTimeUtc)
                .ToList();
        }
        catch
        {
            return [];
        }

        if (segments.Count == 0)
            return [];

        // Max segments to keep: bufferSeconds / segmentDuration + 1 safety margin segment
        double segSec = segmentDuration.TotalSeconds > 0 ? segmentDuration.TotalSeconds : DefaultSegmentSeconds;
        int segmentsToKeep = Math.Max(2, (int)Math.Ceiling(bufferSeconds / segSec) + 1);

        var deleted = new List<string>();
        while (segments.Count > segmentsToKeep)
        {
            FileInfo oldest = segments[0];
            segments.RemoveAt(0);
            try
            {
                oldest.Delete();
                deleted.Add(oldest.FullName);
            }
            catch (Exception exception)
            {
                // If currently open/writing, log diagnostic and move on
                Log.Write($"Prune old segment skipped for '{oldest.FullName}': {exception.Message}");
            }
        }

        return deleted;
    }

    public static IReadOnlyList<string> GetReplaySegments(
        string directory,
        int requestedSeconds,
        TimeSpan segmentDuration)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return [];

        List<FileInfo> segments;
        try
        {
            segments = Directory.EnumerateFiles(directory, "buf_*.*")
                .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.CreationTimeUtc)
                .ToList();
        }
        catch
        {
            return [];
        }

        if (segments.Count == 0)
            return [];

        double segSec = segmentDuration.TotalSeconds > 0 ? segmentDuration.TotalSeconds : DefaultSegmentSeconds;
        int neededCount = Math.Max(1, (int)Math.Ceiling(requestedSeconds / segSec));
        return segments
            .TakeLast(neededCount)
            .Select(s => s.FullName)
            .ToList();
    }

    public static long GetAvailableFreeSpaceBytes(string directory)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(directory)) ?? "";
            if (string.IsNullOrEmpty(root))
                return long.MaxValue;

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : long.MaxValue;
        }
        catch (Exception exception)
        {
            Log.Write($"Failed to query available free space for '{directory}': {exception.Message}");
            return long.MaxValue;
        }
    }

    public static async Task<string> SaveReplayAsync(
        string directory,
        string destinationPath,
        int requestedSeconds,
        TimeSpan segmentDuration,
        FfmpegAdapter ffmpeg,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> segments = GetReplaySegments(
            directory,
            requestedSeconds,
            segmentDuration);

        if (segments.Count == 0)
            throw new InvalidOperationException("No buffer segments available to save replay.");

        await ffmpeg.ConcatenateSegmentsAsync(
            segments,
            destinationPath,
            cancellationToken);

        return destinationPath;
    }
}
