namespace Captail;

internal readonly record struct TimelineVisualState(
    double StartThumbLeft,
    double EndThumbLeft,
    double LeftShadeLeft,
    double LeftShadeWidth,
    double RightShadeLeft,
    double RightShadeWidth,
    double SelectionBorderLeft,
    double SelectionBorderWidth,
    double PlayheadThumbLeft,
    bool SelectionHasLeftOuterRound,
    bool SelectionHasRightOuterRound);

internal static class TimelineLayout
{
    public static TimelineVisualState Calculate(
        double selectionStart,
        double selectionEnd,
        double playbackPosition,
        double duration,
        double timelineWidth,
        double handleWidth,
        double playheadWidth)
    {
        double safeDuration = Math.Max(0.001, duration);
        double safeWidth = Math.Max(1, timelineWidth);
        double startEdge = Math.Clamp(selectionStart / safeDuration * safeWidth, 0, safeWidth);
        double endEdge = Math.Clamp(selectionEnd / safeDuration * safeWidth, 0, safeWidth);
        if (endEdge < startEdge)
            endEdge = startEdge;

        double maxHandleLeft = Math.Max(0, safeWidth - handleWidth);

        // Handles sit in the unselected/shaded region outside the active selection
        // without covering video, but stay clamped within [0, maxHandleLeft]
        double startThumbLeft = Math.Clamp(startEdge - handleWidth, 0, maxHandleLeft);
        double endThumbLeft = Math.Clamp(endEdge, 0, maxHandleLeft);

        // Prevent thumbs from crossing or overlapping each other
        if (safeWidth >= handleWidth * 2)
        {
            if (endThumbLeft < startThumbLeft + handleWidth)
            {
                if (startThumbLeft <= 0)
                    endThumbLeft = startThumbLeft + handleWidth;
                else
                    startThumbLeft = Math.Max(0, endThumbLeft - handleWidth);
            }
        }
        else if (endThumbLeft < startThumbLeft)
        {
            endThumbLeft = startThumbLeft;
        }

        double playhead = Math.Clamp(playbackPosition / safeDuration * safeWidth, 0, safeWidth);
        double playheadHalf = playheadWidth / 2;
        double playheadThumbLeft = Math.Clamp(
            playhead - playheadHalf,
            -playheadHalf,
            safeWidth - playheadHalf);

        bool selectionHasLeftOuterRound = startEdge <= 0.5;
        bool selectionHasRightOuterRound = endEdge >= safeWidth - 0.5;

        return new TimelineVisualState(
            StartThumbLeft: startThumbLeft,
            EndThumbLeft: endThumbLeft,
            LeftShadeLeft: 0,
            LeftShadeWidth: Math.Max(0, startEdge),
            RightShadeLeft: endEdge,
            RightShadeWidth: Math.Max(0, safeWidth - endEdge),
            SelectionBorderLeft: startEdge,
            SelectionBorderWidth: Math.Max(0, endEdge - startEdge),
            PlayheadThumbLeft: playheadThumbLeft,
            SelectionHasLeftOuterRound: selectionHasLeftOuterRound,
            SelectionHasRightOuterRound: selectionHasRightOuterRound);
    }
}
