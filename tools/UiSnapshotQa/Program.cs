using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captail;

namespace Captail.UiSnapshotQa;

internal static class Program
{
    private static readonly double[] Scales = [1.0, 1.25, 1.5, 2.0];

    [STAThread]
    private static int Main(string[] args)
    {
        string outputDirectory = Path.GetFullPath(
            args.FirstOrDefault() ?? Path.Combine("artifacts", "ui-snapshots"));
        Directory.CreateDirectory(outputDirectory);

        var application = new Application();
        foreach (string resource in new[]
        {
            "Languages/Strings.en.xaml",
            "Themes/Theme.xaml",
            "Themes/SettingsFixes.xaml",
        })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/Captail;component/{resource}",
                    UriKind.Absolute),
            });
        }

        var config = new Config
        {
            Language = "en",
            Codec = "av1",
            RecordingResolution = "2160p",
            FrameRate = 240,
            BitrateMbps = 80,
            BufferSeconds = 600,
            ReplayEnabled = true,
            CaptureSystemAudio = true,
            CaptureMicrophone = true,
            AudioRoutingMode = "advanced",
            OutputDirectory = @"D:\Captail\Replays",
        };

        var settings = CreateSettings(config);
        CaptureWindow(settings, "main", outputDirectory);

        settings.DashboardPanel.Visibility = Visibility.Collapsed;
        settings.SettingsPanel.Visibility = Visibility.Visible;
        settings.SettingsButton.Visibility = Visibility.Collapsed;
        settings.CancelSettingsButton.Visibility = Visibility.Visible;
        settings.DoneButton.Visibility = Visibility.Visible;
        PrepareWindowContent(settings);
        settings.SettingsScrollViewer.ScrollToVerticalOffset(790);
        PrepareWindowContent(settings);
        CaptureWindow(settings, "settings-video", outputDirectory);
        settings.SettingsScrollViewer.ScrollToVerticalOffset(1260);
        PrepareWindowContent(settings);
        CaptureWindow(settings, "settings-audio", outputDirectory);

        var routes = new[]
        {
            new ProcessAudioRoute { Executable = @"C:\Games\ExampleGame.exe", Track = 1 },
            new ProcessAudioRoute { Executable = @"C:\Apps\Discord.exe", Track = 2 },
            new ProcessAudioRoute { Executable = @"C:\Apps\Music.exe", Track = 3 },
        };
        var routing = new ProcessAudioRoutingWindow(
            routes,
            microphoneTrack: 2,
            microphoneEnabled: true,
            AudioRoutingFormatCapabilities.For("opus"));
        CaptureWindow(routing, "audio-routing", outputDirectory);

        string scratch = Path.Combine(outputDirectory, "scratch");
        Directory.CreateDirectory(scratch);
        var library = new ReplayLibrary(new FfmpegAdapter(), scratch);
        var clip = new ReplayClip(
            Path.Combine(scratch, "example.mkv"),
            "Ranked comeback",
            "Example Game",
            DateTime.Today.AddHours(20).AddMinutes(41),
            238_000_000,
            TimeSpan.FromMinutes(5),
            null);
        var player = new ClipEditorWindow(
            library, scratch, clip, _ => { }, ClipWindowMode.Preview);
        CaptureWindow(player, "player", outputDirectory);
        var editor = new ClipEditorWindow(
            library, scratch, clip, _ => { }, ClipWindowMode.Trim);
        CaptureWindow(editor, "editor", outputDirectory);

        var indicator = new ReplayStatusIndicatorWindow();
        CaptureWindow(indicator, "indicator-active", outputDirectory);
        indicator.StateRing.Visibility = Visibility.Collapsed;
        indicator.CenterDot.Visibility = Visibility.Collapsed;
        indicator.SavedGlyph.Visibility = Visibility.Visible;
        CaptureWindow(indicator, "indicator-saved", outputDirectory);
        indicator.SavedGlyph.Visibility = Visibility.Collapsed;
        indicator.ErrorGlyph.Visibility = Visibility.Visible;
        CaptureWindow(indicator, "indicator-error", outputDirectory);

        CaptureIconCatalog(outputDirectory);
        Directory.Delete(scratch, recursive: true);
        Console.WriteLine(outputDirectory);
        return 0;
    }

    private static SettingsWindow CreateSettings(Config config) =>
        new(
            config,
            runtimeActive: true,
            saveReplay: () => { },
            setReplayEnabled: _ => Task.FromResult(true),
            setAudioSources: (_, _, _, _) => Task.FromResult(true),
            setAdvancedAudioSourceEnabled: (_, _) => Task.FromResult(true),
            applySettings: (_, _) => Task.FromResult(true),
            EncoderCapabilities.Preview(),
            AdvancedProcessAudioAvailability.Available,
            checkForUpdates: (_, _) => Task.FromResult<UpdateRelease?>(null),
            installUpdate: (_, _, _) => Task.CompletedTask);

    private static void CaptureWindow(Window window, string name, string outputDirectory)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        PrepareWindowContent(window);
        if (window.Content is not FrameworkElement content)
            throw new InvalidOperationException($"{window.GetType().Name} has no renderable content.");

        foreach (double scale in Scales)
        {
            string suffix = $"{scale * 100:0}";
            Render(content, Path.Combine(outputDirectory, $"{name}-{suffix}.png"), scale);
        }
    }

    private static void PrepareWindowContent(Window window)
    {
        if (window.Content is not FrameworkElement content)
            throw new InvalidOperationException($"{window.GetType().Name} has no renderable content.");
        content.Measure(new Size(window.Width, window.Height));
        content.Arrange(new Rect(0, 0, window.Width, window.Height));
        content.UpdateLayout();
    }

    private static void Prepare(FrameworkElement element)
    {
        double width = element is Window window ? window.Width : element.Width;
        double height = element is Window host ? host.Height : element.Height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static void Render(FrameworkElement element, string path, double scale)
    {
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(
            width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void CaptureIconCatalog(string outputDirectory)
    {
        string[] keys =
        [
            "IconDownload", "IconFolder", "IconChevron", "IconBack", "IconClose",
            "IconMinimize", "IconPrevious", "IconNext", "IconSearch", "IconDisplay",
            "IconMic", "IconGear", "IconScissors", "IconTrash", "IconEdit", "IconPlay",
            "IconPause", "IconFullscreen", "IconExitFullscreen", "IconRefresh", "IconCheck",
            "IconWarning", "IconRecord", "IconStop", "IconReload", "IconHelp", "IconGitHub",
            "IconInfo", "IconIssue", "IconFeature",
        ];

        var panel = new WrapPanel { Margin = new Thickness(24) };
        foreach (string key in keys)
        {
            var tile = new StackPanel { Width = 132, Height = 92 };
            bool isFilled = key is "IconGitHub" or "IconRecord" or "IconStop";
            tile.Children.Add(new System.Windows.Shapes.Path
            {
                Data = (Geometry)Application.Current.FindResource(key),
                Stroke = isFilled
                    ? Brushes.Transparent
                    : (Brush)Application.Current.FindResource("TextPrimaryBrush"),
                Fill = isFilled
                    ? (Brush)Application.Current.FindResource("TextPrimaryBrush")
                    : Brushes.Transparent,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 8),
            });
            tile.Children.Add(new TextBlock
            {
                Text = key[4..],
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
            });
            panel.Children.Add(tile);
        }

        var surface = new Border
        {
            Width = 700,
            Height = 760,
            Background = (Brush)Application.Current.FindResource("BgWindowBrush"),
            Child = panel,
        };
        Prepare(surface);
        Render(surface, Path.Combine(outputDirectory, "icon-catalog-200.png"), 2.0);
    }
}
