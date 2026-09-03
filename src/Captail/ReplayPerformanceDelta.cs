using System.Globalization;

namespace Captail;

internal readonly record struct ReplayPerformanceDelta(
    TimeSpan Duration,
    uint RenderedFrames,
    uint LaggedFrames,
    double LaggedFramePercent,
    int EncodedFrames,
    ulong ReplayBytes,
    ulong RecordingBytes,
    TimeSpan CpuTime,
    long WorkingSetDeltaBytes)
{
    public static ReplayPerformanceDelta Calculate(
        ReplayPerformanceSnapshot start,
        ReplayPerformanceSnapshot end)
    {
        TimeSpan duration = end.TimestampUtc >= start.TimestampUtc
            ? end.TimestampUtc - start.TimestampUtc
            : TimeSpan.Zero;

        uint rendered = end.TotalRenderedFrames >= start.TotalRenderedFrames
            ? end.TotalRenderedFrames - start.TotalRenderedFrames
            : end.TotalRenderedFrames;

        uint lagged = end.LaggedRenderedFrames >= start.LaggedRenderedFrames
            ? end.LaggedRenderedFrames - start.LaggedRenderedFrames
            : end.LaggedRenderedFrames;

        double laggedPct = rendered == 0
            ? 0.0
            : ((double)lagged / rendered) * 100.0;

        int encoded = end.EncodedFrames >= start.EncodedFrames
            ? end.EncodedFrames - start.EncodedFrames
            : end.EncodedFrames;
        if (encoded < 0)
            encoded = 0;

        ulong replayBytes = end.ReplayOutputBytes >= start.ReplayOutputBytes
            ? end.ReplayOutputBytes - start.ReplayOutputBytes
            : end.ReplayOutputBytes;

        ulong recordingBytes = end.RecordingOutputBytes >= start.RecordingOutputBytes
            ? end.RecordingOutputBytes - start.RecordingOutputBytes
            : end.RecordingOutputBytes;

        TimeSpan cpuTime = end.ProcessCpuTime >= start.ProcessCpuTime
            ? end.ProcessCpuTime - start.ProcessCpuTime
            : TimeSpan.Zero;

        long workingSetDelta = end.WorkingSetBytes - start.WorkingSetBytes;

        return new ReplayPerformanceDelta(
            Duration: duration,
            RenderedFrames: rendered,
            LaggedFrames: lagged,
            LaggedFramePercent: laggedPct,
            EncodedFrames: encoded,
            ReplayBytes: replayBytes,
            RecordingBytes: recordingBytes,
            CpuTime: cpuTime,
            WorkingSetDeltaBytes: workingSetDelta);
    }

    public string ToPerfLogString(string scenario, double? workingSetMb = null)
    {
        double wsMb = workingSetMb ?? (WorkingSetDeltaBytes / (1024.0 * 1024.0));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"PERF scenario={scenario} durationMs={(long)Duration.TotalMilliseconds} rendered={RenderedFrames} lagged={LaggedFrames} laggedPct={LaggedFramePercent:F3} encoded={EncodedFrames} replayBytes={ReplayBytes} recordingBytes={RecordingBytes} cpuMs={(long)CpuTime.TotalMilliseconds} workingSetMb={wsMb:F2}");
    }
}
