using System.Globalization;
using Xunit;

namespace Captail.Tests;

public sealed class PerformanceTelemetryTests
{
    [Fact]
    public void CalculateDelta_ZeroDelta_ReturnsZeroValues()
    {
        var now = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = new ReplayPerformanceSnapshot(
            TimestampUtc: now,
            TotalRenderedFrames: 1000,
            LaggedRenderedFrames: 5,
            EncodedFrames: 1000,
            ReplayOutputBytes: 50_000_000,
            RecordingOutputBytes: 0,
            WorkingSetBytes: 150_000_000,
            ProcessCpuTime: TimeSpan.FromSeconds(10),
            ReplayOutputActive: true,
            RecordingOutputActive: false);

        ReplayPerformanceDelta delta = ReplayPerformanceDelta.Calculate(snapshot, snapshot);

        Assert.Equal(TimeSpan.Zero, delta.Duration);
        Assert.Equal(0u, delta.RenderedFrames);
        Assert.Equal(0u, delta.LaggedFrames);
        Assert.Equal(0.0, delta.LaggedFramePercent);
        Assert.Equal(0, delta.EncodedFrames);
        Assert.Equal(0ul, delta.ReplayBytes);
        Assert.Equal(0ul, delta.RecordingBytes);
        Assert.Equal(TimeSpan.Zero, delta.CpuTime);
        Assert.Equal(0L, delta.WorkingSetDeltaBytes);
    }

    [Fact]
    public void CalculateDelta_NormalDelta_CalculatesAccurately()
    {
        var start = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(30);

        var startSnapshot = new ReplayPerformanceSnapshot(
            TimestampUtc: start,
            TotalRenderedFrames: 1000,
            LaggedRenderedFrames: 5,
            EncodedFrames: 1000,
            ReplayOutputBytes: 10_000_000,
            RecordingOutputBytes: 5_000_000,
            WorkingSetBytes: 200_000_000,
            ProcessCpuTime: TimeSpan.FromSeconds(10),
            ReplayOutputActive: true,
            RecordingOutputActive: true);

        var endSnapshot = new ReplayPerformanceSnapshot(
            TimestampUtc: end,
            TotalRenderedFrames: 2800,
            LaggedRenderedFrames: 7,
            EncodedFrames: 2800,
            ReplayOutputBytes: 25_000_000,
            RecordingOutputBytes: 40_000_000,
            WorkingSetBytes: 220_000_000,
            ProcessCpuTime: TimeSpan.FromSeconds(12.5),
            ReplayOutputActive: true,
            RecordingOutputActive: true);

        ReplayPerformanceDelta delta = ReplayPerformanceDelta.Calculate(startSnapshot, endSnapshot);

        Assert.Equal(TimeSpan.FromSeconds(30), delta.Duration);
        Assert.Equal(1800u, delta.RenderedFrames);
        Assert.Equal(2u, delta.LaggedFrames);
        Assert.Equal(2.0 / 1800.0 * 100.0, delta.LaggedFramePercent, precision: 4);
        Assert.Equal(1800, delta.EncodedFrames);
        Assert.Equal(15_000_000ul, delta.ReplayBytes);
        Assert.Equal(35_000_000ul, delta.RecordingBytes);
        Assert.Equal(TimeSpan.FromSeconds(2.5), delta.CpuTime);
        Assert.Equal(20_000_000L, delta.WorkingSetDeltaBytes);
    }

    [Fact]
    public void CalculateDelta_CounterReset_ProtectsAgainstUnderflow()
    {
        var start = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(10);

        // Simulated pipeline restart where all counters reset to smaller values
        var startSnapshot = new ReplayPerformanceSnapshot(
            TimestampUtc: start,
            TotalRenderedFrames: 50_000,
            LaggedRenderedFrames: 120,
            EncodedFrames: 49_000,
            ReplayOutputBytes: 500_000_000,
            RecordingOutputBytes: 300_000_000,
            WorkingSetBytes: 200_000_000,
            ProcessCpuTime: TimeSpan.FromSeconds(50),
            ReplayOutputActive: true,
            RecordingOutputActive: false);

        var endSnapshot = new ReplayPerformanceSnapshot(
            TimestampUtc: end,
            TotalRenderedFrames: 600,
            LaggedRenderedFrames: 1,
            EncodedFrames: 590,
            ReplayOutputBytes: 5_000_000,
            RecordingOutputBytes: 0,
            WorkingSetBytes: 150_000_000,
            ProcessCpuTime: TimeSpan.FromSeconds(52),
            ReplayOutputActive: true,
            RecordingOutputActive: false);

        ReplayPerformanceDelta delta = ReplayPerformanceDelta.Calculate(startSnapshot, endSnapshot);

        Assert.Equal(TimeSpan.FromSeconds(10), delta.Duration);
        Assert.Equal(600u, delta.RenderedFrames);
        Assert.Equal(1u, delta.LaggedFrames);
        Assert.Equal(590, delta.EncodedFrames);
        Assert.Equal(5_000_000ul, delta.ReplayBytes);
        Assert.Equal(0ul, delta.RecordingBytes);
        Assert.Equal(TimeSpan.FromSeconds(2), delta.CpuTime);
        Assert.Equal(-50_000_000L, delta.WorkingSetDeltaBytes);
    }

    [Fact]
    public void CalculateDelta_ZeroRenderedFrames_LaggedPercentDoesNotDivideByZero()
    {
        var start = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(5);

        var startSnapshot = new ReplayPerformanceSnapshot(
            TimestampUtc: start,
            TotalRenderedFrames: 0,
            LaggedRenderedFrames: 0,
            EncodedFrames: 0,
            ReplayOutputBytes: 0,
            RecordingOutputBytes: 0,
            WorkingSetBytes: 100_000_000,
            ProcessCpuTime: TimeSpan.FromSeconds(1),
            ReplayOutputActive: false,
            RecordingOutputActive: false);

        var endSnapshot = new ReplayPerformanceSnapshot(
            TimestampUtc: end,
            TotalRenderedFrames: 0,
            LaggedRenderedFrames: 0,
            EncodedFrames: 0,
            ReplayOutputBytes: 0,
            RecordingOutputBytes: 0,
            WorkingSetBytes: 100_000_000,
            ProcessCpuTime: TimeSpan.FromSeconds(1),
            ReplayOutputActive: false,
            RecordingOutputActive: false);

        ReplayPerformanceDelta delta = ReplayPerformanceDelta.Calculate(startSnapshot, endSnapshot);

        Assert.Equal(0.0, delta.LaggedFramePercent);
        Assert.False(double.IsNaN(delta.LaggedFramePercent));
        Assert.False(double.IsInfinity(delta.LaggedFramePercent));
    }

    [Fact]
    public void ToPerfLogString_FormatsStandardStructuredLogLine()
    {
        var delta = new ReplayPerformanceDelta(
            Duration: TimeSpan.FromMilliseconds(30000),
            RenderedFrames: 1800,
            LaggedFrames: 2,
            LaggedFramePercent: 0.1111,
            EncodedFrames: 1800,
            ReplayBytes: 12345678,
            RecordingBytes: 87654321,
            CpuTime: TimeSpan.FromMilliseconds(1250),
            WorkingSetDeltaBytes: 10 * 1024 * 1024);

        string log = delta.ToPerfLogString("replay+record");

        Assert.StartsWith("PERF scenario=replay+record durationMs=30000 rendered=1800 lagged=2 laggedPct=0.111 encoded=1800", log);
        Assert.Contains("replayBytes=12345678", log);
        Assert.Contains("recordingBytes=87654321", log);
        Assert.Contains("cpuMs=1250", log);
        Assert.Contains("workingSetMb=10.00", log);
    }
}
