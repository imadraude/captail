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
    private static readonly Geometry CheckmarkGeometry = Geometry.Parse("M 2.8 7.2 L 5.8 10.2 L 11.2 3.8");
    private static readonly Geometry CrossGeometry = Geometry.Parse("M 3.6 3.6 L 10.4 10.4 M 10.4 3.6 L 3.6 10.4");
    private static readonly Geometry WarningGeometry = Geometry.Parse("M 7 1.8 L 12.4 11.6 C 12.7 12.1 12.3 12.8 11.7 12.8 L 2.3 12.8 C 1.7 12.8 1.3 12.1 1.6 11.6 Z M 7 5.4 L 7 8.6 M 7 10.8 L 7 11.0");
    private static readonly Geometry InfoGeometry = Geometry.Parse("M 7 3.2 L 7 3.4 M 7 5.8 L 7 10.8");
    private static readonly Geometry DotGeometry = Geometry.Parse("M 7 3.4 A 3.6 3.6 0 1 0 7.01 3.4 Z");
    private static readonly Geometry SquareGeometry = Geometry.Parse("M 5 3.6 H 9 C 9.8 3.6 10.4 4.2 10.4 5 V 9 C 10.4 9.8 9.8 10.4 9 10.4 H 5 C 4.2 10.4 3.6 9.8 3.6 9 V 5 C 3.6 4.2 4.2 3.6 5 3.6 Z");

    static OverlayNotificationWindow()
    {
        CheckmarkGeometry.Freeze();
        CrossGeometry.Freeze();
        WarningGeometry.Freeze();
        InfoGeometry.Freeze();
        DotGeometry.Freeze();
        SquareGeometry.Freeze();
    }

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

        ApplyIcon(glyph, accent, tone);
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

    private void ApplyIcon(string? glyph, Brush accent, OverlayTone tone)
    {
        string normalized = (glyph ?? string.Empty).Trim();

        if (normalized is "✓" or "✔" or "check" or "success")
        {
            SetVectorIcon(CheckmarkGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.7);
            return;
        }

        if (normalized is "✕" or "✖" or "x" or "X" or "error")
        {
            SetVectorIcon(CrossGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.7);
            return;
        }

        if (normalized is "⚠" or "!" or "warning" or "alert")
        {
            SetVectorIcon(WarningGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.35);
            return;
        }

        if (normalized is "ℹ" or "i" or "info")
        {
            SetVectorIcon(InfoGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.7);
            return;
        }

        if (normalized is "●" or "•" or "dot" or "record" or "live")
        {
            SetVectorIcon(DotGeometry, stroke: Brushes.Transparent, fill: accent, strokeThickness: 0);
            return;
        }

        if (normalized is "■" or "stop" or "square")
        {
            SetVectorIcon(SquareGeometry, stroke: Brushes.Transparent, fill: accent, strokeThickness: 0);
            return;
        }

        if (string.IsNullOrEmpty(normalized))
        {
            switch (tone)
            {
                case OverlayTone.Success:
                    SetVectorIcon(CheckmarkGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.7);
                    break;
                case OverlayTone.Warning:
                    SetVectorIcon(WarningGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.35);
                    break;
                case OverlayTone.Error:
                    SetVectorIcon(CrossGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.7);
                    break;
                default:
                    SetVectorIcon(InfoGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.7);
                    break;
            }
            return;
        }

        IconPath.Visibility = Visibility.Collapsed;
        IconText.Visibility = Visibility.Visible;
        IconText.Text = normalized;
        IconText.Foreground = accent;
    }

    private void SetVectorIcon(Geometry geometry, Brush stroke, Brush fill, double strokeThickness)
    {
        IconText.Visibility = Visibility.Collapsed;
        IconPath.Visibility = Visibility.Visible;
        IconPath.Data = geometry;
        IconPath.Stroke = stroke;
        IconPath.Fill = fill;
        IconPath.StrokeThickness = strokeThickness;
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
