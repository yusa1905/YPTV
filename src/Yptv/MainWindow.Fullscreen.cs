using System.Windows;
using System.Windows.Input;

namespace Yptv;

// Fullscreen = borderless + maximized, video only. The deck moves into the overlay (inside VideoSurface),
// because anything else drawn over the video HWND would be clipped by the airspace rule.
public partial class MainWindow
{
    bool _fullscreen;
    WindowState _restoreState;
    GridLength _panelWidth;
    Point _lastMouse;
    OneShot? _hideUi, _hideOsd;

    void WireFullscreen()
    {
        Deck.Fullscreen.Click += (_, _) => ToggleFullscreen();
        Overlay.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ToggleFullscreen();
        };
        Overlay.MouseMove += OnOverlayMouseMove;
        Overlay.MouseWheel += (_, e) => Deck.Volume.Value += e.Delta > 0 ? 5 : -5;
        Overlay.FsDeck.MouseLeftButtonDown += (_, e) => e.Handled = true; // double-click on the deck must not toggle
    }

    // The overlay deck and OSD are used by fullscreen and by mini mode.
    bool OverlayUi => _fullscreen || _mini;

    void ToggleFullscreen()
    {
        if (_mini) ExitMini(); // fullscreen from mini mode returns to the normal window afterwards
        if (_fullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    void EnterFullscreen()
    {
        _fullscreen = true;
        _restoreState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        WindowState = WindowState.Normal; // re-maximize after dropping the frame so the window covers the taskbar
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;

        _panelWidth = PanelColumn.Width;
        PanelColumn.MinWidth = 0;
        PanelColumn.Width = new GridLength(0);
        Channels.Visibility = Splitter.Visibility = Status.Visibility = DeckHost.Visibility = Visibility.Collapsed;
        DeckHost.Child = null;
        Overlay.FsDeck.Child = Deck;
        Deck.SetFullscreen(true);
        ShowFullscreenUi();
        ResyncVideo();
    }

    // Once the layout pass has finished, make the video host, LibVLC's video window and the overlay agree again.
    // LibVLC rebuilds its picture size from the scale/aspect setters, which fixes the stretched frame after a resize.
    internal void ResyncVideo()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Video.Resync();
            if (_player != null) ApplyAspect(_player.MediaPlayer);
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        (_resyncTimer ??= new OneShot(TimeSpan.FromMilliseconds(250), () => { if (_player != null) ApplyAspect(_player.MediaPlayer); })).Restart();
    }

    OneShot? _resyncTimer;

    void ExitFullscreen()
    {
        _fullscreen = false;
        _hideUi?.Stop();
        Overlay.FsDeck.Child = null;
        Overlay.FsDeck.Visibility = Visibility.Collapsed;
        Overlay.ClearValue(CursorProperty);
        Overlay.HideOsd();
        DeckHost.Child = Deck;
        Deck.SetFullscreen(false);

        PanelColumn.MinWidth = 240;
        PanelColumn.Width = _panelWidth;
        Channels.Visibility = Splitter.Visibility = Status.Visibility = DeckHost.Visibility = Visibility.Visible;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        if (_restoreState == WindowState.Maximized) WindowState = WindowState.Maximized;
        ResyncVideo();
    }

    // Deck and cursor appear on mouse/key activity and hide 2.5 s after the last one.
    void ShowFullscreenUi()
    {
        if (!OverlayUi) return;
        Overlay.FsDeck.Visibility = Visibility.Visible;
        Overlay.ClearValue(CursorProperty);
        (_hideUi ??= new OneShot(TimeSpan.FromSeconds(2.5), HideFullscreenUi)).Restart();
    }

    void HideFullscreenUi()
    {
        if (!OverlayUi) return;
        if (Overlay.FsDeck.IsMouseOver || PopupMenu.IsOpen)
        {
            _hideUi!.Restart(); // hovering the deck keeps it
            return;
        }
        Overlay.FsDeck.Visibility = Visibility.Collapsed;
        Overlay.Cursor = Cursors.None;
    }

    void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(Overlay);
        if (position == _lastMouse) return; // showing/hiding the deck raises synthetic moves; ignore them
        _lastMouse = position;
        ShowFullscreenUi();
    }

    void ShowOsd(ChannelRow row)
    {
        if (!OverlayUi) return; // windowed mode already has the LCD strip
        Overlay.ShowOsd(row.Number, row.Name);
        (_hideOsd ??= new OneShot(TimeSpan.FromSeconds(3), Overlay.HideOsd)).Restart();
    }
}
