using System.Windows;

namespace Yptv;

// Mini mode (Ctrl+M): only the video; the deck and zap OSD appear on mouse/key activity like in fullscreen (same
// overlay code). The normal window size is remembered in memory, the mini size and both flags persist.
// Always on top (Ctrl+T, pin button) is independent and works in both modes. Also: settings and shortcut dialogs.
public partial class MainWindow
{
    bool _mini, _normalMaximized;
    Size _normalSize;

    void WireMini()
    {
        _normalSize = new Size(Width, Height);
        Deck.Pin.Click += (_, _) => SetAlwaysOnTop(Deck.Pin.IsChecked == true);
        Status.SettingsClicked += ShowSettings;
        SetAlwaysOnTop(Settings.Current.AlwaysOnTop);
        if (Settings.Current.Mini) EnterMini();
    }

    internal string? LibVlcVersion => _player?.Version;

    internal void ShowSettings() => new SettingsDialog(this) { Owner = this }.ShowDialog();

    internal void ShowShortcuts() => new ShortcutsDialog { Owner = this }.ShowDialog();

    void SetAlwaysOnTop(bool on)
    {
        Topmost = on; // the owned video overlay window follows its owner
        Deck.Pin.IsChecked = on;
        Settings.Current.AlwaysOnTop = on;
        Settings.SaveSoon();
    }

    void ToggleMini()
    {
        if (_mini) ExitMini();
        else EnterMini();
    }

    void EnterMini()
    {
        if (_mini || _fullscreen) return;
        _mini = true;
        _normalMaximized = WindowState == WindowState.Maximized;
        _normalSize = _normalMaximized ? RestoreBounds.Size : new Size(Width, Height);
        WindowState = WindowState.Normal;
        MinWidth = 460;
        MinHeight = 200;
        Width = Settings.Current.MiniWidth;
        Height = Settings.Current.MiniHeight;

        _panelWidth = PanelColumn.Width;
        PanelColumn.MinWidth = 0;
        PanelColumn.Width = new GridLength(0);
        Channels.Visibility = Splitter.Visibility = Status.Visibility = DeckHost.Visibility = Visibility.Collapsed;
        DeckHost.Child = null;
        Overlay.FsDeck.Child = Deck;
        Deck.SetCompact(true);
        Settings.Current.Mini = true;
        Settings.SaveSoon();
        ShowFullscreenUi();
        ResyncVideo();
    }

    void ExitMini()
    {
        if (!_mini) return;
        _mini = false;
        if (WindowState == WindowState.Normal)
        {
            Settings.Current.MiniWidth = Width;
            Settings.Current.MiniHeight = Height;
        }
        Settings.Current.Mini = false;
        Settings.SaveSoon();
        _hideUi?.Stop();
        Overlay.FsDeck.Child = null;
        Overlay.FsDeck.Visibility = Visibility.Collapsed;
        Overlay.ClearValue(CursorProperty);
        Overlay.HideOsd();
        DeckHost.Child = Deck;
        Deck.SetCompact(false);

        PanelColumn.MinWidth = 240;
        PanelColumn.Width = _panelWidth;
        Channels.Visibility = Splitter.Visibility = Status.Visibility = DeckHost.Visibility = Visibility.Visible;
        WindowState = WindowState.Normal;
        MinWidth = 960;
        MinHeight = 600;
        Width = _normalSize.Width;
        Height = _normalSize.Height;
        if (_normalMaximized) WindowState = WindowState.Maximized;
        ResyncVideo();
    }
}
