using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace Yptv;

// Bottom status bar. Setters only; MainWindow decides what to show.
public partial class StatusStrip : UserControl
{
    public StatusStrip() => InitializeComponent();

    // dotBrush is a theme key (Fg, Fg3, Warn or Err); referenced, not copied, so a theme switch recolors the dot.
    public void SetState(string text, string dotBrush)
    {
        StateText.Text = text;
        Dot.SetResourceReference(Shape.FillProperty, dotBrush);
    }

    public void SetVideo(string? text) => VideoText.Text = text ?? "—";
    public void SetRate(string? text) => RateText.Text = text ?? "—";
    public void SetBuffer(string? text) => BufferText.Text = text ?? "—";
    public void SetCount(int channels) => CountText.Text = $"{channels:N0} ch";
    public void SetSource(string text) => SourceText.Text = text;

    public event Action? HiddenClicked;

    public void SetHidden(int groups)
    {
        var shown = groups > 0 ? Visibility.Visible : Visibility.Collapsed;
        HiddenSep.Visibility = HiddenText.Visibility = shown;
        HiddenText.Text = groups == 1 ? "1 group hidden" : $"{groups:N0} groups hidden";
    }

    public event Action? ExpiryClicked;

    // Xtream playlists only: "Expires 12 Nov 2026 | 33 d", in the error color when expired or under 7 days left.
    public void SetExpiry(AccountInfo? info)
    {
        var shown = info == null ? Visibility.Collapsed : Visibility.Visible;
        ExpirySep.Visibility = ExpiryText.Visibility = shown;
        if (info == null) return;
        var (text, warn) = Xtream.ExpirySummary(info);
        ExpiryText.Text = text;
        ExpiryText.SetResourceReference(TextBlock.ForegroundProperty, warn ? "Err" : "Fg2");
    }

    public event Action? SettingsClicked;

    void Settings_Click(object sender, MouseButtonEventArgs e) => SettingsClicked?.Invoke();

    void Expiry_Click(object sender, MouseButtonEventArgs e) => ExpiryClicked?.Invoke();

    void Hidden_Click(object sender, MouseButtonEventArgs e) => HiddenClicked?.Invoke();

    void Theme_Click(object sender, MouseButtonEventArgs e) =>
        PopupMenu.Show((FrameworkElement)sender, Themes.Names.Select(name =>
            new PopupMenu.Item(name == "System" ? "System (follow Windows)" : name, Settings.Current.Theme == name, () =>
            {
                Themes.Apply(name);
                Settings.SaveSoon();
            })));
}
