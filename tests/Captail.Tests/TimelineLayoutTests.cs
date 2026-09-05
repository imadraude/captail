namespace Captail.Tests;

using Xunit;

public sealed class TimelineLayoutTests
{
    [Fact]
    public void FullClipSelection_PositionsHandlesInsideTimelineAndFlushWithEdges()
    {
        // 5-minute video (300s) on 858px wide timeline
        const double duration = 300.0;
        const double timelineWidth = 858.0;
        const double handleWidth = 16.0;
        const double playheadWidth = 13.0;

        TimelineVisualState state = TimelineLayout.Calculate(
            selectionStart: 0,
            selectionEnd: 300.0,
            playbackPosition: 0,
            duration: duration,
            timelineWidth: timelineWidth,
            handleWidth: handleWidth,
            playheadWidth: playheadWidth);

        // StartThumb must start at x=0 (inside timeline, not -16)
        Assert.Equal(0.0, state.StartThumbLeft);

        // EndThumb must end at timelineWidth, so its left edge is timelineWidth - handleWidth (842)
        Assert.Equal(842.0, state.EndThumbLeft);

        // Unselected shades should have 0 width
        Assert.Equal(0.0, state.LeftShadeWidth);
        Assert.Equal(0.0, state.RightShadeWidth);

        // SelectionBorder spans the entire timeline
        Assert.Equal(0.0, state.SelectionBorderLeft);
        Assert.Equal(858.0, state.SelectionBorderWidth);

        // Playhead at position 0: center of 13px thumb is at -6.5 + 6.5 = 0.0
        Assert.Equal(-6.5, state.PlayheadThumbLeft);
    }

    [Fact]
    public void TrimmedSelection_PositionsHandlesAndSelectionBorderAccurately()
    {
        const double duration = 100.0;
        const double timelineWidth = 1000.0;
        const double handleWidth = 16.0;
        const double playheadWidth = 13.0;

        TimelineVisualState state = TimelineLayout.Calculate(
            selectionStart: 20.0,
            selectionEnd: 50.0,
            playbackPosition: 20.0,
            duration: duration,
            timelineWidth: timelineWidth,
            handleWidth: handleWidth,
            playheadWidth: playheadWidth);

        // 20s of 100s on 1000px = 200px
        Assert.Equal(200.0, state.StartThumbLeft);

        // 50s of 100s on 1000px = 500px; handle ends at 500px -> 500 - 16 = 484px
        Assert.Equal(484.0, state.EndThumbLeft);

        // Left shade covers [0, 200]
        Assert.Equal(0.0, state.LeftShadeLeft);
        Assert.Equal(200.0, state.LeftShadeWidth);

        // Right shade covers [500, 1000]
        Assert.Equal(500.0, state.RightShadeLeft);
        Assert.Equal(500.0, state.RightShadeWidth);

        // SelectionBorder covers [200, 500]
        Assert.Equal(200.0, state.SelectionBorderLeft);
        Assert.Equal(300.0, state.SelectionBorderWidth);

        // Playhead at selection start (20s -> 200px)
        Assert.Equal(200.0 - 6.5, state.PlayheadThumbLeft);
    }

    [Fact]
    public void PlayheadAtClipEnd_AlignsCenterLineWithRightEdge()
    {
        const double duration = 60.0;
        const double timelineWidth = 600.0;
        const double handleWidth = 16.0;
        const double playheadWidth = 13.0;

        TimelineVisualState state = TimelineLayout.Calculate(
            selectionStart: 0,
            selectionEnd: 60.0,
            playbackPosition: 60.0,
            duration: duration,
            timelineWidth: timelineWidth,
            handleWidth: handleWidth,
            playheadWidth: playheadWidth);

        // Center line of playhead is at playheadThumbLeft + 6.5 = 600.0
        Assert.Equal(600.0 - 6.5, state.PlayheadThumbLeft);
    }

    [Fact]
    public void VeryShortSelection_PreventsHandlesFromCrossingOver()
    {
        const double duration = 300.0;
        const double timelineWidth = 858.0;
        const double handleWidth = 16.0;
        const double playheadWidth = 13.0;

        TimelineVisualState state = TimelineLayout.Calculate(
            selectionStart: 100.0,
            selectionEnd: 100.25,
            playbackPosition: 100.0,
            duration: duration,
            timelineWidth: timelineWidth,
            handleWidth: handleWidth,
            playheadWidth: playheadWidth);

        Assert.True(state.EndThumbLeft >= state.StartThumbLeft);
        Assert.True(state.StartThumbLeft >= 0);
        Assert.True(state.EndThumbLeft <= timelineWidth - handleWidth);
    }

    [Fact]
    public void DegenerateInputs_HandledSafelyWithoutExceptions()
    {
        TimelineVisualState zeroState = TimelineLayout.Calculate(
            selectionStart: 0,
            selectionEnd: 0,
            playbackPosition: 0,
            duration: 0,
            timelineWidth: 0,
            handleWidth: 16.0,
            playheadWidth: 13.0);

        Assert.False(double.IsNaN(zeroState.StartThumbLeft));
        Assert.False(double.IsNaN(zeroState.EndThumbLeft));
        Assert.False(double.IsNaN(zeroState.PlayheadThumbLeft));
    }
}
