namespace Captail;

public partial class SettingsWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FixAboutPopupInteraction();
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
}
