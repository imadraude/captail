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
    private readonly Geometry _checkmarkGeometry;
    private readonly Geometry _crossGeometry;
    private readonly Geometry _warningGeometry;
    private readonly Geometry _infoGeometry;
    private readonly Geometry _recordGeometry;
    private readonly Geometry _stopGeometry;
    private readonly Geometry _reloadGeometry;

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
        _checkmarkGeometry = (Geometry)FindResource("IconCheck");
        _crossGeometry = (Geometry)FindResource("IconClose");
        _warningGeometry = (Geometry)FindResource("IconWarning");
        _infoGeometry = (Geometry)FindResource("IconInfo");
        _recordGeometry = (Geometry)FindResource("IconRecord");
        _stopGeometry = (Geometry)FindResource("IconStop");
        _reloadGeometry = (Geometry)FindResource("IconReload");
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
        _sequence++;
        ApplyPresentation(glyph, title, detail, tone);

        // Card keeps 8 px of transparent room for its shadow. Position the
        // visible card, rather than the layered window, 16 px from the edge.
        Left = SystemParameters.WorkArea.Right - Width - 8;
        Top = SystemParameters.WorkArea.Top + 14;
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(durationMilliseconds);
        _hideTimer.Start();

        if (!IsVisible)
            Show();

        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            _translate.X = 0;
            LifeScale.ScaleX = 1;
            return;
        }

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

    internal void PrepareSnapshot(string glyph, string title, OverlayTone tone) =>
        ApplyPresentation(glyph, title, "Overlay icon QA", tone);

    private void ApplyPresentation(string glyph, string title, string detail, OverlayTone tone)
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

        ApplyIcon(glyph, accent, tone);
        IconSurface.Fill = accentSurface;
        IconRing.Stroke = accentRing;
        LifeBar.Background = accent;
        TitleText.Text = title;
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrWhiteSpace(detail)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    private void HideAnimated()
    {
        _hideTimer.Stop();
        if (!SystemParameters.ClientAreaAnimation)
        {
            Hide();
            return;
        }
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
            SetVectorIcon(_checkmarkGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.8);
            return;
        }

        if (normalized is "✕" or "✖" or "x" or "X" or "error")
        {
            SetVectorIcon(_crossGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.8);
            return;
        }

        if (normalized is "⚠" or "!" or "warning" or "alert")
        {
            SetVectorIcon(_warningGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.65);
            return;
        }

        if (normalized is "ℹ" or "i" or "info")
        {
            SetVectorIcon(_infoGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.8);
            return;
        }

        if (normalized is "●" or "•" or "dot" or "record" or "live")
        {
            SetVectorIcon(_recordGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.6);
            return;
        }

        if (normalized is "■" or "stop" or "square")
        {
            SetVectorIcon(_stopGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.6);
            return;
        }

        if (normalized is "⟳" or "↻" or "reload" or "sync" or "saving" or "recovering")
        {
            SetVectorIcon(_reloadGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.8);
            return;
        }

        if (string.IsNullOrEmpty(normalized))
        {
            switch (tone)
            {
                case OverlayTone.Success:
                    SetVectorIcon(_checkmarkGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.8);
                    break;
                case OverlayTone.Warning:
                    SetVectorIcon(_warningGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.65);
                    break;
                case OverlayTone.Error:
                    SetVectorIcon(_crossGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.8);
                    break;
                default:
                    SetVectorIcon(_infoGeometry, stroke: accent, fill: Brushes.Transparent, strokeThickness: 1.8);
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
