using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Captail;

public partial class SettingsWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FixAboutPopupInteraction();
        ApplyDashboardVisualPolish();
    }

    private void FixAboutPopupInteraction()
    {
        // Popup owns its own HWND. Closing it from the parent window's
        // Deactivated/PreviewMouseDown handlers can happen before a child
        // Button receives Click, which makes the About actions appear inert.
        // Let Popup handle outside-click dismissal itself instead.
        Deactivated -= Window_Deactivated;
        PreviewMouseDown -= Window_PreviewMouseDown;
        AboutPopup.StaysOpen = false;
    }

    private void ApplyDashboardVisualPolish()
    {
        // Recording is a primary Captail state, so use the same coral accent
        // as the rest of the 0.7 UI instead of the danger/error color.
        if (FindResource("AccentBrush") is Brush accentBrush)
        {
            RecordDot.Fill = accentBrush;
            RecordSquare.Fill = accentBrush;
        }

        foreach (Shape shape in new Shape[]
                 {
                     StatusRing,
                     StatusDot,
                     RecordDot,
                     RecordSquare,
                     SystemSourceDot,
                     MicSourceDot,
                 })
        {
            shape.SnapsToDevicePixels = true;
            shape.UseLayoutRounding = true;
        }

        ConfigureSourceStatusDot(SystemSourceChip, SystemSourceDot);
        ConfigureSourceStatusDot(MicSourceChip, MicSourceDot);
    }

    private void ConfigureSourceStatusDot(ToggleButton source, Ellipse dot)
    {
        // Keep both source markers on the same six-pixel geometry and make
        // inactive sources visibly secondary instead of leaving a stray
        // coral dot in an unchecked card.
        dot.Width = 6;
        dot.Height = 6;
        dot.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        dot.VerticalAlignment = System.Windows.VerticalAlignment.Center;

        void RefreshDot() => UpdateSourceStatusDot(source, dot);

        source.Checked += (_, _) => RefreshDot();
        source.Unchecked += (_, _) => RefreshDot();
        source.IsEnabledChanged += (_, _) => RefreshDot();
        RefreshDot();
    }

    private void UpdateSourceStatusDot(ToggleButton source, Ellipse dot)
    {
        bool active = source.IsEnabled && source.IsChecked == true;
        string brushKey = active ? "AccentBrush" : "RingIdleBrush";
        if (FindResource(brushKey) is Brush brush)
            dot.Fill = brush;

        dot.Opacity = active ? 1.0 : 0.58;
    }
}
