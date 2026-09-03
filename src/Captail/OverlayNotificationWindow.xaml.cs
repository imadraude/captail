using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Captail;

public enum OverlayTone
{
    Success,
    Neutral,
    Warning,
    Error,
}

public partial class OverlayNotificationWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly DispatcherTimer _hideTimer;
    private readonly TranslateTransform _translate = new();
    private bool _allowClose;
    // Bumped on every ShowNotification so a stale hide/fade from a previous
    // notification cannot dismiss the one currently on screen.
    private long _sequence;

    public OverlayNotificationWindow()
    {
        InitializeComponent();
        Card.RenderTransform = _translate;
        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => HideAnimated();
        SourceInitialized += (_, _) => MakeClickThrough();
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void ShowNotification(
        string glyph,
        string title,
        string detail,
        OverlayTone tone,
        int durationMilliseconds = 3200)
    {
        Brush accent = new SolidColorBrush(tone switch
        {
            OverlayTone.Warning => Color.FromRgb(224, 179, 99),
            OverlayTone.Error => Color.FromRgb(224, 130, 99),
            OverlayTone.Neutral => Color.FromRgb(148, 163, 171),
            _ => Color.FromRgb(99, 224, 189),
        });
        Brush accentSurface = new SolidColorBrush(tone switch
        {
            OverlayTone.Warning => Color.FromArgb(34, 224, 179, 99),
            OverlayTone.Error => Color.FromArgb(34, 224, 130, 99),
            OverlayTone.Neutral => Color.FromArgb(30, 148, 163, 171),
            _ => Color.FromArgb(32, 99, 224, 189),
        });
        Brush accentRing = new SolidColorBrush(tone switch
        {
            OverlayTone.Warning => Color.FromArgb(128, 224, 179, 99),
            OverlayTone.Error => Color.FromArgb(128, 224, 130, 99),
            OverlayTone.Neutral => Color.FromArgb(112, 148, 163, 171),
            _ => Color.FromArgb(128, 99, 224, 189),
        });

        _sequence++;

        IconText.Text = glyph;
        IconText.Foreground = accent;
        IconSurface.Fill = accentSurface;
        IconRing.Stroke = accentRing;
        LifeBar.Background = accent;
        TitleText.Text = title;
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrWhiteSpace(detail)
            ? Visibility.Collapsed
            : Visibility.Visible;

        // Card keeps 8 px of transparent room for its shadow. Position the
        // visible card, rather than the layered window, 16 px from the edge.
        Left = SystemParameters.WorkArea.Right - Width - 8;
        Top = SystemParameters.WorkArea.Top + 14;
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(durationMilliseconds);
        _hideTimer.Start();

        if (!IsVisible)
            Show();

        Opacity = 0;
        _translate.X = 14;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        _translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        LifeScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(durationMilliseconds)));
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    private void HideAnimated()
    {
        _hideTimer.Stop();
        long token = _sequence;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150));
        fade.Completed += (_, _) =>
        {
            // A newer notification may have appeared during the fade — don't hide it.
            if (token == _sequence)
                Hide();
        };
        BeginAnimation(OpacityProperty, fade);
        _translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, 10, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });
    }

    private void MakeClickThrough()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        Marshal.SetLastPInvokeError(0);
        int styles = GetWindowLong(hwnd, GwlExStyle);
        int error = Marshal.GetLastPInvokeError();
        if (styles == 0 && error != 0)
        {
            Log.Write($"Could not read overlay window style: Win32 error {error}.");
            return;
        }

        Marshal.SetLastPInvokeError(0);
        int previousStyles = SetWindowLong(hwnd, GwlExStyle,
            styles | WsExTransparent | WsExToolWindow | WsExNoActivate);
        error = Marshal.GetLastPInvokeError();
        if (previousStyles == 0 && error != 0)
            Log.Write($"Could not make overlay click-through: Win32 error {error}.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hwnd, int index, int newStyle);
}
