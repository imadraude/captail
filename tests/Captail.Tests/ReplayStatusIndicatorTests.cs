namespace Captail.Tests;

using Captail;
using Xunit;

public sealed class ReplayStatusIndicatorTests
{
    [Fact]
    public void IndicatorConstantsAreCompactAndTranslucent()
    {
        Assert.Equal(18, ReplayStatusIndicatorWindow.BaseWindowSize);
        Assert.Equal(8, ReplayStatusIndicatorWindow.BaseEdgeInset);
        Assert.Equal(4, ReplayStatusIndicatorWindow.BaseIndicatorGap);
        Assert.Equal(22, ReplayStatusIndicatorWindow.MultiIndicatorOffset);
        Assert.Equal(0.75, ReplayStatusIndicatorWindow.BaseIndicatorOpacity);
    }

    [Theory]
    [InlineData(ReplayIndicatorPlacement.TopRight)]
    [InlineData(ReplayIndicatorPlacement.TopLeft)]
    [InlineData(ReplayIndicatorPlacement.BottomRight)]
    [InlineData(ReplayIndicatorPlacement.BottomLeft)]
    public void IndicatorHasEqualSmallMarginsFromBothEdgesOfEveryCorner(ReplayIndicatorPlacement placement)
    {
        var bounds = new ReplayStatusIndicatorWindow.Rect
        {
            Left = 0,
            Top = 0,
            Right = 1920,
            Bottom = 1080,
        };
        const int size = ReplayStatusIndicatorWindow.BaseWindowSize;
        const int inset = ReplayStatusIndicatorWindow.BaseEdgeInset;

        (int left, int top) = ReplayStatusIndicatorWindow.CalculateIndicatorPosition(
            bounds,
            size,
            inset,
            inwardOffset: 0,
            placement);

        int horizontalMargin = placement is ReplayIndicatorPlacement.TopRight or ReplayIndicatorPlacement.BottomRight
            ? bounds.Right - (left + size)
            : left - bounds.Left;

        int verticalMargin = placement is ReplayIndicatorPlacement.BottomLeft or ReplayIndicatorPlacement.BottomRight
            ? bounds.Bottom - (top + size)
            : top - bounds.Top;

        Assert.Equal(inset, horizontalMargin);
        Assert.Equal(inset, verticalMargin);
        Assert.Equal(horizontalMargin, verticalMargin);
    }

    [Theory]
    [InlineData(ReplayIndicatorPlacement.TopRight)]
    [InlineData(ReplayIndicatorPlacement.TopLeft)]
    [InlineData(ReplayIndicatorPlacement.BottomRight)]
    [InlineData(ReplayIndicatorPlacement.BottomLeft)]
    public void DualIndicatorsAreAlignedWithConsistentGap(ReplayIndicatorPlacement placement)
    {
        var bounds = new ReplayStatusIndicatorWindow.Rect
        {
            Left = 0,
            Top = 0,
            Right = 1920,
            Bottom = 1080,
        };
        const int size = ReplayStatusIndicatorWindow.BaseWindowSize;
        const int inset = ReplayStatusIndicatorWindow.BaseEdgeInset;
        const int offset = ReplayStatusIndicatorWindow.MultiIndicatorOffset;

        (int outerLeft, int outerTop) = ReplayStatusIndicatorWindow.CalculateIndicatorPosition(
            bounds,
            size,
            inset,
            inwardOffset: 0,
            placement);

        (int innerLeft, int innerTop) = ReplayStatusIndicatorWindow.CalculateIndicatorPosition(
            bounds,
            size,
            inset,
            inwardOffset: offset,
            placement);

        Assert.Equal(outerTop, innerTop);

        int gap = placement is ReplayIndicatorPlacement.TopRight or ReplayIndicatorPlacement.BottomRight
            ? outerLeft - (innerLeft + size)
            : innerLeft - (outerLeft + size);

        Assert.Equal(ReplayStatusIndicatorWindow.BaseIndicatorGap, gap);
    }

    [Fact]
    public void CalculateIndicatorBoundsReturnsMonitorWhenGameCoversDisplay()
    {
        var monitor = new ReplayStatusIndicatorWindow.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var workArea = new ReplayStatusIndicatorWindow.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1032 };
        var fullscreenGame = new ReplayStatusIndicatorWindow.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

        var bounds = ReplayStatusIndicatorWindow.CalculateIndicatorBounds(
            gameDetected: true,
            hasTargetGameWindow: true,
            monitorRect: monitor,
            workAreaRect: workArea,
            gameWindowRect: fullscreenGame);

        Assert.Equal(monitor, bounds);
    }

    [Fact]
    public void CalculateIndicatorBoundsReturnsGameWindowWhenWindowed()
    {
        var monitor = new ReplayStatusIndicatorWindow.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var workArea = new ReplayStatusIndicatorWindow.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1032 };
        var windowedGame = new ReplayStatusIndicatorWindow.Rect { Left = 160, Top = 90, Right = 1760, Bottom = 990 };

        var bounds = ReplayStatusIndicatorWindow.CalculateIndicatorBounds(
            gameDetected: true,
            hasTargetGameWindow: true,
            monitorRect: monitor,
            workAreaRect: workArea,
            gameWindowRect: windowedGame);

        Assert.Equal(windowedGame, bounds);
    }

    [Fact]
    public void CalculateIndicatorBoundsFallsBackToWorkAreaWhenNoGame()
    {
        var monitor = new ReplayStatusIndicatorWindow.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var workArea = new ReplayStatusIndicatorWindow.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1032 };
        var empty = default(ReplayStatusIndicatorWindow.Rect);

        var bounds = ReplayStatusIndicatorWindow.CalculateIndicatorBounds(
            gameDetected: false,
            hasTargetGameWindow: false,
            monitorRect: monitor,
            workAreaRect: workArea,
            gameWindowRect: empty);

        Assert.Equal(workArea, bounds);
    }

    [Theory]
    [InlineData("cs2.exe", "cs2.exe", true)]
    [InlineData("cs2.exe", "CS2", true)]
    [InlineData(@"C:\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe", "cs2.exe", true)]
    [InlineData("dota2.exe", "cs2.exe", false)]
    [InlineData("", "cs2.exe", false)]
    [InlineData(null, "cs2.exe", false)]
    public void IsExecutableMatchComparesCleanly(string? candidate, string? target, bool expected)
    {
        bool result = ReplayStatusIndicatorWindow.IsExecutableMatch(candidate, target);
        Assert.Equal(expected, result);
    }
}
