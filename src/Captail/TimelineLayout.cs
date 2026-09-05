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
    double PlayheadThumbLeft);

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

        double maxHandleLeft = Math.Max(0, safeWidth - handleWidth);
        double startThumbLeft = Math.Clamp(startEdge, 0, maxHandleLeft);
        double endThumbLeft = Math.Clamp(endEdge - handleWidth, 0, maxHandleLeft);

        if (endThumbLeft < startThumbLeft)
            endThumbLeft = startThumbLeft;

        double playhead = Math.Clamp(playbackPosition / safeDuration * safeWidth, 0, safeWidth);
        double playheadHalf = playheadWidth / 2;
        double playheadThumbLeft = Math.Clamp(
            playhead - playheadHalf,
            -playheadHalf,
            safeWidth - playheadHalf);

        return new TimelineVisualState(
            StartThumbLeft: startThumbLeft,
            EndThumbLeft: endThumbLeft,
            LeftShadeLeft: 0,
            LeftShadeWidth: Math.Max(0, startEdge),
            RightShadeLeft: endEdge,
            RightShadeWidth: Math.Max(0, safeWidth - endEdge),
            SelectionBorderLeft: startEdge,
            SelectionBorderWidth: Math.Max(0, endEdge - startEdge),
            PlayheadThumbLeft: playheadThumbLeft);
    }
}
