namespace Captail;

internal readonly record struct ReplayPerformanceSnapshot(
    DateTime TimestampUtc,
    uint TotalRenderedFrames,
    uint LaggedRenderedFrames,
    int EncodedFrames,
    ulong ReplayOutputBytes,
    ulong RecordingOutputBytes,
    long WorkingSetBytes,
    TimeSpan ProcessCpuTime,
    bool ReplayOutputActive,
    bool RecordingOutputActive);
