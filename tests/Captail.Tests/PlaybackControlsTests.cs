using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Captail;
using Xunit;

namespace Captail.Tests;

public sealed class PlaybackControlsTests
{
    [Fact]
    public void PlaybackSettings_RenderAndSynchronizeAcrossSurfaces()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                foreach (string resource in new[] { "Languages/Strings.en.xaml", "Themes/Theme.xaml", "Themes/SettingsFixes.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/Captail;component/{resource}"),
                    });

                var clip = new ReplayClip("preview.mkv", "Preview", null, DateTime.UtcNow,
                    0, TimeSpan.FromSeconds(30), null);
                var window = new ClipEditorWindow(null!, "", clip, _ => { }, ClipWindowMode.Preview);
                var content = (FrameworkElement)window.Content;
                if (Environment.GetEnvironmentVariable("CAPTAIL_PLAYBACK_LAYOUT") is { Length: > 0 } snapshotPath)
                {
                    content.Measure(new Size(900, 736));
                    content.Arrange(new Rect(0, 0, 900, 736));
                    content.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        900, 736, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(snapshotPath);
                    encoder.Save(output);
                }
                // Instantiate all three toolbar templates without opening a native player.
                ((FrameworkElement)window.FindName("NormalPlaybackBar")).Visibility = Visibility.Visible;
                ((FrameworkElement)window.FindName("FullscreenControlBar")).Visibility = Visibility.Visible;
                content.Measure(new Size(900, 736));
                content.Arrange(new Rect(0, 0, 900, 736));
                content.UpdateLayout();
                var controls = Descendants(content).ToArray();
                var volumes = controls.OfType<Slider>().Where(s =>
                    System.Windows.Automation.AutomationProperties.GetName(s) == "Volume").ToArray();
                var speeds = controls.OfType<ComboBox>().ToArray();
                Assert.Equal(3, volumes.Length);
                Assert.Equal(3, speeds.Length);
                Assert.All(volumes, volume => Assert.Equal(100, volume.Value));
                Assert.All(speeds, speed => Assert.Equal(3, speed.SelectedIndex));

                volumes[0].Value = 35;
                speeds[1].SelectedIndex = 5;
                Assert.Equal(35, window.PlaybackVolume);
                Assert.Equal(5, window.PlaybackSpeedIndex);
                window.PlaybackVolume = 70;
                window.PlaybackSpeedIndex = 0;
                window.PlaybackMuted = true;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Assert.All(volumes, volume => Assert.Equal(70, volume.Value));
                Assert.All(speeds, speed => Assert.Equal(0, speed.SelectedIndex));
                var mutes = controls.OfType<CheckBox>().Where(c =>
                    System.Windows.Automation.AutomationProperties.GetName(c) == "Mute").ToArray();
                Assert.Equal(3, mutes.Length);
                Assert.All(mutes, mute => Assert.True(mute.IsChecked));

                window.Close();
                app.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Playback controls layout timed out.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child))
                yield return descendant;
        }
    }
}
