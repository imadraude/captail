using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Captail.Interop;

namespace Captail;

public enum ReplayIndicatorState
{
    Active,
    Recovering,
    Error,
    Saved,
    Recording,
}

public enum ReplayIndicatorPlacement
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public partial class ReplayStatusIndicatorWindow : Window
{
    internal const int BaseWindowSize = 18;
    internal const int BaseEdgeInset = 8;
    internal const int BaseIndicatorGap = 4;
    internal const int MultiIndicatorOffset = BaseWindowSize + BaseIndicatorGap;
    internal const double BaseIndicatorOpacity = 0.75;

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint WdaNone = 0x00000000;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint GwOwner = 4;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _captureAffinityTimer;
    private readonly DispatcherTimer _transientTimer;
    private ReplayIndicatorState? _state;
    private ReplayIndicatorPlacement _placement = ReplayIndicatorPlacement.TopRight;
    private ReplayIndicatorState _resumeState = ReplayIndicatorState.Active;
    private bool _transientActive;
    private bool _allowClose;
    private bool _captureAffinityFailureLogged;
    private uint? _captureAffinity;
    private uint _lastForegroundProcessId;
    private bool _lastForegroundIsScreenCapture;
    private string _targetGameExecutable = "";
    private nint _targetGameHwnd;
    private bool _gameDetected;
    private int _inwardOffset;
    private bool _firstFrameRendered;
    private int _lastLeft = int.MinValue;
    private int _lastTop = int.MinValue;
    private int _lastWidth = int.MinValue;
    private int _lastHeight = int.MinValue;
#if DEBUG
    internal bool AllowCaptureForQa { get; set; }
#endif

    internal ReplayStatusIndicatorWindow()
    {
        InitializeComponent();
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750),
        };
        _positionTimer.Tick += (_, _) => PositionOnForegroundMonitor();
        _captureAffinityTimer = new DispatcherTimer
        {
            // Fast enough to notice the Windows snipping overlay before its
            // capture, without waking the UI thread ten times per second.
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _captureAffinityTimer.Tick += (_, _) => UpdateCaptureAffinity();
        _transientTimer = new DispatcherTimer();
        _transientTimer.Tick += (_, _) => ResumeAfterTransient();
        SourceInitialized += (_, _) => ConfigureWindow();
        ContentRendered += (_, _) => CompleteFirstFrame();
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                HideIndicator();
            }
        };
    }

    internal void SetState(ReplayIndicatorState state)
    {
        if (_transientActive && state == _resumeState)
            return;

        _transientTimer.Stop();
        _transientActive = false;
        ApplyState(state);
        ShowIndicator();
    }

    internal void SetPlacement(string placement)
    {
        ReplayIndicatorPlacement normalized = placement switch
        {
            "top-left" => ReplayIndicatorPlacement.TopLeft,
            "bottom-left" => ReplayIndicatorPlacement.BottomLeft,
            "bottom-right" => ReplayIndicatorPlacement.BottomRight,
            _ => ReplayIndicatorPlacement.TopRight,
        };
        if (_placement == normalized)
            return;

        _placement = normalized;
        if (IsVisible)
            PositionOnForegroundMonitor();
    }

    internal void SetTargetGame(string? gameExecutable)
    {
        string normalized = (gameExecutable ?? "").Trim();
        if (string.Equals(_targetGameExecutable, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        _targetGameExecutable = normalized;
        _gameDetected = !string.IsNullOrEmpty(normalized);
        _targetGameHwnd = 0;
        if (IsVisible)
            PositionOnForegroundMonitor();
    }

    internal void SetGameDetected(bool gameDetected)
    {
        if (string.IsNullOrEmpty(_targetGameExecutable))
        {
            if (_gameDetected == gameDetected)
                return;
            _gameDetected = gameDetected;
        }
        else if (!gameDetected)
        {
            _targetGameExecutable = "";
            _gameDetected = false;
            _targetGameHwnd = 0;
        }

        if (IsVisible)
            PositionOnForegroundMonitor();
    }

    internal void SetInwardOffset(int offset)
    {
        int normalized = Math.Max(0, offset);
        if (_inwardOffset == normalized)
            return;

        _inwardOffset = normalized;
        if (IsVisible)
            PositionOnForegroundMonitor();
    }

    internal void ShowTransient(
        ReplayIndicatorState state,
        ReplayIndicatorState resumeState,
        int durationMilliseconds)
    {
        _resumeState = resumeState;
        _transientActive = true;
        ApplyState(state, force: true);
        ShowIndicator();
        _transientTimer.Stop();
        _transientTimer.Interval = TimeSpan.FromMilliseconds(durationMilliseconds);
        _transientTimer.Start();
    }

    internal void HideIndicator()
    {
        _transientTimer.Stop();
        _positionTimer.Stop();
        _captureAffinityTimer.Stop();
        _transientActive = false;
        _state = null;
        _targetGameHwnd = 0;
        _lastLeft = int.MinValue;
        _lastTop = int.MinValue;
        _lastWidth = int.MinValue;
        _lastHeight = int.MinValue;
        StopAnimations();
        Hide();
    }

    internal void ClosePermanently()
    {
        _allowClose = true;
        _transientTimer.Stop();
        _positionTimer.Stop();
        _captureAffinityTimer.Stop();
        Close();
    }

    private void ShowIndicator()
    {
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, BaseIndicatorOpacity, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut,
                    },
                });
        }
        PositionOnForegroundMonitor();
        _positionTimer.Start();
        _captureAffinityTimer.Start();
    }

    private void ApplyState(ReplayIndicatorState state, bool force = false)
    {
        if (!force && _state == state)
            return;

        _state = state;
        StopAnimations();
        SavedGlyph.Visibility = Visibility.Collapsed;
        ErrorGlyph.Visibility = Visibility.Collapsed;
        CenterDot.Visibility = Visibility.Visible;
        IndicatorRoot.Opacity = 1;

        Color accent = state switch
        {
            ReplayIndicatorState.Recording => Color.FromRgb(255, 95, 99),
            ReplayIndicatorState.Recovering => Color.FromRgb(242, 194, 66),
            ReplayIndicatorState.Error => Color.FromRgb(255, 95, 99),
            _ => Color.FromRgb(99, 224, 189),
        };
        var brush = new SolidColorBrush(accent);
        StateRing.Stroke = brush;
        CenterDot.Fill = brush;

        switch (state)
        {
            case ReplayIndicatorState.Recording:
                StateRing.StrokeDashArray = new DoubleCollection([5, 2.5]);
                break;
            case ReplayIndicatorState.Active:
                StateRing.StrokeDashArray = new DoubleCollection([5.5, 2.5]);
                break;
            case ReplayIndicatorState.Recovering:
                StateRing.StrokeDashArray = new DoubleCollection([1.8, 2]);
                break;
            case ReplayIndicatorState.Error:
                CenterDot.Visibility = Visibility.Collapsed;
                ErrorGlyph.Visibility = Visibility.Visible;
                StateRing.StrokeDashArray = new DoubleCollection([1, 1.8]);
                break;
            case ReplayIndicatorState.Saved:
                CenterDot.Visibility = Visibility.Collapsed;
                SavedGlyph.Visibility = Visibility.Visible;
                SavedGlyph.Stroke = brush;
                StateRing.StrokeDashArray = null;
                IndicatorScale.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    PulseAnimation());
                IndicatorScale.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    PulseAnimation());
                break;
        }
    }

    private static DoubleAnimation PulseAnimation() =>
        new(0.78, 1, TimeSpan.FromMilliseconds(230))
        {
            EasingFunction = new BackEase
            {
                Amplitude = 0.22,
                EasingMode = EasingMode.EaseOut,
            },
        };

    private void StopAnimations()
    {
        StateRing.BeginAnimation(OpacityProperty, null);
        StateRing.Opacity = 1;
        CenterDot.BeginAnimation(OpacityProperty, null);
        CenterDot.Opacity = 1;
        IndicatorRoot.BeginAnimation(OpacityProperty, null);
        IndicatorRoot.Opacity = 1;
        IndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        IndicatorScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        IndicatorScale.ScaleX = 1;
        IndicatorScale.ScaleY = 1;
    }

    private void ResumeAfterTransient()
    {
        _transientTimer.Stop();
        _transientActive = false;
        ApplyState(_resumeState, force: true);
    }

    private void ConfigureWindow()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        Marshal.SetLastPInvokeError(0);
        int styles = GetWindowLong(hwnd, GwlExStyle);
        int error = Marshal.GetLastPInvokeError();
        if (styles == 0 && error != 0)
        {
            Log.Write($"Could not read recording indicator style: Win32 error {error}.");
            return;
        }

        Marshal.SetLastPInvokeError(0);
        int previousStyles = SetWindowLong(
            hwnd,
            GwlExStyle,
            styles | WsExTransparent | WsExToolWindow | WsExNoActivate);
        error = Marshal.GetLastPInvokeError();
        if (previousStyles == 0 && error != 0)
            Log.Write($"Could not make recording indicator click-through: Win32 error {error}.");

        PositionOnForegroundMonitor();
    }
    private void CompleteFirstFrame()
    {
        if (_firstFrameRendered)
            return;

        _firstFrameRendered = true;
        BeginAnimation(OpacityProperty, null);
        Opacity = BaseIndicatorOpacity;
        if (_state is ReplayIndicatorState state)
            ApplyState(state, force: true);
        InvalidateVisual();
        UpdateLayout();
        UpdateCaptureAffinity();
    }

    private void UpdateCaptureAffinity()
    {
        if (!_firstFrameRendered)
            return;

        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
            return;

#if DEBUG
        if (AllowCaptureForQa)
            return;
#endif

        SetCaptureAffinity(
            hwnd,
            IsScreenCaptureForeground() ? WdaNone : WdaExcludeFromCapture);
    }

    private bool IsScreenCaptureForeground()
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0)
            return false;

        _ = GetWindowThreadProcessId(foreground, out uint processId);
        if (processId == 0)
            return false;
        if (processId == _lastForegroundProcessId)
            return _lastForegroundIsScreenCapture;

        _lastForegroundProcessId = processId;
        try
        {
            if (CaptureInterop.TryGetProcessImageInfo(processId, out string executable, out _))
            {
                string processName = Path.GetFileNameWithoutExtension(executable);
                _lastForegroundIsScreenCapture = IsScreenCaptureProcessName(processName);
                return _lastForegroundIsScreenCapture;
            }

            using Process process = Process.GetProcessById((int)processId);
            string fallbackName = process.ProcessName;
            _lastForegroundIsScreenCapture = IsScreenCaptureProcessName(fallbackName);
        }
        catch (ArgumentException)
        {
            _lastForegroundIsScreenCapture = false;
        }
        catch (InvalidOperationException)
        {
            _lastForegroundIsScreenCapture = false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _lastForegroundIsScreenCapture = false;
        }

        return _lastForegroundIsScreenCapture;
    }

    private static bool IsScreenCaptureProcessName(string processName) =>
        processName.Equals("SnippingTool", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("ScreenClippingHost", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("ScreenSketch", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("SnipAndSketch", StringComparison.OrdinalIgnoreCase);

    private void SetCaptureAffinity(nint hwnd, uint affinity)
    {
        if (_captureAffinity == affinity)
            return;

        if (SetWindowDisplayAffinity(hwnd, affinity))
        {
            _captureAffinity = affinity;
            return;
        }
        if (_captureAffinityFailureLogged)
            return;

        _captureAffinityFailureLogged = true;
        Log.Write(
            $"Could not update recording indicator capture affinity: Win32 error " +
            $"{Marshal.GetLastWin32Error()}.");
    }

    private void PositionOnForegroundMonitor()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
            return;

        nint gameHwnd = !string.IsNullOrWhiteSpace(_targetGameExecutable)
            ? ResolveTargetGameWindow(_targetGameExecutable)
            : 0;

        nint targetWindow = gameHwnd != 0 ? gameHwnd : GetForegroundWindow();
        nint monitor = MonitorFromWindow(
            targetWindow != 0 ? targetWindow : hwnd,
            MonitorDefaultToNearest);
        if (monitor == 0)
        {
            monitor = MonitorFromWindow(
                hwnd,
                MonitorDefaultToPrimary);
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            return;

        uint dpi = GetDpiForWindow(targetWindow != 0 ? targetWindow : hwnd);
        double scale = dpi > 0 ? dpi / 96d : 1d;
        int size = (int)Math.Round(BaseWindowSize * scale);
        int inset = (int)Math.Round(BaseEdgeInset * scale);
        int inwardOffset = (int)Math.Round(_inwardOffset * scale);

        Rect gameRect = default;
        bool hasGameRect = gameHwnd != 0 &&
            GetWindowRect(gameHwnd, out gameRect) &&
            !IsIconic(gameHwnd);

        Rect bounds = CalculateIndicatorBounds(
            _gameDetected,
            hasGameRect,
            info.Monitor,
            info.WorkArea,
            gameRect);

        (int left, int top) = CalculateIndicatorPosition(
            bounds,
            size,
            inset,
            inwardOffset,
            _placement);

        if (left == _lastLeft &&
            top == _lastTop &&
            size == _lastWidth &&
            size == _lastHeight)
        {
            return;
        }

        // Preserve current topmost-band order. Raising the window on every
        // timer tick would cover newer system overlays such as Snipping Tool.
        if (SetWindowPos(
            hwnd,
            0,
            left,
            top,
            size,
            size,
            SwpNoActivate | SwpNoZOrder))
        {
            _lastLeft = left;
            _lastTop = top;
            _lastWidth = size;
            _lastHeight = size;
        }
    }

    internal static Rect CalculateIndicatorBounds(
        bool gameDetected,
        bool hasTargetGameWindow,
        Rect monitorRect,
        Rect workAreaRect,
        Rect gameWindowRect)
    {
        if (hasTargetGameWindow &&
            !gameWindowRect.IsEmpty &&
            (gameWindowRect.Right - gameWindowRect.Left) > 100 &&
            (gameWindowRect.Bottom - gameWindowRect.Top) > 100)
        {
            bool coversMonitor =
                gameWindowRect.Left <= monitorRect.Left + 4 &&
                gameWindowRect.Top <= monitorRect.Top + 4 &&
                gameWindowRect.Right >= monitorRect.Right - 4 &&
                gameWindowRect.Bottom >= monitorRect.Bottom - 4;

            return coversMonitor ? monitorRect : gameWindowRect;
        }

        return gameDetected ? monitorRect : workAreaRect;
    }

    internal static (int Left, int Top) CalculateIndicatorPosition(
        Rect bounds,
        int size,
        int inset,
        int inwardOffset,
        ReplayIndicatorPlacement placement)
    {
        bool placeRight = placement is
            ReplayIndicatorPlacement.TopRight or
            ReplayIndicatorPlacement.BottomRight;
        bool placeBottom = placement is
            ReplayIndicatorPlacement.BottomLeft or
            ReplayIndicatorPlacement.BottomRight;

        int left = placeRight
            ? bounds.Right - size - inset - inwardOffset
            : bounds.Left + inset + inwardOffset;
        int top = placeBottom
            ? bounds.Bottom - size - inset
            : bounds.Top + inset;

        return (left, top);
    }

    private nint ResolveTargetGameWindow(string targetExecutable)
    {
        if (string.IsNullOrWhiteSpace(targetExecutable))
            return 0;

        string targetName = Path.GetFileName(targetExecutable);

        nint foreground = GetForegroundWindow();
        if (foreground != 0 && IsWindowValidForGame(foreground, targetName))
        {
            _targetGameHwnd = foreground;
            return foreground;
        }

        if (_targetGameHwnd != 0 && IsWindowValidForGame(_targetGameHwnd, targetName))
        {
            return _targetGameHwnd;
        }

        nint found = 0;
        EnumWindows((candidateHwnd, _) =>
        {
            if (IsWindowValidForGame(candidateHwnd, targetName))
            {
                found = candidateHwnd;
                return false;
            }
            return true;
        }, 0);

        if (found != 0)
        {
            _targetGameHwnd = found;
            return found;
        }

        return _targetGameHwnd != 0 && IsWindow(_targetGameHwnd) ? _targetGameHwnd : 0;
    }

    private static bool IsWindowValidForGame(nint hwnd, string targetName)
    {
        if (hwnd == 0 || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd))
            return false;

        if (GetWindow(hwnd, GwOwner) != 0)
            return false;

        if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0)
            return false;

        if (!CaptureInterop.TryGetProcessImageInfo(pid, out string exe, out _))
            return false;

        if (!IsExecutableMatch(exe, targetName))
            return false;

        if (!GetWindowRect(hwnd, out Rect rect))
            return false;

        return (rect.Right - rect.Left) > 100 && (rect.Bottom - rect.Top) > 100;
    }

    internal static bool IsExecutableMatch(string? candidatePathOrExe, string? targetPathOrExe)
    {
        if (string.IsNullOrWhiteSpace(candidatePathOrExe) || string.IsNullOrWhiteSpace(targetPathOrExe))
            return false;

        string candidateClean = Path.GetFileNameWithoutExtension(candidatePathOrExe.Trim());
        string targetClean = Path.GetFileNameWithoutExtension(targetPathOrExe.Trim());
        return string.Equals(candidateClean, targetClean, StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect : IEquatable<Rect>
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
        internal int Width => Right - Left;
        internal int Height => Bottom - Top;

        public bool Equals(Rect other) =>
            Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

        public override bool Equals(object? obj) => obj is Rect r && Equals(r);
        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        internal int Size;
        internal Rect Monitor;
        internal Rect WorkArea;
        internal uint Flags;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint cmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint hwnd,
        out uint processId);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
