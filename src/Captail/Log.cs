using System.IO;

namespace Captail;

public static class Log
{
    internal const long MaxLogSizeBytes = 10 * 1024 * 1024;
    private static readonly Lock _lock = new();
    private static StreamWriter? _writer;
    private static int _pendingLines;
    private static long _nextRotationThresholdBytes = MaxLogSizeBytes;
    public static readonly string Path = AppDataPaths.LogFile;

    static Log() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Close();

    public static void Write(string message)
    {
        lock (_lock)
        {
            try
            {
                _writer ??= CreateWriter();
                _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
                _pendingLines++;
                if (_pendingLines >= 32 || IsUrgent(message))
                {
                    _writer.Flush();
                    _pendingLines = 0;
                    CheckRotation();
                }
            }
            catch
            {
                _writer?.Dispose();
                _writer = null;
                _pendingLines = 0;
            }
        }
    }

    public static void Close()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
            _pendingLines = 0;
        }
    }

    internal static void Flush()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _pendingLines = 0;
            CheckRotation();
        }
    }

    private static void CheckRotation()
    {
        if (_writer is not null && _writer.BaseStream.Length >= _nextRotationThresholdBytes)
        {
            long currentLength = _writer.BaseStream.Length;
            _writer.Dispose();
            _writer = null;
            if (RotateIfNeeded(Path, MaxLogSizeBytes))
            {
                _nextRotationThresholdBytes = MaxLogSizeBytes;
            }
            else
            {
                _nextRotationThresholdBytes = currentLength + 1024 * 1024;
            }
        }
    }

    private static StreamWriter CreateWriter()
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        RotateIfNeeded(Path, MaxLogSizeBytes);
        return new StreamWriter(new FileStream(
            Path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            16 * 1024,
            FileOptions.SequentialScan))
        {
            AutoFlush = false,
        };
    }

    internal static bool RotateIfNeeded(string logPath, long maxSizeBytes = MaxLogSizeBytes)
    {
        try
        {
            if (File.Exists(logPath))
            {
                var info = new FileInfo(logPath);
                if (info.Length >= maxSizeBytes)
                {
                    string? directory = System.IO.Path.GetDirectoryName(logPath);
                    if (string.IsNullOrEmpty(directory))
                        directory = ".";
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(logPath);
                    string ext = System.IO.Path.GetExtension(logPath);
                    string backup = System.IO.Path.Combine(directory, $"{fileName}.old{ext}");
                    File.Move(logPath, backup, overwrite: true);
                    return true;
                }
            }
        }
        catch
        {
            // Suppress rotation failure (e.g. file lock or permissions)
        }
        return false;
    }

    private static bool IsUrgent(string message) =>
        message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("crash", StringComparison.OrdinalIgnoreCase);
}
