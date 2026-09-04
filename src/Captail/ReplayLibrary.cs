using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace Captail;

public sealed record ReplayClip(
    string Path,
    string Name,
    string? Collection,
    DateTime SavedAt,
    long SizeBytes,
    TimeSpan Duration,
    string? ThumbnailPath)
{
    public bool IsRecording => Name.StartsWith("Recording", StringComparison.OrdinalIgnoreCase);
    public string KindBadge => IsRecording ? "REC" : "REPLAY";
}

public sealed class ReplayLibrary
{
    private const long MaximumCacheBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan MaximumCacheAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan CacheMaintenanceInterval = TimeSpan.FromHours(1);
    private static long _lastCacheMaintenanceTicks;
    private static int _cacheMaintenanceInProgress;
    private static readonly HashSet<string> VideoExtensions = new(
        [".mp4", ".mkv", ".mov", ".webm"],
        StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TimeSpan> _durationCache = new(StringComparer.Ordinal);
    private readonly FfmpegAdapter _ffmpeg;
    private readonly string _thumbnailDirectory;

    public ReplayLibrary(FfmpegAdapter ffmpeg)
        : this(ffmpeg, AppDataPaths.ThumbnailDirectory)
    {
    }

    internal ReplayLibrary(FfmpegAdapter ffmpeg, string thumbnailDirectory)
    {
        _ffmpeg = ffmpeg;
        _thumbnailDirectory = thumbnailDirectory;
        TryMaintainCache();
    }

    public Task<IReadOnlyList<ReplayClip>> GetRecentAsync(
        string rootDirectory,
        int limit,
        CancellationToken cancellationToken = default) =>
        GetPageAsync(rootDirectory, 0, limit, cancellationToken);

    public async Task<IReadOnlyList<ReplayClip>> GetPageAsync(
        string rootDirectory,
        int skip,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(skip));
        if (limit <= 0 || !Directory.Exists(rootDirectory))
            return [];

        string root = NormalizeRoot(rootDirectory);
        List<FileInfo> files = await Task.Run(() =>
        {
            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                };
                // Keep only the requested prefix. A full OrderBy materializes every
                // FileInfo and scales poorly for long-running replay libraries.
                IEnumerable<FileInfo> candidates = Directory.EnumerateFiles(root, "*", options)
                    .Where(path =>
                        VideoExtensions.Contains(Path.GetExtension(path)) &&
                        !IsInternalWorkingFile(path))
                    .Select(path => new FileInfo(path));
                return SelectPage(candidates, skip, limit, cancellationToken);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                Log.Write($"Replay library scan failed: {exception.Message}");
                return [];
            }
        }, cancellationToken);

        if (files.Count == 0)
            return [];

        var clips = new ReplayClip[files.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Count),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 4,
            },
            async (index, token) =>
            {
                clips[index] = await LoadClipAsync(root, files[index], token);
            });
        return clips;
    }

    internal static List<FileInfo> SelectPage(
        IEnumerable<FileInfo> candidates,
        int skip,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(skip));
        if (limit <= 0)
            return [];

        int retainedCount = checked(skip + limit);
        var newest = new PriorityQueue<FileInfo, (long Ticks, string Path)>();
        foreach (FileInfo file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.Exists || file.Length <= 0)
                continue;
            newest.Enqueue(file, (file.LastWriteTimeUtc.Ticks, file.FullName));
            if (newest.Count > retainedCount)
                newest.Dequeue();
        }
        return newest.UnorderedItems
            .Select(item => item.Element)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(limit)
            .ToList();
    }

    public void Reveal(string rootDirectory, ReplayClip clip)
    {
        string path = ValidateClipPath(rootDirectory, clip.Path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Replay no longer exists.", path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }

    public void DeleteToRecycleBin(string rootDirectory, ReplayClip clip)
    {
        string path = ValidateClipPath(rootDirectory, clip.Path);
        if (!File.Exists(path))
            return;
        FileSystem.DeleteFile(
            path,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin,
            UICancelOption.ThrowException);
        DeleteThumbnail(clip.ThumbnailPath);
        string metaPath = MetadataPathFromClip(clip);
        DeleteThumbnail(metaPath);
        _durationCache.TryRemove(CacheKey(clip), out _);
    }

    public ReplayClip Rename(string rootDirectory, ReplayClip clip, string newNameWithoutExtension)
    {
        string sourcePath = ValidateClipPath(rootDirectory, clip.Path);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Replay no longer exists.", sourcePath);

        if (string.IsNullOrWhiteSpace(newNameWithoutExtension))
            throw new ArgumentException("New name cannot be empty.", nameof(newNameWithoutExtension));

        string trimmedName = newNameWithoutExtension.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        if (trimmedName.IndexOfAny(invalidChars) >= 0 ||
            trimmedName.Contains('/') ||
            trimmedName.Contains('\\') ||
            trimmedName == "." ||
            trimmedName == "..")
        {
            throw new ArgumentException("New name contains invalid characters.", nameof(newNameWithoutExtension));
        }

        string directory = Path.GetDirectoryName(sourcePath)!;
        string extension = Path.GetExtension(sourcePath);
        string newFileName = trimmedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? trimmedName
            : trimmedName + extension;

        if (IsInternalWorkingFile(newFileName))
            throw new ArgumentException("Name conflicts with internal working file pattern.", nameof(newNameWithoutExtension));

        string destinationPath = Path.Combine(directory, newFileName);
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            return clip;

        if (File.Exists(destinationPath))
            throw new IOException($"A file with the name '{newFileName}' already exists.");

        File.Move(sourcePath, destinationPath);

        string oldCacheKey = CacheKey(clip);
        string oldMetaPath = MetadataPathFromClip(clip);
        string oldThumbPath = clip.ThumbnailPath ?? (CachePathPrefix(oldCacheKey) + ".jpg");

        var newFileInfo = new FileInfo(destinationPath);
        string newCacheKey = CacheKey(newFileInfo);
        string newPrefix = CachePathPrefix(newCacheKey);
        string newMetaPath = newPrefix + ".meta";
        string newThumbPath = newPrefix + ".jpg";

        string? finalThumbnailPath = null;
        if (File.Exists(oldThumbPath))
        {
            try
            {
                File.Move(oldThumbPath, newThumbPath, overwrite: true);
                finalThumbnailPath = newThumbPath;
            }
            catch (IOException)
            {
                // Thumbnail cache migration is best effort.
            }
        }

        if (File.Exists(oldMetaPath))
        {
            try
            {
                File.Move(oldMetaPath, newMetaPath, overwrite: true);
            }
            catch (IOException)
            {
                // Metadata cache migration is best effort.
            }
        }

        if (_durationCache.TryRemove(oldCacheKey, out TimeSpan cachedDuration) && cachedDuration > TimeSpan.Zero)
        {
            _durationCache[newCacheKey] = cachedDuration;
        }
        else if (clip.Duration > TimeSpan.Zero)
        {
            _durationCache[newCacheKey] = clip.Duration;
        }

        return new ReplayClip(
            destinationPath,
            newFileName,
            clip.Collection,
            newFileInfo.LastWriteTime,
            newFileInfo.Length,
            clip.Duration,
            finalThumbnailPath);
    }

    public async Task<string> TrimAsync(
        string rootDirectory,
        ReplayClip clip,
        TimeSpan start,
        TimeSpan end,
        IReadOnlyList<int>? audioStreamIndices = null,
        bool mergeAudioTracks = false,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateClipPath(rootDirectory, clip.Path);
        if (!File.Exists(source))
            throw new FileNotFoundException("Replay no longer exists.", source);
        if (start < TimeSpan.Zero || end > clip.Duration + TimeSpan.FromMilliseconds(250) || end <= start)
            throw new ArgumentOutOfRangeException(nameof(start));

        string directory = Path.GetDirectoryName(source)!;
        string baseName = Path.GetFileNameWithoutExtension(source);
        string extension = Path.GetExtension(source);
        string destination = UniquePath(
            directory,
            $"{baseName}_trimmed_{DateTime.Now:HH-mm-ss}",
            extension);
        await _ffmpeg.TrimCopyAsync(
            source,
            destination,
            start,
            end,
            audioStreamIndices,
            mergeAudioTracks,
            cancellationToken);
        return destination;
    }

    public async Task<string> TrimOverwriteAsync(
        string rootDirectory,
        ReplayClip clip,
        TimeSpan start,
        TimeSpan end,
        IReadOnlyList<int>? audioStreamIndices = null,
        bool mergeAudioTracks = false,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateClipPath(rootDirectory, clip.Path);
        if (!File.Exists(source))
            throw new FileNotFoundException("Replay no longer exists.", source);
        if (start < TimeSpan.Zero ||
            end > clip.Duration + TimeSpan.FromMilliseconds(250) ||
            end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        await _ffmpeg.TrimOverwriteAsync(
            source,
            start,
            end,
            audioStreamIndices,
            mergeAudioTracks,
            cancellationToken);
        return source;
    }

    public Task<IReadOnlyList<AudioTrackInfo>> GetAudioTracksAsync(
        string rootDirectory,
        ReplayClip clip,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateClipPath(rootDirectory, clip.Path);
        return _ffmpeg.ReadAudioTracksAsync(source, cancellationToken);
    }

    public Task<VideoStreamInfo?> GetVideoInfoAsync(
        string rootDirectory,
        ReplayClip clip,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateClipPath(rootDirectory, clip.Path);
        return _ffmpeg.ReadVideoInfoAsync(source, cancellationToken);
    }

    public async Task<string?> GetAudioWaveformAsync(
        string rootDirectory,
        ReplayClip clip,
        AudioTrackInfo track,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateClipPath(rootDirectory, clip.Path);
        if (!_ffmpeg.IsAvailable || clip.Duration <= TimeSpan.Zero)
            return null;

        string identity =
            $"{source}|{clip.SizeBytes}|{clip.SavedAt.ToUniversalTime().Ticks}|" +
            $"audio:{track.StreamIndex}";
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        string path = Path.Combine(_thumbnailDirectory, $"{hash}_waveform.png");
        if (!File.Exists(path))
        {
            await _ffmpeg.CreateWaveformAsync(
                source,
                path,
                track.StreamIndex,
                1200,
                42,
                cancellationToken);
        }
        return path;
    }

    public async Task<IReadOnlyList<string>> GetTimelineThumbnailsAsync(
        string rootDirectory,
        ReplayClip clip,
        int count,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateClipPath(rootDirectory, clip.Path);
        if (!_ffmpeg.IsAvailable || count <= 0 || clip.Duration <= TimeSpan.Zero)
            return [];

        string identity = $"{source}|{clip.SizeBytes}|{clip.SavedAt.ToUniversalTime().Ticks}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var thumbnails = new List<string>(count);
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(
                _thumbnailDirectory,
                $"{hash}_timeline_{index}.jpg");
            if (!File.Exists(path))
            {
                double fraction = (index + 0.5) / count;
                await _ffmpeg.CreateThumbnailAtAsync(
                    source,
                    path,
                    TimeSpan.FromSeconds(clip.Duration.TotalSeconds * fraction),
                    cancellationToken);
            }
            thumbnails.Add(path);
        }
        return thumbnails;
    }

    public async Task<string?> GetPreviewThumbnailAsync(
        string rootDirectory,
        ReplayClip clip,
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateClipPath(rootDirectory, clip.Path);
        if (!_ffmpeg.IsAvailable || clip.Duration <= TimeSpan.Zero)
            return clip.ThumbnailPath;

        long bucket = Math.Max(0, (long)(position.TotalMilliseconds / 250) * 250);
        string identity = $"{source}|{clip.SizeBytes}|{clip.SavedAt.ToUniversalTime().Ticks}|{bucket}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        string path = Path.Combine(_thumbnailDirectory, $"{hash}_preview.jpg");
        if (!File.Exists(path))
        {
            await _ffmpeg.CreateThumbnailAtAsync(
                source,
                path,
                TimeSpan.FromMilliseconds(bucket),
                cancellationToken);
            TryMaintainCache();
        }
        return path;
    }

    private async Task<ReplayClip> LoadClipAsync(
        string rootDirectory,
        FileInfo file,
        CancellationToken cancellationToken)
    {
        TimeSpan duration = TimeSpan.Zero;
        string? thumbnail = null;
        if (_ffmpeg.IsAvailable)
        {
            try
            {
                string cacheKey = CacheKey(file);
                string prefix = CachePathPrefix(cacheKey);
                string metaPath = prefix + ".meta";
                thumbnail = prefix + ".jpg";
                duration = await GetOrFetchDurationAsync(file, cacheKey, metaPath, cancellationToken);
                if (!File.Exists(thumbnail))
                    await _ffmpeg.CreateThumbnailAsync(file.FullName, thumbnail, duration, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.Write($"Replay metadata failed ({file.Name}): {exception.Message}");
                thumbnail = null;
            }
        }

        string? collection = Path.GetRelativePath(rootDirectory, file.DirectoryName!);
        if (collection == ".")
            collection = null;
        return new ReplayClip(
            file.FullName,
            file.Name,
            collection,
            file.LastWriteTime,
            file.Length,
            duration,
            thumbnail);
    }

    private async Task<TimeSpan> GetOrFetchDurationAsync(
        FileInfo file,
        string cacheKey,
        string metaPath,
        CancellationToken cancellationToken)
    {
        if (_durationCache.TryGetValue(cacheKey, out TimeSpan cachedDuration))
            return cachedDuration;

        if (File.Exists(metaPath))
        {
            try
            {
                string text = await File.ReadAllTextAsync(metaPath, cancellationToken);
                if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) &&
                    ticks > 0)
                {
                    TimeSpan duration = TimeSpan.FromTicks(ticks);
                    RecordCachedDuration(cacheKey, duration);
                    return duration;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Ignore corrupted or inaccessible cache file
            }
        }

        TimeSpan fetched = await _ffmpeg.ReadDurationAsync(file.FullName, cancellationToken);
        if (fetched > TimeSpan.Zero)
        {
            RecordCachedDuration(cacheKey, fetched);
            try
            {
                Directory.CreateDirectory(_thumbnailDirectory);
                await File.WriteAllTextAsync(
                    metaPath,
                    fetched.Ticks.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Cache write failure is non-fatal
            }
        }

        return fetched;
    }

    private void RecordCachedDuration(string key, TimeSpan duration)
    {
        if (_durationCache.Count > 10000)
            _durationCache.Clear();
        _durationCache[key] = duration;
    }

    private static string CacheKey(FileInfo file) =>
        $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";

    private static string CacheKey(ReplayClip clip) =>
        $"{clip.Path}|{clip.SizeBytes}|{clip.SavedAt.ToUniversalTime().Ticks}";

    private string CachePathPrefix(string cacheKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey));
        return Path.Combine(_thumbnailDirectory, Convert.ToHexString(hash));
    }

    private string MetadataPathFromClip(ReplayClip clip)
    {
        if (clip.ThumbnailPath is not null)
            return Path.ChangeExtension(clip.ThumbnailPath, ".meta");

        return CachePathPrefix(CacheKey(clip)) + ".meta";
    }

    private static string ValidateClipPath(string rootDirectory, string clipPath)
    {
        string root = NormalizeRoot(rootDirectory);
        string path = Path.GetFullPath(clipPath);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Replay path is outside configured library.");
        if (!VideoExtensions.Contains(Path.GetExtension(path)))
            throw new InvalidOperationException("Unsupported replay file type.");
        return path;
    }

    private static string NormalizeRoot(string rootDirectory) =>
        Path.GetFullPath(rootDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    internal static bool IsInternalWorkingFile(string path)
    {
        string name = Path.GetFileName(path);
        return name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               name.Contains(".tmp.", StringComparison.OrdinalIgnoreCase) ||
               (name.StartsWith('.') &&
                name.Contains(".replacement", StringComparison.OrdinalIgnoreCase));
    }

    private static string UniquePath(string directory, string baseName, string extension)
    {
        string candidate = Path.Combine(directory, baseName + extension);
        for (int suffix = 2; File.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{baseName}_{suffix}{extension}");
        return candidate;
    }

    private static void DeleteThumbnail(string? thumbnailPath)
    {
        try
        {
            if (thumbnailPath is not null && File.Exists(thumbnailPath))
                File.Delete(thumbnailPath);
        }
        catch (IOException)
        {
            // Cache cleanup is best effort.
        }
    }

    internal void TryMaintainCache()
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        long lastTicks = Interlocked.Read(ref _lastCacheMaintenanceTicks);
        if (nowTicks - lastTicks < CacheMaintenanceInterval.Ticks)
            return;

        if (Interlocked.Exchange(ref _cacheMaintenanceInProgress, 1) != 0)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                MaintainCache();
                Interlocked.Exchange(ref _lastCacheMaintenanceTicks, DateTime.UtcNow.Ticks);
            }
            finally
            {
                Interlocked.Exchange(ref _cacheMaintenanceInProgress, 0);
            }
        });
    }

    private void MaintainCache()
    {
        try
        {
            if (!Directory.Exists(_thumbnailDirectory))
                return;

            DateTime cutoff = DateTime.UtcNow - MaximumCacheAge;
            FileInfo[] files = Directory.EnumerateFiles(_thumbnailDirectory)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            long retainedBytes = 0;
            foreach (FileInfo file in files)
            {
                bool expired = file.LastWriteTimeUtc < cutoff;
                long length;
                try
                {
                    length = file.Length;
                }
                catch (IOException)
                {
                    continue;
                }
                bool overBudget = retainedBytes + length > MaximumCacheBytes;
                if (expired || overBudget)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (IOException)
                    {
                        // Cache cleanup is best effort.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Cache cleanup is best effort.
                    }
                    continue;
                }
                retainedBytes += length;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Thumbnail cache cleanup failed: {exception.Message}");
        }
    }
}
