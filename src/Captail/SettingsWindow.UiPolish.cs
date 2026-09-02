using System.Windows;
using System.Windows.Controls;

namespace Captail;

public partial class SettingsWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CompactNvencSettingsRow();
    }

    private void CompactNvencSettingsRow()
    {
        NvencSettingsRow.SetResourceReference(
            FrameworkElement.ToolTipProperty,
            "L.Help.NvencLowOverhead");
        ToolTipService.SetInitialShowDelay(NvencSettingsRow, 250);
        ToolTipService.SetShowDuration(NvencSettingsRow, 12000);

        if (NvencSettingsRow.Children.Count > 0 &&
            NvencSettingsRow.Children[0] is StackPanel labelPanel &&
            labelPanel.Children.Count > 1 &&
            labelPanel.Children[1] is TextBlock inlineHelp)
        {
            inlineHelp.Visibility = Visibility.Collapsed;
        }

        LowOverheadAqBox.Margin = new Thickness(2, 6, 0, 0);
    }
}
