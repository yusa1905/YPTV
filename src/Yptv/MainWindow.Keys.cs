using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yptv;

// Hotkeys: F11 fullscreen, Up/Down zap, Space pause, M mute, J/L seek -/+10 s (movies, series), B audio, V subtitles,
// digits jump to a channel number, Ctrl+M mini, Ctrl+T on top, Ctrl+, settings, F1 cheat sheet. The list shown to the
// user is Hotkeys.All.
// The video overlay is its own window, so the same handler is attached to it.
public partial class MainWindow
{
    string _digits = "";
    OneShot? _digitTimer;

    void WireKeys()
    {
        PreviewKeyDown += OnKey;
        Overlay.Loaded += (_, _) =>
        {
            if (Window.GetWindow(Overlay) is { } overlayWindow) overlayWindow.PreviewKeyDown += OnKey;
        };
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.M:
                    ToggleMini();
                    break;
                case Key.T:
                    SetAlwaysOnTop(!Settings.Current.AlwaysOnTop);
                    break;
                case Key.OemComma:
                    ShowSettings();
                    break;
                default:
                    return;
            }
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        bool typing = Keyboard.FocusedElement is TextBox;
        if (Keyboard.FocusedElement is ComboBoxItem or MenuItem) return; // open group list or popup menu: arrows belong to it
        ShowFullscreenUi();

        switch (e.Key)
        {
            case Key.F11:
                ToggleFullscreen();
                break;
            case Key.F1:
                ShowShortcuts();
                break;
            case Key.Escape when _digits.Length > 0:
                CancelDigits();
                break;
            case Key.Escape when _fullscreen:
                ExitFullscreen();
                break;
            case Key.Up:
                Channels.Zap(-1);
                break;
            case Key.Down:
                Channels.Zap(1);
                break;
            case Key.Space when !typing:
                TogglePause();
                break;
            case Key.M when !typing:
                ToggleMute();
                break;
            case Key.B when !typing:
                CycleAudio();
                break;
            case Key.V when !typing:
                CycleSubtitle();
                break;
            case Key.A when !typing:
                CycleAspect();
                break;
            case Key.D when !typing:
                ToggleDeinterlace();
                break;
            case Key.J when !typing:
                SeekBy(-10_000);
                break;
            case Key.L when !typing:
                SeekBy(10_000);
                break;
            case Key.Enter when _digits.Length > 0:
                CommitDigits();
                break;
            case >= Key.D0 and <= Key.D9 when !typing:
                AddDigit(e.Key - Key.D0);
                break;
            case >= Key.NumPad0 and <= Key.NumPad9 when !typing:
                AddDigit(e.Key - Key.NumPad0);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Type a channel number, then wait 1.5 s or press Enter.
    void AddDigit(int digit)
    {
        if (_digits.Length >= 5) return;
        _digits += digit;
        Overlay.ShowDigits(_digits + "_");
        (_digitTimer ??= new OneShot(TimeSpan.FromMilliseconds(1500), CommitDigits)).Restart();
    }

    void CommitDigits()
    {
        var text = _digits;
        CancelDigits();
        if (int.TryParse(text, out var number)) Channels.SelectNumber(number);
    }

    void CancelDigits()
    {
        _digitTimer?.Stop();
        _digits = "";
        Overlay.HideDigits();
    }
}
