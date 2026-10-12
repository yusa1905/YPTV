using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yptv;

// LCD channel display + transport buttons. Buttons are public; MainWindow wires them.
public partial class Deck : UserControl
{
    public Deck()
    {
        InitializeComponent();
        Seek.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Seek_Released), true);
    }

    public void SetChannel(int number, string? name, string? meta)
    {
        var text = number > 0 ? number.ToString("D3") : "";
        Number.Text = text;
        Ghost.Text = new string('8', Math.Max(3, text.Length));
        LcdName.Text = name?.ToUpperInvariant() ?? "NO CHANNEL";
        LcdMeta.Text = meta ?? "";
    }

    public void SetMeta(string meta) => LcdMeta.Text = meta;

    // Mini mode: the window is too narrow for the LCD strip, the channel shows in the OSD instead.
    public void SetCompact(bool compact) => Lcd.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

    // Movies / episodes show the seek row instead of the meta line.
    public void SetVod(bool vod)
    {
        SeekRow.Visibility = vod ? Visibility.Visible : Visibility.Collapsed;
        LcdMeta.Visibility = vod ? Visibility.Collapsed : Visibility.Visible;
        if (!vod) return;
        _shownSecond = _shownLength = -1;
        SetTime(0, 0);
    }

    // Slider position 0..1000 is a fraction of the length; a drag only seeks when the mouse is released.
    public event Action<float>? SeekRequested;
    bool _programmatic, _dragPending;
    long _shownSecond = -1, _shownLength = -1;

    public void SetTime(long timeMs, long lengthMs)
    {
        if (SeekRow.Visibility != Visibility.Visible) return;
        long second = timeMs / 1000;
        if (second == _shownSecond && lengthMs / 1000 == _shownLength) return; // cheap when nothing visible changed
        _shownSecond = second;
        _shownLength = lengthMs / 1000;
        Seek.IsEnabled = lengthMs > 0;
        TimeText.Text = $"{Format(timeMs)} / {Format(lengthMs)}";
        if (Seek.IsMouseCaptureWithin) return; // the user is dragging
        _programmatic = true;
        Seek.Value = lengthMs > 0 ? Math.Clamp(timeMs * 1000.0 / lengthMs, 0, 1000) : 0;
        _programmatic = false;
    }

    void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_programmatic) return;
        if (Seek.IsMouseCaptureWithin) _dragPending = true; // dragging: seek on release
        else SeekRequested?.Invoke((float)(e.NewValue / 1000));
    }

    void Seek_Released(object sender, MouseButtonEventArgs e)
    {
        if (!_dragPending) return;
        _dragPending = false;
        SeekRequested?.Invoke((float)(Seek.Value / 1000));
    }

    static string Format(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes}:{t.Seconds:D2}";
    }
    public void SetPlaying(bool playing) => PlayPause.Content = playing ? "" : "";
    public void SetFullscreen(bool on) => Fullscreen.Content = on ? "" : "";

    public void SetMuted(bool muted)
    {
        Mute.IsChecked = muted;
        Mute.Content = muted ? "" : "";
    }
}
