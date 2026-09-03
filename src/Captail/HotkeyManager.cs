using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Captail;

/// <summary>Global hotkeys for saving a replay and toggling the replay buffer.</summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int SaveHotkeyId = 1;
    private const int ToggleHotkeyId = 2;
    private const int RecordHotkeyId = 3;
    private const int OpenAppHotkeyId = 4;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    private readonly HwndSource _source;
    private (uint Modifiers, uint Vk)? _saveBinding;
    private (uint Modifiers, uint Vk)? _toggleBinding;
    private (uint Modifiers, uint Vk)? _recordBinding;
    private (uint Modifiers, uint Vk)? _openAppBinding;

    public event Action? SaveRequested;
    public event Action? ToggleRequested;
    public event Action? RecordRequested;
    public event Action? OpenAppRequested;

    public HotkeyManager(
        string saveHotkey,
        string toggleHotkey,
        string recordHotkey = "Ctrl+Shift+F11",
        string openAppHotkey = "Ctrl+Shift+F8")
    {
        _source = new HwndSource(new HwndSourceParameters("CaptailHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            HwndSourceHook = WndProc,
        });
        Rebind(saveHotkey, toggleHotkey, recordHotkey, openAppHotkey);
    }

    public void Rebind(string saveHotkey, string toggleHotkey) =>
        Rebind(saveHotkey, toggleHotkey, "Ctrl+Shift+F11", "Ctrl+Shift+F8");

    public void Rebind(string saveHotkey, string toggleHotkey, string recordHotkey) =>
        Rebind(saveHotkey, toggleHotkey, recordHotkey, "Ctrl+Shift+F8");

    public void Rebind(
        string saveHotkey,
        string toggleHotkey,
        string recordHotkey,
        string openAppHotkey)
    {
        var newSave = Parse(saveHotkey);
        var newToggle = Parse(toggleHotkey);
        var newRecord = Parse(recordHotkey);
        var newOpenApp = Parse(openAppHotkey);
        if (!AreDistinct(saveHotkey, toggleHotkey, recordHotkey, openAppHotkey))
            throw new InvalidOperationException(
                Localization.Text("L.Hotkey.MustDiffer"));

        if (_saveBinding == newSave &&
            _toggleBinding == newToggle &&
            _recordBinding == newRecord &&
            _openAppBinding == newOpenApp)
        {
            return;
        }

        var oldSave = _saveBinding;
        var oldToggle = _toggleBinding;
        var oldRecord = _recordBinding;
        var oldOpenApp = _openAppBinding;
        UnregisterCurrent();

        bool saveRegistered = false;
        bool toggleRegistered = false;
        bool recordRegistered = false;
        try
        {
            if (!RegisterHotKey(_source.Handle, SaveHotkeyId, newSave.Modifiers, newSave.Vk))
                throw new InvalidOperationException(
                    Localization.Format("L.Hotkey.Occupied", saveHotkey));
            saveRegistered = true;

            if (!RegisterHotKey(_source.Handle, ToggleHotkeyId, newToggle.Modifiers, newToggle.Vk))
                throw new InvalidOperationException(
                    Localization.Format("L.Hotkey.Occupied", toggleHotkey));
            toggleRegistered = true;

            if (!RegisterHotKey(_source.Handle, RecordHotkeyId, newRecord.Modifiers, newRecord.Vk))
                throw new InvalidOperationException(
                    Localization.Format("L.Hotkey.Occupied", recordHotkey));
            recordRegistered = true;

            if (!RegisterHotKey(_source.Handle, OpenAppHotkeyId, newOpenApp.Modifiers, newOpenApp.Vk))
                throw new InvalidOperationException(
                    Localization.Format("L.Hotkey.Occupied", openAppHotkey));

            _saveBinding = newSave;
            _toggleBinding = newToggle;
            _recordBinding = newRecord;
            _openAppBinding = newOpenApp;
        }
        catch
        {
            if (saveRegistered)
                UnregisterHotKey(_source.Handle, SaveHotkeyId);
            if (toggleRegistered)
                UnregisterHotKey(_source.Handle, ToggleHotkeyId);
            if (recordRegistered)
                UnregisterHotKey(_source.Handle, RecordHotkeyId);
            UnregisterHotKey(_source.Handle, OpenAppHotkeyId);
            _saveBinding = null;
            _toggleBinding = null;
            _recordBinding = null;
            _openAppBinding = null;
            Restore(oldSave, oldToggle, oldRecord, oldOpenApp);
            throw;
        }
    }

    public static bool IsValid(string hotkey)
    {
        try
        {
            _ = Parse(hotkey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool AreDistinct(string first, string second)
    {
        try
        {
            return Parse(first) != Parse(second);
        }
        catch
        {
            return false;
        }
    }

    public static bool AreDistinct(string first, string second, string third)
    {
        try
        {
            var p1 = Parse(first);
            var p2 = Parse(second);
            var p3 = Parse(third);
            return p1 != p2 && p1 != p3 && p2 != p3;
        }
        catch
        {
            return false;
        }
    }

    public static bool AreDistinct(string first, string second, string third, string fourth)
    {
        try
        {
            var p1 = Parse(first);
            var p2 = Parse(second);
            var p3 = Parse(third);
            var p4 = Parse(fourth);
            return p1 != p2 && p1 != p3 && p1 != p4 && p2 != p3 && p2 != p4 && p3 != p4;
        }
        catch
        {
            return false;
        }
    }

    public static bool AreDistinct(params string[] hotkeys)
    {
        try
        {
            var set = new HashSet<(uint Modifiers, uint Vk)>();
            foreach (string hotkey in hotkeys)
            {
                if (!set.Add(Parse(hotkey)))
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Restore(
        (uint Modifiers, uint Vk)? save,
        (uint Modifiers, uint Vk)? toggle,
        (uint Modifiers, uint Vk)? record,
        (uint Modifiers, uint Vk)? openApp)
    {
        if (save is { } saveBinding &&
            RegisterHotKey(_source.Handle, SaveHotkeyId, saveBinding.Modifiers, saveBinding.Vk))
        {
            _saveBinding = saveBinding;
        }

        if (toggle is { } toggleBinding &&
            RegisterHotKey(_source.Handle, ToggleHotkeyId, toggleBinding.Modifiers, toggleBinding.Vk))
        {
            _toggleBinding = toggleBinding;
        }

        if (record is { } recordBinding &&
            RegisterHotKey(_source.Handle, RecordHotkeyId, recordBinding.Modifiers, recordBinding.Vk))
        {
            _recordBinding = recordBinding;
        }

        if (openApp is { } openAppBinding &&
            RegisterHotKey(_source.Handle, OpenAppHotkeyId, openAppBinding.Modifiers, openAppBinding.Vk))
        {
            _openAppBinding = openAppBinding;
        }
    }

    private void UnregisterCurrent()
    {
        if (_saveBinding is not null)
            UnregisterHotKey(_source.Handle, SaveHotkeyId);
        if (_toggleBinding is not null)
            UnregisterHotKey(_source.Handle, ToggleHotkeyId);
        if (_recordBinding is not null)
            UnregisterHotKey(_source.Handle, RecordHotkeyId);
        if (_openAppBinding is not null)
            UnregisterHotKey(_source.Handle, OpenAppHotkeyId);
        _saveBinding = null;
        _toggleBinding = null;
        _recordBinding = null;
        _openAppBinding = null;
    }

    private static (uint Modifiers, uint Vk) Parse(string hotkey)
    {
        uint modifiers = 0;
        uint vk = 0;
        int keyCount = 0;
        foreach (string rawPart in hotkey.Split('+'))
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
                throw new FormatException(
                    Localization.Format("L.Hotkey.ParseError", hotkey));
            switch (part.ToUpperInvariant())
            {
                case "CTRL": modifiers |= MOD_CONTROL; break;
                case "SHIFT": modifiers |= MOD_SHIFT; break;
                case "ALT": modifiers |= MOD_ALT; break;
                default:
                    keyCount++;
                    var key = Enum.Parse<Key>(NormalizeKeyName(part), ignoreCase: true);
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        if (vk == 0 || keyCount != 1)
            throw new FormatException(
                Localization.Format("L.Hotkey.ParseError", hotkey));
        return (modifiers, vk);
    }

    private static string NormalizeKeyName(string name) => name.Length == 1 && char.IsDigit(name[0])
        ? "D" + name
        : name;

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY)
            return 0;

        if (wParam == SaveHotkeyId)
            SaveRequested?.Invoke();
        else if (wParam == ToggleHotkeyId)
            ToggleRequested?.Invoke();
        else if (wParam == RecordHotkeyId)
            RecordRequested?.Invoke();
        else if (wParam == OpenAppHotkeyId)
            OpenAppRequested?.Invoke();
        else
            return 0;

        handled = true;
        return 0;
    }

    public void Dispose()
    {
        UnregisterCurrent();
        _source.Dispose();
    }
}
