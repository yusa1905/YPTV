using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yptv;

// Centered message over the video: first run, loading, idle hint, can't-play.
public partial class VideoOverlay : UserControl
{
    public VideoOverlay() => InitializeComponent();

    public void ShowEmpty(string? title = null, string? hint = null) =>
        Show(title ?? "No playlist yet", hint ?? "Open a .m3u / .m3u8 file or paste a playlist URL.", buttons: true);

    public void ShowLoading() => Show("Loading playlist…", null);
    public void ShowHint(string text) => Show(text, null, fg2: true);

    public void ShowError(string? channelName)
    {
        Show("Can't play this stream", channelName, error: true);
        RetryButton.Visibility = Visibility.Visible;
    }

    public void Clear() => Center.Visibility = Visibility.Collapsed;

    public void ShowOsd(int number, string name)
    {
        OsdNumber.Text = number > 0 ? number.ToString("D3") : "";
        OsdName.Text = name.ToUpperInvariant();
        Osd.Visibility = Visibility.Visible;
    }

    public void HideOsd() => Osd.Visibility = Visibility.Collapsed;

    public void ShowDigits(string text)
    {
        DigitsText.Text = text;
        Digits.Visibility = Visibility.Visible;
    }

    public void HideDigits() => Digits.Visibility = Visibility.Collapsed;

    public void SetBackdrop(bool opaque) => Root.Background = (Brush)FindResource(opaque ? "VideoBlack" : "Veil");

    void Show(string title, string? hint, bool buttons = false, bool error = false, bool fg2 = false)
    {
        Title.Text = title;
        Title.Foreground = (Brush)FindResource(error ? "VErr" : fg2 ? "VFg2" : "VFg");
        Hint.Text = hint ?? "";
        Hint.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
        Glyph.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        Buttons.Visibility = buttons ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        Center.Visibility = Visibility.Visible;
    }
}
